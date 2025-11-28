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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
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
        private string entryPointName;
        private readonly LocalPlayer _localPlayer;

        public ExitManager(ulong localGameWorld, string mapId, LocalPlayer localPlayer)
        {
            _localGameWorld = localGameWorld;
            _isPMC = localPlayer.IsPmc;
            _mapId = mapId;
            _localPlayer = localPlayer;
        }

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
                    entryPointName = Memory.ReadUnicodeString(entryPointPtr);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExitManager] Memory read failed during Init: {ex}");
                _exits = list;
                return;
            }

            using var exfilArray = UnityArray<ulong>.Create(exfilArrayAddr, false);
            foreach (var exfilAddr in exfilArray)
            {
                var namePtr = Memory.ReadPtrChain(exfilAddr, false, new[] { Offsets.ExfiltrationPoint.Settings, Offsets.ExitTriggerSettings.Name });
                var exfilName = Memory.ReadUnicodeString(namePtr)?.Trim();

                bool usable = false;

                if (_isPMC)
                {
                    ulong eligibleEntryPointsArray = Memory.ReadPtr(exfilAddr + Offsets.ExfiltrationPoint.EligibleEntryPoints, false);
                    using var eligibleEntryPoints = UnityArray<ulong>.Create(eligibleEntryPointsArray, false);
                    foreach (var eligibleEntryPointAddr in eligibleEntryPoints)
                    {
                        string entryPointIDStr = Memory.ReadUnicodeString(eligibleEntryPointAddr);
                        if (!string.IsNullOrEmpty(entryPointIDStr) && entryPointIDStr.Equals(entryPointName, StringComparison.OrdinalIgnoreCase))
                        {
                            usable = true;
                            break;
                        }
                    }
                    //Debug.WriteLine($"[ExitManager] PMC exfil '{exfilName}' usable={usable}");
                }
                else
                {
                    var eligibleIdsAddr = Memory.ReadPtr(exfilAddr + Offsets.ExfiltrationPoint.EligibleIds, false);
                    using var eligibleIdsList = UnityList<ulong>.Create(eligibleIdsAddr, false);
                    usable = eligibleIdsList.Count > 0;
                    //Debug.WriteLine($"[ExitManager] Scav exfil '{exfilName}' eligibleIdsCount={eligibleIdsList.Count} usable={usable}");
                }

                if (!usable)
                    continue;

                if (!TarkovDataManager.MapData.TryGetValue(_mapId, out var mapData))
                    continue;

                // Filter extracts by PMC/scav/shared to reduce comparison set
                var extracts = (_isPMC
                    ? mapData.Extracts.Where(x => x.IsShared || x.IsPmc)
                    : mapData.Extracts.Where(x => !x.IsPmc))
                    .ToList();

                //Debug.WriteLine($"[ExitManager] Adding Exfil: {exfilName} (isPMC={_isPMC})");

                // Matching strategies (operating on filtered 'extracts'):
                bool matchedAny = false;

                // 1) exact (case-insensitive)
                var exactMatches = extracts.Where(ep => !string.IsNullOrEmpty(exfilName) && ep.Name.Equals(exfilName, StringComparison.OrdinalIgnoreCase)).ToList();
                if (exactMatches.Any())
                {
                    foreach (var ex in exactMatches) list.Add(new Exfil(ex));
                    matchedAny = true;
                }

                // helpers
                static string Normalize(string s) => new string((s ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
                static string RemoveCommonPrefix(string s)
                {
                    if (string.IsNullOrEmpty(s)) return s;
                    var prefixes = new[] { "exfil_", "exfil", "exit_", "customs_", "customs", "sniper_", "pmc_", "scav_" };
                    var lower = s.ToLowerInvariant();
                    foreach (var p in prefixes)
                    {
                        if (lower.StartsWith(p))
                            return s.Substring(p.Length);
                    }
                    return s;
                }
                static string InsertDashBetweenLettersAndDigits(string s)
                {
                    if (string.IsNullOrEmpty(s)) return s;
                    return Regex.Replace(s, "([A-Za-z]+)(\\d+)", "$1-$2");
                }

                // 2) normalized alnum
                if (!matchedAny && !string.IsNullOrEmpty(exfilName))
                {
                    var normEx = Normalize(exfilName);
                    var normalizedMatches = extracts.Where(ep => Normalize(ep.Name).Equals(normEx)).ToList();
                    if (normalizedMatches.Any())
                    {
                        foreach (var ex in normalizedMatches) list.Add(new Exfil(ex));
                        matchedAny = true;
                    }
                    else
                    {
                        // 3) remove prefixes / replace separators / insert dash
                        var cleaned = RemoveCommonPrefix(exfilName).Replace('_', ' ').Replace('-', ' ');
                        cleaned = InsertDashBetweenLettersAndDigits(cleaned);
                        var normCleaned = Normalize(cleaned);
                        var cleanedMatches = extracts.Where(ep => Normalize(RemoveCommonPrefix(ep.Name).Replace('_', ' ').Replace('-', ' ')).Equals(normCleaned)).ToList();
                        if (cleanedMatches.Any())
                        {
                            foreach (var ex in cleanedMatches) list.Add(new Exfil(ex));
                            matchedAny = true;
                        }
                    }
                }

                // 4) fuzzy fallback (Levenshtein on normalized strings)
                if (!matchedAny && !string.IsNullOrEmpty(exfilName))
                {
                    var normEx = Normalize(exfilName);
                    var candidates = extracts.Select(ep => new { Ep = ep, Norm = Normalize(ep.Name) }).ToList();
                    var best = candidates.Select(c => new { c.Ep, c.Norm, Dist = LevenshteinDistance(normEx, c.Norm) })
                                         .OrderBy(x => x.Dist)
                                         .FirstOrDefault();
                    if (best != null && best.Dist <= 2) // threshold (tweak as needed)
                    {
                        list.Add(new Exfil(best.Ep));
                        matchedAny = true;
                    }
                }

                // 5) hardcoded mapping fallback
                if (!matchedAny)
                {
                    var hardcodedMatches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "customs_sniper_exit", "Railroad Passage (Flare)" },
                        { "Factory Gate", "Friendship Bridge (Co-Op)" },
                        { "South V-Ex", "Bridge V-Ex" },
                        { "wood_sniper_exit", "Power Line Passage (Flare)" },
                        { "Custom_scav_pmc", "Boiler Room Basement (Co-op)" },
                    };
                    if (hardcodedMatches.TryGetValue(exfilName, out var targetExtractName))
                    {
                        var hardcodedExtract = extracts.FirstOrDefault(ep => ep.Name.Equals(targetExtractName, StringComparison.OrdinalIgnoreCase));
                        if (hardcodedExtract != null)
                        {
                            list.Add(new Exfil(hardcodedExtract));
                            matchedAny = true;
                        }
                    }
                }
                else
                {
                    Debug.WriteLine($"[ExitManager] No match for memory exfil '{exfilName}'.");
                }
            }

            // Add transits
            if (TarkovDataManager.MapData.TryGetValue(_mapId, out var Transits))
            {
                foreach (var transit in Transits.Transits)
                {
                    list.Add(new TransitPoint(transit));
                }
            }

            _exits = list;
        }

        private static int LevenshteinDistance(string a, string b)
        {
            if (a == null) a = string.Empty;
            if (b == null) b = string.Empty;
            var lenA = a.Length;
            var lenB = b.Length;
            if (lenA == 0) return lenB;
            if (lenB == 0) return lenA;

            var v0 = new int[lenB + 1];
            var v1 = new int[lenB + 1];

            for (int j = 0; j <= lenB; j++) v0[j] = j;

            for (int i = 0; i < lenA; i++)
            {
                v1[0] = i + 1;
                for (int j = 0; j < lenB; j++)
                {
                    int cost = a[i] == b[j] ? 0 : 1;
                    v1[j + 1] = Math.Min(Math.Min(v1[j] + 1, v0[j + 1] + 1), v0[j] + cost);
                }
                var tmp = v0;
                v0 = v1;
                v1 = tmp;
            }
            return v0[lenB];
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