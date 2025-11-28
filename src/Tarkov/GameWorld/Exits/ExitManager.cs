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
                            
                            if (_isPMC)
                            {
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
                        }
                    };
                }
            };
            map.Execute();
            map.Dispose();

            if (_isPMC)
            {
                entryPointName = Memory.ReadUnicodeString(entryPointPtr);
            }
            
            //both pmc and scav use exfiltration points, but scav ignores entry point filtering
            using var exfilArray = UnityArray<ulong>.Create(exfilArrayAddr, false);
            foreach (var exfilAddr in exfilArray)
            {
                if (!_isPMC) 
                {
                    var eligibleIdsAddr = Memory.ReadPtr(exfilAddr + Offsets.ExfiltrationPoint.EligibleIds, false);
                    var eligibleIdsList = UnityList<ulong>.Create(eligibleIdsAddr, false);
                    if (eligibleIdsList.Count == 0)
                    {
                        continue;
                    }
                }


                var namePtr = Memory.ReadPtrChain(exfilAddr, false, new[] { Offsets.ExfiltrationPoint.Settings, Offsets.ExitTriggerSettings.Name });
                var exfilName = Memory.ReadUnicodeString(namePtr)?.Trim();
                //skip for scav, no entry point filtering so we can just compare all exfil names directly
                ulong eligibleEntryPointsArray = Memory.ReadPtr(exfilAddr + Offsets.ExfiltrationPoint.EligibleEntryPoints, false);
                using var eligibleEntryPoints = UnityArray<ulong>.Create(eligibleEntryPointsArray, false);
                foreach (var eligibleEntryPointAddr in eligibleEntryPoints)
                {
                    string entryPointIDStr = Memory.ReadUnicodeString(eligibleEntryPointAddr);
                    if (entryPointIDStr.Equals(entryPointName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (TarkovDataManager.MapData.TryGetValue(_mapId, out var mapData))
                        {
                            // Filter extracts by PMC/scav/shared to reduce comparison set
                            var extracts = (_isPMC
                                ? mapData.Extracts.Where(x => x.IsShared || x.IsPmc)
                                : mapData.Extracts.Where(x => !x.IsPmc))
                                .ToList();

                            Debug.WriteLine($"[ExitManager] Adding Exfil: {exfilName} for Entry Point: {entryPointName}");

                            // Print filtered extract names for debugging
                            var extractNames = extracts.Select(ep => ep.Name ?? "<null>").ToArray();
                            Debug.WriteLine($"[ExitManager] Map extracts ({extractNames.Length}): {string.Join(", ", extractNames)}");

                            // Try matching using several strategies:
                            // 1) exact case-insensitive (existing)
                            // 2) normalized (letters+digits)
                            // 3) normalized after removing common prefixes / replacing separators
                            // 4) relaxed fuzzy (small Levenshtein distance) on normalized tokens
                            // 5) hardcoded known problematic cases
                            bool matchedAny = false;

                            // 1) exact (case-insensitive)
                            var exactMatches = mapData.Extracts.Where(ep => !string.IsNullOrEmpty(exfilName) && ep.Name.Equals(exfilName, StringComparison.OrdinalIgnoreCase)).ToList();
                            if (exactMatches.Any())
                            {
                                Debug.WriteLine($"[ExitManager] Exact match(es) found for '{exfilName}': {string.Join(", ", exactMatches.Select(m => m.Name))}");
                                foreach (var ex in exactMatches) list.Add(new Exfil(ex));
                                matchedAny = true;
                            }

                            // helper normalizer
                            static string Normalize(string s) => new string((s ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
                            static string RemoveCommonPrefix(string s)
                            {
                                if (string.IsNullOrEmpty(s)) return s;
                                var prefixes = new[] { "exfil_", "exfil", "exit_", "customs_", "customs", "sniper_", "pmc_", "scav_" };
                                var lower = s.ToLowerInvariant();
                                foreach (var p in prefixes)
                                {
                                    if (lower.StartsWith(p))
                                    {
                                        return s.Substring(p.Length);
                                    }
                                }
                                return s;
                            }
                            static string InsertDashBetweenLettersAndDigits(string s)
                            {
                                if (string.IsNullOrEmpty(s)) return s;
                                // turn "zb013" -> "zb-013" (useful for tags like ZB013 -> ZB-013)
                                return Regex.Replace(s, "([A-Za-z]+)(\\d+)", "$1-$2");
                            }

                            if (!matchedAny && !string.IsNullOrEmpty(exfilName))
                            {
                                // 2) normalized alnum
                                var normEx = Normalize(exfilName);
                                var normalizedMatches = mapData.Extracts.Where(ep => Normalize(ep.Name).Equals(normEx)).ToList();
                                if (normalizedMatches.Any())
                                {
                                    Debug.WriteLine($"[ExitManager] Normalized match(es) for '{exfilName}': {string.Join(", ", normalizedMatches.Select(m => m.Name))}");
                                    foreach (var ex in normalizedMatches) list.Add(new Exfil(ex));
                                    matchedAny = true;
                                }
                                else
                                {
                                    // 3) remove prefixes / convert separators / insert dash
                                    var cleaned = RemoveCommonPrefix(exfilName);
                                    cleaned = cleaned.Replace('_', ' ').Replace('-', ' ');
                                    cleaned = InsertDashBetweenLettersAndDigits(cleaned);
                                    var normCleaned = Normalize(cleaned);
                                    var cleanedMatches = mapData.Extracts.Where(ep => Normalize(RemoveCommonPrefix(ep.Name).Replace('_', ' ').Replace('-', ' ')).Equals(normCleaned)).ToList();
                                    if (cleanedMatches.Any())
                                    {
                                        Debug.WriteLine($"[ExitManager] Cleaned match(es) for '{exfilName}' -> '{cleaned}': {string.Join(", ", cleanedMatches.Select(m => m.Name))}");
                                        foreach (var ex in cleanedMatches) list.Add(new Exfil(ex));
                                        matchedAny = true;
                                    }
                                }
                            }

                            // 4) fuzzy fallback (Levenshtein on normalized strings) - choose best candidate if distance small
                            if (!matchedAny && !string.IsNullOrEmpty(exfilName))
                            {
                                var normEx = Normalize(exfilName);
                                var candidates = mapData.Extracts.Select(ep => new { Ep = ep, Norm = Normalize(ep.Name) }).ToList();
                                var best = candidates.Select(c => new { c.Ep, c.Norm, Dist = LevenshteinDistance(normEx, c.Norm) })
                                                     .OrderBy(x => x.Dist)
                                                     .FirstOrDefault();
                                if (best != null && best.Dist <= 2) // threshold (tweak as needed)
                                {
                                    Debug.WriteLine($"[ExitManager] Fuzzy-match for '{exfilName}' -> '{best.Ep.Name}' (distance {best.Dist})");
                                    list.Add(new Exfil(best.Ep));
                                    matchedAny = true;
                                }
                                else
                                {
                                    Debug.WriteLine($"[ExitManager] No exact/normalized/relaxed match for memory exfil '{exfilName}'. Best candidate: '{best?.Ep?.Name}' dist={best?.Dist}");
                                }
                            }
                            // 5) hardcoded matches for known problematic cases
                            if (!matchedAny)
                            {
                                var hardcodedMatches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    // { memoryExfilName, tarkovDataExtractName }
                                    { "customs_sniper_exit", "Railroad Passage (Flare)" },
                                    { "Factory Gate", "Friendship Bridge (Co-Op)" },
                                    { "South V-Ex", "Bridge V-Ex" },
                                    { "wood_sniper_exit", "Power Line Passage (Flare)" },
                                };
                                if (hardcodedMatches.TryGetValue(exfilName, out var targetExtractName))
                                {
                                    var hardcodedExtract = mapData.Extracts.FirstOrDefault(ep => ep.Name.Equals(targetExtractName, StringComparison.OrdinalIgnoreCase));
                                    if (hardcodedExtract != null)
                                    {
                                        Debug.WriteLine($"[ExitManager] Hardcoded match for '{exfilName}' -> '{hardcodedExtract.Name}'");
                                        list.Add(new Exfil(hardcodedExtract));
                                        matchedAny = true;
                                    }
                                }
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