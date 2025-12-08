/*
 * Lone EFT DMA Radar
 * Brought to you by Lone (Lone DMA)
 * 
MIT License

Copyright (c) 2025 Lone DMA

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
 *
*/

using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using TwitchLib.Api.Helix.Models.Entitlements;
using TwitchLib.Api.Helix.Models.Raids;
using VmmSharpEx.Extensions;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Exits
{
    /// <summary>
    /// List of PMC/Scav 'Exits' in Local Game World and their position/status.
    /// </summary>
    public sealed class ExitManager : IReadOnlyCollection<IExitPoint>
    {
        private IReadOnlyList<IExitPoint> _exits;
        private readonly object _sync = new();

        private readonly ulong _localGameWorld;
        private readonly string _mapId;
        private readonly bool _isPMC;
        private ulong exfilArrayAddr;
        private ulong secretExfilArrayAddr;
        private ulong entryPointPtr;
        private string entryPointName;
        private readonly LocalPlayer _localPlayer;

        public ExitManager(ulong localGameWorld, string mapId, LocalPlayer localPlayer)
        {
            _localGameWorld = localGameWorld;
            _isPMC = localPlayer.IsPmc;
            _mapId = mapId;
            _localPlayer = localPlayer;
        }

        /// <summary>
        /// Public snapshot of current exits. Never returns null.
        /// </summary>
        public IReadOnlyList<IExitPoint> Exits => _exits ?? Array.Empty<IExitPoint>();

        private void Init()
        {
            var list = new List<IExitPoint>();

            try
            {
                var exfiltrationController = Memory.ReadPtr(_localGameWorld + Offsets.GameWorld.ExfiltrationController);
                exfilArrayAddr = Memory.ReadPtr(exfiltrationController + (_isPMC ? Offsets.ExfiltrationController.ExfiltrationPoints : Offsets.ExfiltrationController.ScavExfiltrationPoints));
                secretExfilArrayAddr = Memory.ReadPtr(exfiltrationController + Offsets.ExfiltrationController.SecretExfiltrationPoints);

                if (_isPMC)
                {
                    entryPointPtr = Memory.ReadPtr(_localPlayer.Info + Offsets.PlayerInfo.EntryPoint);
                    entryPointName = Memory.ReadUnityString(entryPointPtr);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExitManager] Init Error: {ex}");
                // preserve empty list in case of error to avoid null consumers
                lock (_sync)
                {
                    _exits = list;
                }
                return;
            }

            // Populate exfils from the game memory
            try
            {
                using var exfilArray = UnityArray<ulong>.Create(exfilArrayAddr, false);
                foreach (var exfilAddr in exfilArray)
                {
                    var namePtr = Memory.ReadPtrChain(exfilAddr, false, new[] { Offsets.ExfiltrationPoint.Settings, Offsets.ExitTriggerSettings.Name });
                    var exfilName = Memory.ReadUnityString(namePtr)?.Trim();

                    if (_isPMC)
                    {
                        ulong eligibleEntryPointsArray = Memory.ReadPtr(exfilAddr + Offsets.ExfiltrationPoint.EligibleEntryPoints, false);
                        using var eligibleEntryPoints = UnityArray<ulong>.Create(eligibleEntryPointsArray, false);
                        foreach (var eligibleEntryPointAddr in eligibleEntryPoints)
                        {
                            string entryPointIDStr = Memory.ReadUnityString(eligibleEntryPointAddr);
                            if (!string.IsNullOrEmpty(entryPointIDStr) && entryPointIDStr.Equals(entryPointName, StringComparison.OrdinalIgnoreCase))
                            {
                                list.Add(new Exfil(exfilAddr, exfilName, _mapId, _isPMC));
                                break; // matched entry point — no need to check other entry points for this exfil
                            }
                        }
                    }
                    else
                    {
                        var eligibleIdsAddr = Memory.ReadPtr(exfilAddr + Offsets.ExfiltrationPoint.EligibleIds, false);
                        using var eligibleIdsList = UnityList<ulong>.Create(eligibleIdsAddr, false);
                        if (eligibleIdsList.Count > 0)
                        {
                            list.Add(new Exfil(exfilAddr, exfilName, _mapId, _isPMC));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExitManager] Exfil enumeration Error: {ex}");
                // fall through and still try to include map-defined transit points
            }

            // Add known transit points (and keep room for future inclusion of map extracts)
            if (TarkovDataManager.MapData.TryGetValue(_mapId, out var map))
            {
                foreach (var transit in map.Transits)
                {
                    list.Add(new TransitPoint(transit));
                }
            }

            lock (_sync)
            {
                _exits = list;
            }
        }

        private void EnsureInitialized()
        {
            if (_exits is null)
            {
                lock (_sync)
                {
                    if (_exits is null)
                    {
                        Init();
                    }
                }
            }
        }

        /// <summary>
        /// Refresh statuses for Exfil points. Uses a single Completed handler and reads all prepared addresses in one pass.
        /// This avoids subscribing multiple Completed handlers and avoids per-loop closure pitfalls.
        /// </summary>
        public void Refresh()
        {
            try
            {
                EnsureInitialized();
                ArgumentNullException.ThrowIfNull(_exits, nameof(_exits));

                var exfils = _exits.OfType<Exfil>().ToList();
                if (exfils.Count == 0)
                    return;

                var map = Memory.CreateScatterMap();
                var round = map.AddRound();

                // Prepare addresses and reads once
                var addresses = new List<ulong>(exfils.Count);
                foreach (var exfil in exfils)
                {
                    var addr = exfil.exfilBase + Offsets.ExfiltrationPoint._status;
                    addresses.Add(addr);
                    round.PrepareReadValue<int>(addr);
                }

                // Attach a single Completed handler that iterates the prepared addresses
                round.Completed += (sender, completedRound) =>
                {
                    for (int i = 0; i < exfils.Count; i++)
                    {
                        var addr = addresses[i];
                        if (completedRound.ReadValue<int>(addr, out var exfilStatus))
                        {
                            try
                            {
                                exfils[i].Update((Enums.EExfiltrationStatus)exfilStatus);
                            }
                            catch (Exception updateEx)
                            {
                                Debug.WriteLine($"[ExitManager] Update Exfil Error for addr 0x{addr:X}: {updateEx}");
                            }
                        }
                    }
                };

                map.Execute();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExitManager] Refresh Error: {ex}");
            }
        }

        #region IReadOnlyCollection

        public int Count => Exits.Count;
        public IEnumerator<IExitPoint> GetEnumerator() => Exits.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion
    }
}