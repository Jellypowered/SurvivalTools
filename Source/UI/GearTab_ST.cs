// RimWorld 1.6 / C# 7.3
// Source/UI/GearTab_ST.cs
// From our refactor branch, keep.
// Phase 7: Gear iTab implementation
// - Readable panel inside vanilla Gear tab
// - Tool scoring display with alloc-free loops
// - Performance optimized with pooled buffers

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SurvivalTools.UI
{
    public static class GearTab_ST
    {
        // UI constants
        private const float HEADER_HEIGHT = 30f;
        private const float TOOL_ROW_HEIGHT = 48f; // Two lines with better spacing
        private const float SCROLL_BAR_WIDTH = 16f;
        private const float MARGIN = 8f;

        // UI colors
        private static readonly Color HeaderBgColor = new Color(0.15f, 0.25f, 0.35f, 0.95f);
        private static readonly Color PanelBgColor = new Color(0.08f, 0.08f, 0.08f, 0.9f);
        private static readonly Color RowBgColor = new Color(0.12f, 0.12f, 0.12f, 0.7f);
        private static readonly Color RowAltBgColor = new Color(0.16f, 0.16f, 0.16f, 0.7f);
        private static readonly Color ScoreGoodColor = new Color(0.4f, 0.9f, 0.6f);
        private static readonly Color ScoreMediumColor = new Color(0.9f, 0.8f, 0.3f);
        private static readonly Color WhyTextColor = new Color(0.7f, 0.7f, 0.7f);
        private static readonly Color BorderColor = new Color(0.3f, 0.3f, 0.3f);

        // Cached data
        private static Pawn _cachedPawn;
        private static int _cacheInvalidationKey;
        private static List<ToolDisplayInfo> _cachedToolInfo = new List<ToolDisplayInfo>();
        private static string _cachedModeCarryText = "";
        private static string _cachedGatingText = "";
        private static StringBuilder _tooltipBuilder = new StringBuilder();
        private static Vector2 _scrollPos = Vector2.zero;

        // Pooled arrays for performance
        private static readonly StatDef[] KeyWorkStats = {
            ST_StatDefOf.DiggingSpeed,
            StatDefOf.ConstructionSpeed,
            ST_StatDefOf.TreeFellingSpeed,
            ST_StatDefOf.PlantHarvestingSpeed
        };

        private struct ToolDisplayInfo
        {
            public Thing Tool;
            public string Label;
            public string ScoreText;
            public string WhyText;
            public string TooltipText;
        }

        public static float DesiredWidth
        {
            get
            {
                float baseWidth = 380f;
                return baseWidth * Prefs.UIScale;
            }
        }

        public static void Draw(Rect hostRect, Pawn pawn)
        {
            if (pawn == null) return;

            // Update cache if needed
            UpdateCacheIfNeeded(pawn);

            // Draw layered background with subtle gradient effect
            Widgets.DrawBoxSolid(hostRect, PanelBgColor);

            // Draw border with corner accents
            Widgets.DrawBox(hostRect, 2);

            var contentRect = hostRect.ContractedBy(MARGIN);
            var curY = contentRect.y;

            // Header with background
            var headerRect = new Rect(contentRect.x, curY, contentRect.width, HEADER_HEIGHT);
            Widgets.DrawBoxSolid(headerRect, HeaderBgColor);
            DrawHeader(headerRect);
            curY += HEADER_HEIGHT + 6f;

            // Tool list with scroll view
            var listRect = new Rect(contentRect.x, curY, contentRect.width, contentRect.yMax - curY);
            DrawToolList(listRect, pawn);
        }

        private static void DrawHeader(Rect rect)
        {
            var titleRect = new Rect(rect.x + 8f, rect.y, rect.width - 88f, rect.height);
            var settingsButtonRect = new Rect(rect.xMax - 80f, rect.y + 3f, 75f, rect.height - 6f);

            // Title with icon styling
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.9f, 0.95f, 1f); // Slight blue-white tint
            Widgets.Label(titleRect, "ST_GearTab_Header".Translate());
            GUI.color = Color.white;

            // Settings button with better styling
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            // Custom button with hover effect
            var buttonColor = Mouse.IsOver(settingsButtonRect)
                ? new Color(0.25f, 0.35f, 0.45f)
                : new Color(0.2f, 0.28f, 0.36f);

            Widgets.DrawBoxSolid(settingsButtonRect, buttonColor);
            Widgets.DrawBox(settingsButtonRect, 1);

            if (Widgets.ButtonInvisible(settingsButtonRect))
            {
                Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<SurvivalToolsMod>()));
            }

            GUI.color = new Color(0.85f, 0.9f, 0.95f);
            Widgets.Label(settingsButtonRect, "Settings");
            GUI.color = Color.white;

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static void DrawToolList(Rect listRect, Pawn pawn)
        {
            var viewRect = new Rect(0f, 0f, listRect.width - SCROLL_BAR_WIDTH, _cachedToolInfo.Count * TOOL_ROW_HEIGHT + 20f);

            // Consume input only within our rect
            var originalMousePos = Input.mousePosition;
            bool mouseInside = listRect.Contains(Event.current.mousePosition);

            GUI.BeginGroup(listRect);
            _scrollPos = GUI.BeginScrollView(new Rect(0, 0, listRect.width, listRect.height), _scrollPos, viewRect);

            if (_cachedToolInfo.Count == 0)
            {
                // Empty state with better styling
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                var emptyRect = new Rect(0, viewRect.height * 0.5f - 30f, viewRect.width, 60f);
                Widgets.Label(emptyRect, "ST_GearTab_NoTools".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                // Draw tools with alternating backgrounds
                var curY = 10f;
                for (int i = 0; i < _cachedToolInfo.Count; i++)
                {
                    var toolInfo = _cachedToolInfo[i];
                    var toolRect = new Rect(4f, curY, viewRect.width - 8f, TOOL_ROW_HEIGHT);

                    DrawToolRow(toolRect, toolInfo, mouseInside, i % 2 == 0);
                    curY += TOOL_ROW_HEIGHT + 2f; // Small gap between rows
                }
            }

            GUI.EndScrollView();
            GUI.EndGroup();
        }

        private static void DrawToolRow(Rect rect, ToolDisplayInfo toolInfo, bool allowTooltip, bool isEvenRow)
        {
            // Rounded background with alternating colors
            var bgColor = isEvenRow ? RowBgColor : RowAltBgColor;

            // Hover highlight
            if (allowTooltip && Mouse.IsOver(rect))
            {
                bgColor = new Color(bgColor.r + 0.1f, bgColor.g + 0.1f, bgColor.b + 0.15f, bgColor.a);
            }

            if (Event.current.type == EventType.Repaint)
            {
                Widgets.DrawBoxSolid(rect, bgColor);

                // Subtle left border accent
                var accentRect = new Rect(rect.x, rect.y, 3f, rect.height);
                Widgets.DrawBoxSolid(accentRect, new Color(0.3f, 0.5f, 0.7f, 0.6f));
            }

            var line1Rect = new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, 20f);
            var line2Rect = new Rect(rect.x + 10f, rect.y + 26f, rect.width - 20f, 18f);

            // Phase 12: Check for powered tool and show charge bar
            // DISABLED: Battery system turned off
            //var powerComp = toolInfo.Tool?.TryGetComp<CompPowerTool>();
            //bool hasPowerComp = powerComp != null;
            //float chargeBarHeight = 0f;

            //if (hasPowerComp)
            //{
            //    chargeBarHeight = 4f;
            //    line2Rect.y += chargeBarHeight + 2f;
            //}

            // Line 1: Tool label + scores
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            // Draw tool label with slight emphasis
            GUI.color = new Color(0.95f, 0.95f, 1f);
            var labelWidth = Text.CalcSize(toolInfo.Label).x + 10f;
            var labelRect = new Rect(line1Rect.x, line1Rect.y, labelWidth, line1Rect.height);
            Widgets.Label(labelRect, toolInfo.Label);
            GUI.color = Color.white;

            // Draw scores with color based on quality
            var scoreRect = new Rect(labelRect.xMax + 8f, line1Rect.y, line1Rect.width - labelWidth - 8f, line1Rect.height);
            Text.Anchor = TextAnchor.MiddleRight;

            // Color score based on average value
            var avgScore = GetAverageScore(toolInfo.ScoreText);
            var scoreColor = avgScore >= 1.5f ? ScoreGoodColor :
                            avgScore >= 1.0f ? ScoreMediumColor :
                            new Color(0.8f, 0.6f, 0.4f);

            GUI.color = scoreColor;
            Widgets.Label(scoreRect, toolInfo.ScoreText);
            GUI.color = Color.white;

            // Phase 12: Draw charge bar if powered tool
            // DISABLED: Battery system turned off
            //if (hasPowerComp)
            //{
            //    var chargeBarRect = new Rect(line1Rect.x, line1Rect.yMax + 2f, line1Rect.width, chargeBarHeight);
            //    DrawChargeBar(chargeBarRect, powerComp);
            //}

            // Line 2: Why text with better styling
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            GUI.color = WhyTextColor;
            Widgets.Label(line2Rect, toolInfo.WhyText);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // Tooltip with better formatting
            if (allowTooltip && Mouse.IsOver(rect) && !string.IsNullOrEmpty(toolInfo.TooltipText))
            {
                TooltipHandler.TipRegion(rect, toolInfo.TooltipText);
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }

        // Phase 12: Draw charge bar for powered tools
        // DISABLED: Battery system turned off
        /*
        private static void DrawChargeBar(Rect rect, CompPowerTool powerComp)
        {
            if (powerComp == null) return;

            // Background
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.8f));

            // Charge fill (5% bucket steps)
            float chargePct = powerComp.ChargePct;
            if (chargePct > 0f)
            {
                var fillRect = new Rect(rect.x, rect.y, rect.width * chargePct, rect.height);
                var fillColor = chargePct > 0.5f ? new Color(0.4f, 0.8f, 0.5f) : // Green when >50%
                                chargePct > 0.2f ? new Color(0.9f, 0.8f, 0.3f) : // Yellow when >20%
                                new Color(0.9f, 0.4f, 0.3f); // Red when low
                Widgets.DrawBoxSolid(fillRect, fillColor);
            }

            // Border
            Widgets.DrawBox(rect, 1);
        }
        */

        private static float GetAverageScore(string scoreText)
        {
            if (string.IsNullOrEmpty(scoreText)) return 0f;

            var parts = scoreText.Split(' ');
            float sum = 0f;
            int count = 0;

            foreach (var part in parts)
            {
                var colonIdx = part.IndexOf(':');
                if (colonIdx > 0 && colonIdx < part.Length - 1)
                {
                    if (float.TryParse(part.Substring(colonIdx + 1), out float val))
                    {
                        sum += val;
                        count++;
                    }
                }
            }

            return count > 0 ? sum / count : 0f;
        }

        private static void UpdateCacheIfNeeded(Pawn pawn)
        {
            var currentKey = GetCacheInvalidationKey(pawn);
            if (_cachedPawn == pawn && _cacheInvalidationKey == currentKey)
                return;

            _cachedPawn = pawn;
            _cacheInvalidationKey = currentKey;
            _cachedToolInfo.Clear();

            // Update mode/carry text
            var settings = SurvivalToolsMod.Settings;
            string mode = settings?.hardcoreMode == true ? (settings.extraHardcoreMode ? "Nightmare" : "Hardcore") : "Normal";
            string carry = "Auto"; // Simplified for now
            _cachedModeCarryText = "ST_Gear_ModeCarry".Translate(mode, carry);

            // Update gating status
            bool isGated = false;
            if (settings?.hardcoreMode == true || settings?.extraHardcoreMode == true)
            {
                // Quick check if pawn would be blocked from any work
                var testWorkGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Mine");
                if (testWorkGiver != null)
                {
                    isGated = Gating.JobGate.ShouldBlock(pawn, testWorkGiver, (JobDef)null, false, out var _, out var _, out var _);
                }
            }
            _cachedGatingText = isGated ? "ST_Gear_Gated".Translate().ToString() : "";

            // Build tool list
            BuildToolList(pawn);
        }

        private static void BuildToolList(Pawn pawn)
        {
            var tools = new List<Thing>();

            // Add equipped tool
            if (pawn.equipment?.Primary != null && ToolUtility.IsSurvivalTool(pawn.equipment.Primary))
            {
                tools.Add(pawn.equipment.Primary);
            }

            // Add inventory tools (skip raw tool-stuff; we'll show them as a single virtual entry)
            if (pawn.inventory?.innerContainer != null)
            {
                for (int i = 0; i < pawn.inventory.innerContainer.Count; i++)
                {
                    var item = pawn.inventory.innerContainer[i];
                    if (ToolUtility.IsSurvivalTool(item) && !(item.def?.IsToolStuff() == true))
                    {
                        tools.Add(item);
                    }
                }
            }

            // Only add virtual tools if the pawn actually has them in inventory
            if (pawn.inventory?.innerContainer != null)
            {
                // Only include textile-based virtual tool (Cloth). Wood is no longer treated as a virtual tool.
                var virtualCandidates = new[] { ThingDefOf.Cloth };

                // Track which defs we've already represented to avoid duplicates when multiple stacks exist
                var representedDefs = new HashSet<ThingDef>();
                for (int i = 0; i < tools.Count; i++)
                {
                    var t = tools[i];
                    if (t?.def != null) representedDefs.Add(t.def);
                }

                foreach (var candidate in virtualCandidates)
                {
                    // Check if pawn actually has this material
                    bool hasInInventory = false;
                    for (int i = 0; i < pawn.inventory.innerContainer.Count; i++)
                    {
                        var item = pawn.inventory.innerContainer[i];
                        if (item.def == candidate)
                        {
                            hasInInventory = true;
                            break;
                        }
                    }

                    // Only create virtual tool if pawn has the material AND it can be used as a tool
                    if (hasInInventory && !representedDefs.Contains(candidate) && Helpers.ToolStatResolver.GetToolCandidates().Contains(candidate))
                    {
                        // Create virtual representation
                        var virtualTool = ThingMaker.MakeThing(candidate);
                        virtualTool.stackCount = 1;
                        tools.Add(virtualTool);
                        representedDefs.Add(candidate);
                    }
                }
            }

            // Convert to display info
            _tooltipBuilder.Clear();
            for (int i = 0; i < tools.Count; i++)
            {
                var tool = tools[i];
                var displayInfo = CreateToolDisplayInfo(tool, pawn);
                _cachedToolInfo.Add(displayInfo);
            }
        }

        private static ToolDisplayInfo CreateToolDisplayInfo(Thing tool, Pawn pawn)
        {
            var info = new ToolDisplayInfo
            {
                Tool = tool,
                Label = tool.LabelCap
            };

            // Get relevant stats for this specific tool
            var relevantStats = GetRelevantStatsForTool(tool);

            // Build score text using only relevant stats
            _tooltipBuilder.Clear();
            var scoreBuilder = new StringBuilder();
            var whyBuilder = new StringBuilder();

            for (int i = 0; i < relevantStats.Count; i++)
            {
                var stat = relevantStats[i];
                if (stat == null) continue;

                var score = Scoring.ToolScoring.Score(tool, pawn, stat);
                if (score > 0.001f)
                {
                    if (scoreBuilder.Length > 0) scoreBuilder.Append(" ");
                    scoreBuilder.Append($"{GetStatAbbreviation(stat)}:{score:F2}");

                    // Get top contributors for "why" text
                    if (whyBuilder.Length == 0) // Only show for first stat
                    {
                        var contributors = Scoring.ToolScoring.TopContributors(tool, pawn, stat, 2);
                        if (contributors.Length > 0)
                        {
                            var topContrib = contributors[0];
                            var pct = (topContrib.Item2 - 1f) * 100f;
                            whyBuilder.Append("ST_GearTab_Why".Translate(stat.LabelCap, pct.ToString("F0")));
                        }
                    }

                    // Build tooltip using ToolStatInfo
                    var statInfo = Helpers.ToolStatResolver.GetToolStatInfo(tool.def, tool.Stuff, stat);
                    _tooltipBuilder.AppendLine($"{stat.LabelCap}: {statInfo.Factor:F3} ({statInfo.Source})");
                    if (!string.IsNullOrEmpty(statInfo.QuirkSummary))
                    {
                        _tooltipBuilder.AppendLine($"  Quirks: {statInfo.QuirkSummary}");
                    }
                }
            }

            info.ScoreText = scoreBuilder.ToString();
            info.WhyText = whyBuilder.Length > 0 ? whyBuilder.ToString() : "ST_GearTab_BaseValues".Translate().ToString();

            // Phase 12: Add charge status to tooltip
            // DISABLED: Battery system turned off
            /*
            var powerComp = tool?.TryGetComp<CompPowerTool>();
            if (powerComp != null)
            {
                _tooltipBuilder.AppendLine();
                if (powerComp.HasCharge)
                {
                    _tooltipBuilder.AppendLine("ST_GearTab_PoweredBonus".Translate());
                    _tooltipBuilder.AppendLine("ST_Power_Charged".Translate((powerComp.ChargePct * 100f).ToString("F0") + "%"));
                }
                else
                {
                    _tooltipBuilder.AppendLine("ST_GearTab_PowerEmpty".Translate());
                }
            }
            */

            info.TooltipText = _tooltipBuilder.ToString();

            return info;
        }

        private static string GetStatAbbreviation(StatDef stat)
        {
            if (stat == ST_StatDefOf.DiggingSpeed) return "Min";
            if (stat == StatDefOf.ConstructionSpeed) return "Con";
            if (stat == ST_StatDefOf.TreeFellingSpeed) return "Tree";
            if (stat == ST_StatDefOf.PlantHarvestingSpeed) return "Plant";
            return stat.defName.Substring(0, Math.Min(4, stat.defName.Length));
        }

        private static List<StatDef> GetRelevantStatsForTool(Thing tool)
        {
            var relevantStats = new List<StatDef>();

            // Check what stats this tool actually affects by testing the tool stat resolver
            foreach (var stat in KeyWorkStats)
            {
                if (stat == null) continue;

                var statInfo = Helpers.ToolStatResolver.GetToolStatInfo(tool.def, tool.Stuff, stat);

                // If the tool has a meaningful impact on this stat (not just base 1.0), include it
                if (statInfo.Factor > 1.001f || !string.IsNullOrEmpty(statInfo.QuirkSummary))
                {
                    relevantStats.Add(stat);
                }
                // Also include if the tool is equipped and provides any bonus
                else if (tool == _cachedPawn?.equipment?.Primary)
                {
                    var score = Scoring.ToolScoring.Score(tool, _cachedPawn, stat);
                    if (score > 0.001f)
                    {
                        relevantStats.Add(stat);
                    }
                }
            }

            // If no stats were found, fall back to checking tool type patterns
            if (relevantStats.Count == 0)
            {
                var toolName = tool.def.defName.ToLowerInvariant();
                var toolLabel = tool.LabelNoCount.ToLowerInvariant();

                // Mining tools
                if (toolName.Contains("pickaxe") || toolName.Contains("drill") ||
                    toolLabel.Contains("pickaxe") || toolLabel.Contains("drill"))
                {
                    relevantStats.Add(ST_StatDefOf.DiggingSpeed);
                }

                // Tree felling tools
                if (toolName.Contains("axe") || toolName.Contains("chainsaw") ||
                    toolLabel.Contains("axe") || toolLabel.Contains("chainsaw"))
                {
                    relevantStats.Add(ST_StatDefOf.TreeFellingSpeed);
                }

                // Construction tools
                if (toolName.Contains("hammer") || toolName.Contains("drill") ||
                    toolLabel.Contains("hammer") || toolLabel.Contains("drill"))
                {
                    relevantStats.Add(StatDefOf.ConstructionSpeed);
                }

                // Plant harvesting tools
                if (toolName.Contains("sickle") || toolName.Contains("scythe") || toolName.Contains("hoe") ||
                    toolLabel.Contains("sickle") || toolLabel.Contains("scythe") || toolLabel.Contains("hoe"))
                {
                    relevantStats.Add(ST_StatDefOf.PlantHarvestingSpeed);
                }

            }

            return relevantStats;
        }

        private static int GetCacheInvalidationKey(Pawn pawn)
        {
            var hash = 0;

            // Include resolver version
            hash ^= Helpers.ToolStatResolver.Version << 8;

            // Include score cache state
            var cacheStats = Helpers.ScoreCache.GetCacheStats();
            hash ^= cacheStats.resolverVersion << 16;

            // Include pawn inventory/equipment state
            if (pawn.equipment?.Primary != null)
            {
                hash ^= pawn.equipment.Primary.GetHashCode();
                hash ^= pawn.equipment.Primary.HitPoints << 4;
            }

            if (pawn.inventory?.innerContainer != null)
            {
                hash ^= pawn.inventory.innerContainer.Count << 12;
                // Sample a few items to detect changes without full enumeration
                for (int i = 0; i < Math.Min(3, pawn.inventory.innerContainer.Count); i++)
                {
                    var item = pawn.inventory.innerContainer[i];
                    hash ^= item.GetHashCode() << (i * 2);
                    hash ^= item.HitPoints << (i * 2 + 8);
                }
            }

            return hash;
        }
    }
}