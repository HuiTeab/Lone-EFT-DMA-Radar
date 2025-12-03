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
using LoneEftDmaRadar.UI.Radar.Maps;
using LoneEftDmaRadar.UI.Skia;
using System.Text.RegularExpressions;
using TwitchLib.Api.Helix.Models.Entitlements;
using SkiaSharp;
using System.ComponentModel;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Exits
{
    public class Exfil : IExitPoint, IWorldEntity, IMapEntity, IMouseoverEntity
    {
        public EStatus Status { get; private set; } = EStatus.Closed;
        public Exfil(ulong baseAddr, string exfilName, string mapId, bool IsPmc)
        {
            exfilBase = baseAddr;
            if (!TarkovDataManager.MapData.TryGetValue(mapId, out var mapData)) 
            { 
                return;
            }
            var extracts = (IsPmc
                    ? mapData.Extracts.Where(x => x.IsShared || x.IsPmc)
                    : mapData.Extracts.Where(x => !x.IsPmc))
                    .ToList();

            // Matching strategies (operating on filtered 'extracts'):
            bool matchedAny = false;

            // 1) exact (case-insensitive)
            var exactMatches = extracts.Where(ep => !string.IsNullOrEmpty(exfilName) && ep.Name.Equals(exfilName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exactMatches.Any())
            {
                foreach (var ex in exactMatches)
                {
                    matchedAny = true;
                    Name = ex.Name;
                    _position = ex.Position.AsVector3();
                }
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
                    foreach (var ex in normalizedMatches)
                    {
                        matchedAny = true;
                        Name = ex.Name;
                        _position = ex.Position.AsVector3();
                    }
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
                        foreach (var ex in cleanedMatches)
                        {
                            matchedAny = true;
                            Name = ex.Name;
                            _position = ex.Position.AsVector3();
                        }
                    }
                }
            }

            // 3) hardcoded mapping fallback
            if (!matchedAny)
            {
                var hardcodedMatches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        //Factory
                        //Customs
                        { "customs_sniper_exit", "Railroad Passage (Flare)" },
                        { "Factory Gate", "Friendship Bridge (Co-Op)" },
                        { "Military Checkpoint", "Military Base CP" },
                        //Woods
                        { "South V-Ex", "Bridge V-Ex" },
                        { "wood_sniper_exit", "Power Line Passage (Flare)" },
                        { "Custom_scav_pmc", "Boiler Room Basement (Co-op)" },
                        { "West Border", "RUAF Roadblock" },
                        { "Old Station", "Old Railway Depot" },
                        { "un-sec", "Northern UN Roadblock" },
                        //Interchange
                        { "SE Exfil", "Emercom Checkpoint" },
                        { "NW Exfil", "Railway Exfil" },
                        { "Interchange Cooperation", "Scav Camp (Co-Op)" },
                        //Reserve
                        { "EXFIL_Train", "Armored Train" },
                        { "EXFIL_Bunker_D2", "D-2" },
                        { "EXFIL_Bunker", "Bunker Hermetic Door" },
                        { "Alpinist", "Cliff Descent" },
                        { "EXFIL_ScavCooperation", "Scav Lands (Co-Op)" },
                        { "EXFIL_vent", "Sewer Manhole" },
                        { "Exit1", "Hole in the Wall by the Mountains" },//???
                        { "Exit2", "Heating Pipe" }, //???
                        { "Exit4", "Checkpoint Fence" },
                        { "Exit3", "Depot Hermetic Door" },
                        //Lighthouse
                        { "V-Ex_light", "Road to Military Base V-Ex" },
                        { "tunnel_shared", "Side Tunnel (Co-Op)" },
                        { "Alpinist_light", "Mountain Pass" },
                        { "Shorl_free", "Path to Shoreline" },
                        { "Nothern_Checkpoint", "Northern Checkpoint" },
                        { "Coastal_South_Road", "Southern Road" },
                        //Shorline
                        { "Shorl_V-Ex", "Road to North V-Ex" },
                        { "Road_at_railbridge", "Railway Bridge" },
                        { "Lighthouse_pass", "Path to Lighthouse" },
                        { "Smugglers_Trail_coop", "Smugglers' Path (Co-op)" },
                        { "RedRebel_alp", "Climber's Trail" },
                        //Ground Zero
                        { "Sandbox_VExit", "Police Cordon V-Ex" },
                        { "Unity_free_exit", "Nakatani Basement Stairs" },
                        { "Scav_coop_exit", "Scav Checkpoint (Co-op)" },
                        { "Sniper_exit", "Mira Ave (Flare)" },
                        //Streets of Tarkov
                        { "E8_yard", "Courtyard" },
                        { "E7_car", "Primorsky Ave Taxi V-Ex" },
                        { "E1", "Damaged House" },
                        { "E9_sniper", "Klimov Street (Flare)" },
                        { "E7", "Expo Checkpoint" },
                        { "Exit_E10_coop", "Pinewood Basement (Co-Op)" },
                        { "E6", "Sewer River" },
                        //The Lab

                    };
                if (hardcodedMatches.TryGetValue(exfilName, out var targetExtractName))
                {
                    var hardcodedExtract = extracts.FirstOrDefault(ep => ep.Name.Equals(targetExtractName, StringComparison.OrdinalIgnoreCase));
                    if (hardcodedExtract != null)
                    {
                        matchedAny = true;
                        Name = hardcodedExtract.Name;
                        _position = hardcodedExtract.Position.AsVector3();
                    }
                }
            }

            // Detailed diagnostics when nothing matched
            if (!matchedAny)
            {
                var raw = exfilName ?? "<null>";
                var norm = Normalize(raw);
                var charCodes = string.Join(" ", raw.Select(c => ((int)c).ToString("X4")));
                Debug.WriteLine($"[ExitManager] UNMATCHED memory exfil raw='{raw}' len={raw.Length} norm='{norm}' codes=[{charCodes}]");

                foreach (var ep in extracts)
                {
                    var epName = ep.Name ?? "<null>";
                    var epNorm = Normalize(epName);
                    var exact = !string.IsNullOrEmpty(raw) && epName.Equals(raw, StringComparison.OrdinalIgnoreCase);
                    var normEq = epNorm == norm;
                    Debug.WriteLine($"[ExitManager] CompareExtract: '{epName}' len={epName.Length} norm='{epNorm}' exact={exact} normEq={normEq}");
                }
            }

        }

        public void Update(Enums.EExfiltrationStatus status)
        {
            /// Update Status
            switch (status)
            {
                case Enums.EExfiltrationStatus.NotPresent:
                    Status = EStatus.Closed;
                    break;
                case Enums.EExfiltrationStatus.UncompleteRequirements:
                    Status = EStatus.Pending;
                    break;
                case Enums.EExfiltrationStatus.Countdown:
                    Status = EStatus.Open;
                    break;
                case Enums.EExfiltrationStatus.RegularMode:
                    Status = EStatus.Open;
                    break;
                case Enums.EExfiltrationStatus.Pending:
                    Status = EStatus.Pending;
                    break;
                case Enums.EExfiltrationStatus.AwaitsManualActivation:
                    Status = EStatus.Pending;
                    break;
                case Enums.EExfiltrationStatus.Hidden:
                    Status = EStatus.Closed;
                    break;
            }
        }

        public string Name { get; }

        #region Interfaces

        public ulong exfilBase { get; init; }

        private readonly Vector3 _position;
        public ref readonly Vector3 Position => ref _position;
        public Vector2 MouseoverPosition { get; set; }

        public void Draw(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            var heightDiff = Position.Y - localPlayer.Position.Y;
            var paint = SKPaints.PaintExfil;
            if (Status == EStatus.Open)
            {
                SKPaints.PaintExfil.Color = SKColors.Green;
            }
            else if (Status == EStatus.Pending)
            {
                SKPaints.PaintExfil.Color = SKColors.Yellow;
            }
            else // Closed
            {
                SKPaints.PaintExfil.Color = SKColors.Red;
            }
            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(point.X, point.Y);
            SKPaints.ShapeOutline.StrokeWidth = 2f;
            if (heightDiff > 1.85f) // exfil is above player
            {
                using var path = point.GetUpArrow(6.5f);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paint);
            }
            else if (heightDiff < -1.85f) // exfil is below player
            {
                using var path = point.GetDownArrow(6.5f);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paint);
            }
            else // exfil is level with player
            {
                float size = 4.75f * App.Config.UI.UIScale;
                canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
                canvas.DrawCircle(point, size, paint);
            }
        }

        public void DrawMouseover(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            List<string> lines = new();
            var exfilName = Name;
            exfilName ??= "unknown";
            lines.Add($"{exfilName} ({Status.ToString()})");
            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, string.Join("\n", lines));
        }

        #endregion

        public enum EStatus
        {
            [Description(nameof(Open))] Open,
            [Description(nameof(Pending))] Pending,
            [Description(nameof(Closed))] Closed
        }
    }
}
