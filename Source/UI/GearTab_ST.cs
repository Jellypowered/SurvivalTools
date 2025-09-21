// RimWorld 1.6 / C# 7.3
// Source/UI/GearTab_ST.cs
//
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
        private const float HEADER_HEIGHT = 24f;
        private const float TOOL_ROW_HEIGHT = 42f; // Two lines at 21px each
        private const float SCROLL_BAR_WIDTH = 16f;
        private const float MARGIN = 6f;

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

            // Draw background
            Widgets.DrawBoxSolid(hostRect, new Color(0.1f, 0.1f, 0.1f, 0.8f));
            Widgets.DrawBox(hostRect);

            var contentRect = hostRect.ContractedBy(MARGIN);
            var curY = contentRect.y;

            // Header
            DrawHeader(new Rect(contentRect.x, curY, contentRect.width, HEADER_HEIGHT));
            curY += HEADER_HEIGHT + 4f;

            // Tool list with scroll view
            var listRect = new Rect(contentRect.x, curY, contentRect.width, contentRect.yMax - curY);
            DrawToolList(listRect, pawn);
        }

        private static void DrawHeader(Rect rect)
        {
            var titleRect = new Rect(rect.x, rect.y, rect.width - 80f, rect.height);
            var settingsButtonRect = new Rect(rect.xMax - 75f, rect.y, 75f, rect.height);

            // Title
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(titleRect, "ST_GearTab_Header".Translate());

            // Settings button
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            if (Widgets.ButtonText(settingsButtonRect, "Settings"))
            {
                Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<SurvivalTools>()));
            }

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
                // Empty state
                Text.Anchor = TextAnchor.MiddleCenter;
                var emptyRect = new Rect(0, viewRect.height * 0.5f - 20f, viewRect.width, 40f);
                Widgets.Label(emptyRect, "ST_GearTab_NoTools".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                // Draw tools
                var curY = 10f;
                for (int i = 0; i < _cachedToolInfo.Count; i++)
                {
                    var toolInfo = _cachedToolInfo[i];
                    var toolRect = new Rect(0, curY, viewRect.width, TOOL_ROW_HEIGHT);

                    DrawToolRow(toolRect, toolInfo, mouseInside);
                    curY += TOOL_ROW_HEIGHT;
                }
            }

            GUI.EndScrollView();
            GUI.EndGroup();
        }

        private static void DrawToolRow(Rect rect, ToolDisplayInfo toolInfo, bool allowTooltip)
        {
            // Alternate background
            if (Event.current.type == EventType.Repaint)
            {
                var bgColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
                GUI.DrawTexture(rect, TexUI.HighlightTex, ScaleMode.StretchToFill, true, 0f, bgColor, 0f, 0f);
            }

            var line1Rect = new Rect(rect.x + 4f, rect.y + 2f, rect.width - 8f, 21f);
            var line2Rect = new Rect(rect.x + 4f, rect.y + 21f, rect.width - 8f, 21f);

            // Line 1: Tool label + scores
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            var labelWidth = Text.CalcSize(toolInfo.Label).x + 10f;
            var labelRect = new Rect(line1Rect.x, line1Rect.y, labelWidth, line1Rect.height);
            var scoreRect = new Rect(labelRect.xMax, line1Rect.y, line1Rect.width - labelWidth, line1Rect.height);

            Widgets.Label(labelRect, toolInfo.Label);

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = Color.cyan;
            Widgets.Label(scoreRect, toolInfo.ScoreText);
            GUI.color = Color.white;

            // Line 2: Why text
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.gray;
            Widgets.Label(line2Rect, toolInfo.WhyText);
            GUI.color = Color.white;

            // Tooltip
            if (allowTooltip && Mouse.IsOver(rect) && !string.IsNullOrEmpty(toolInfo.TooltipText))
            {
                TooltipHandler.TipRegion(rect, toolInfo.TooltipText);
            }

            Text.Anchor = TextAnchor.UpperLeft;
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
            var settings = SurvivalTools.Settings;
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
                    isGated = Gating.JobGate.ShouldBlock(pawn, testWorkGiver, null, false, out var _, out var _, out var _);
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

            // Add inventory tools
            if (pawn.inventory?.innerContainer != null)
            {
                for (int i = 0; i < pawn.inventory.innerContainer.Count; i++)
                {
                    var item = pawn.inventory.innerContainer[i];
                    if (ToolUtility.IsSurvivalTool(item))
                    {
                        tools.Add(item);
                    }
                }
            }

            // Only add virtual tools if the pawn actually has them in inventory
            if (pawn.inventory?.innerContainer != null)
            {
                var virtualCandidates = new[]
                {
                    ThingDefOf.Cloth,
                    ThingDefOf.WoodLog
                };

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
                    if (hasInInventory && Helpers.ToolStatResolver.GetToolCandidates().Contains(candidate))
                    {
                        // Create virtual representation
                        var virtualTool = ThingMaker.MakeThing(candidate);
                        virtualTool.stackCount = 1;
                        tools.Add(virtualTool);
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
            info.WhyText = whyBuilder.Length > 0 ? whyBuilder.ToString() : "Base values";
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

                // Virtual tools (cloth/wood) - show what they can substitute for
                if (tool.def == ThingDefOf.Cloth)
                {
                    relevantStats.Add(ST_StatDefOf.PlantHarvestingSpeed); // Cloth can substitute for plant harvesting
                }
                else if (tool.def == ThingDefOf.WoodLog)
                {
                    relevantStats.Add(StatDefOf.ConstructionSpeed); // Wood can substitute for construction
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