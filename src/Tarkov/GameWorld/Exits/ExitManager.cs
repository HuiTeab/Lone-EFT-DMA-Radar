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

using LoneEftDmaRadar.Tarkov.Unity.Collections;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Exits
{
    /// <summary>
    /// List of PMC/Scav 'Exits' in Local Game World and their position/status.
    /// </summary>
    public sealed class ExitManager : IReadOnlyCollection<IExitPoint>
    {
        private IReadOnlyList<IExitPoint> _exits;

        private readonly ulong _localGameWorld;
        private readonly string _mapId;
        private readonly bool _isPMC;
        private ulong exfilArrayAddr;
        private ulong secretExfilArrayAddr;
        private ulong entryPointPtr;
        public ExitManager(ulong localGameWorld, string mapId, bool isPMC)
        {
            _localGameWorld = localGameWorld;
            _isPMC = isPMC;
            _mapId = mapId;
        }

        private void Init()
        {
            var list = new List<IExitPoint>();
            var map = Memory.CreateScatterMap();
            var round1 = map.AddRound();
            var round2 = map.AddRound();
            var round3 = map.AddRound();
            var round4 = map.AddRound();
            round1.PrepareReadPtr(_localGameWorld + Offsets.GameWorld.ExfiltrationController);
            round1.PrepareReadPtr(_localGameWorld + Offsets.GameWorld.MainPlayer);
            round1.Completed += (sender, s1) =>
            {
                if (s1.ReadPtr(_localGameWorld + Offsets.GameWorld.ExfiltrationController, out var exfiltrationController) &&
                    s1.ReadPtr(_localGameWorld + Offsets.GameWorld.MainPlayer, out var mainPlayer))
                {
                    round2.PrepareReadPtr(exfiltrationController + (_isPMC ? Offsets.ExfiltrationController.ExfiltrationPoints : Offsets.ExfiltrationController.ScavExfiltrationPoints));
                    round2.PrepareReadPtr(exfiltrationController + Offsets.ExfiltrationController.SecretExfiltrationPoints);
                    round2.PrepareReadPtr(mainPlayer + Offsets.Player.Profile);
                    round2.Completed += (sender, s2) =>
                    {
                        if (s2.ReadPtr(exfiltrationController + (_isPMC ? Offsets.ExfiltrationController.ExfiltrationPoints : Offsets.ExfiltrationController.ScavExfiltrationPoints), out var exfiltrationPoints) &&
                            s2.ReadPtr(mainPlayer + Offsets.Player.Profile, out var profile) &&
                            s2.ReadPtr(exfiltrationController + Offsets.ExfiltrationController.SecretExfiltrationPoints, out var secretExfiltrationPoints))

                        {
                            exfilArrayAddr = exfiltrationPoints;
                            secretExfilArrayAddr = secretExfiltrationPoints;

                            round3.PrepareReadPtr(profile + Offsets.Profile.Info);
                            round3.Completed += (sender, s3) =>
                            {
                                if (s3.ReadPtr(profile + Offsets.Profile.Info, out var info))
                                {

                                    round4.PrepareReadPtr(info + Offsets.PlayerInfo.EntryPoint);

                                    round4.Completed += (sender, s4) =>
                                    {
                                        if (s4.ReadPtr(info + Offsets.PlayerInfo.EntryPoint, out var entryPoint))
                                        {
                                            entryPointPtr = entryPoint;
                                        }
                                    };
                                }
                            };
                        }
                    };
                }
            };
            map.Execute();
            map.Dispose();

            var entryPointName = Memory.ReadUnicodeString(entryPointPtr);
            using var exfilArray = UnityArray<ulong>.Create(exfilArrayAddr, false);
            foreach (var exfilAddr in exfilArray)
            {
                var namePtr = Memory.ReadPtrChain(exfilAddr, false, new[] { Offsets.ExfiltrationPoint.Settings, Offsets.ExitTriggerSettings.Name});
                var exfilName = Memory.ReadUnicodeString(namePtr)?.Trim();
                
                ulong eligibleEntryPointsArray = Memory.ReadPtr(exfilAddr + Offsets.ExfiltrationPoint.EligibleEntryPoints, false);
                using var eligibleEntryPoints = UnityArray<ulong>.Create(eligibleEntryPointsArray, false);
                foreach (var eligibleEntryPointAddr in eligibleEntryPoints)
                {
                    string entryPointIDStr = Memory.ReadUnicodeString(eligibleEntryPointAddr);
                    if (entryPointIDStr.Equals(entryPointName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (TarkovDataManager.MapData.TryGetValue(_mapId, out var mapData))
                        {
                            //Debug.WriteLine($"[ExitManager] Adding Exfil: {exfilName} for Entry Point: {entryPointName}");
                            var filteredExfils = mapData.Extracts.Where(ep => ep.Name.Equals(exfilName, StringComparison.OrdinalIgnoreCase));
                            foreach (var exfil in filteredExfils)
                            {
                                list.Add(new Exfil(exfil));
                            }   
                        }
                    }
                }
            }
            if (TarkovDataManager.MapData.TryGetValue(_mapId, out var Transits))
            {
                foreach (var transit in Transits.Transits)
                {
                    list.Add(new TransitPoint(transit));
                }
            }
             _exits = list;

        }

        public void Refresh()
        {
            try
            {
                if (_exits is null) // Initialize
                    Init();
                ArgumentNullException.ThrowIfNull(_exits, nameof(_exits));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExitManager] Refresh Error: {ex}");
            }
        }

        #region IReadOnlyCollection

        public int Count => _exits?.Count ?? 0;
        public IEnumerator<IExitPoint> GetEnumerator() => _exits?.GetEnumerator() ?? Enumerable.Empty<IExitPoint>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion
    }
}