// RimWorld 1.6 / C# 7.3
// Source/DebugTools/DebugAction_TestNewFeatures.cs
using System.Linq;
using RimWorld;
using Verse;
using LudeonTK;

namespace SurvivalTools
{
    /// <summary>
    /// Quick debug actions for testing the new penalty and WorkSpeedGlobal systems.
    /// </summary>
    public static class DebugAction_TestNewFeatures
    {
#if DEBUG
        [DebugAction("Survival Tools", "Test Normal Mode Penalties", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestNormalModePenalties()
        {
            var settings = SurvivalTools.Settings;
            if (settings == null)
            {
                Messages.Message("Survival Tools settings not available", MessageTypeDefOf.RejectInput);
                return;
            }

            // Determine current mode
            string currentMode;
            if (settings.extraHardcoreMode)
            {
                currentMode = "Extra Hardcore";
            }
            else if (settings.hardcoreMode)
            {
                currentMode = "Hardcore";
            }
            else
            {
                currentMode = "Normal";
            }

            var message = $"{currentMode} Mode Penalty Settings:\n" +
                         $"â€¢ Current Mode: {currentMode}\n" +
                         $"â€¢ Hardcore Mode Setting: {settings.hardcoreMode}\n";

            if (currentMode == "Normal")
            {
                message += $"â€¢ Normal Mode Penalties Enabled: {settings.enableNormalModePenalties}\n" +
                          $"â€¢ Normal Mode Penalty Factor: {settings.noToolStatFactorNormal:P0} ({(1f - settings.noToolStatFactorNormal):P0} slower)\n";
            }
            else if (currentMode == "Hardcore")
            {
                message += $"â€¢ Hardcore Penalty: 0% speed (100% slower) for all work without tools\n";
            }
            else if (currentMode == "Extra Hardcore")
            {
                message += $"â€¢ Extra Hardcore Penalty: 0% speed (100% slower) + tool degradation\n";
            }

            message += "\n";

            if (Find.CurrentMap?.mapPawns?.FreeColonistsSpawned?.FirstOrDefault() is Pawn pawn)
            {
                message += $"Sample pawn ({pawn.Name}):\n";

                // Test core stats that should be affected by tools
                var coreStats = new[] { ST_StatDefOf.DiggingSpeed, StatDefOf.ConstructionSpeed, ST_StatDefOf.WorkSpeedGlobal };
                message += $"Core work stats (affected by tool penalties in {currentMode.ToLower()} mode):\n";
                foreach (var stat in coreStats.Where(s => s != null))
                {
                    var value = pawn.GetStatValue(stat);
                    message += $"â€¢ {stat.label}: {value:P0}";

                    // Add context about what we expect to see
                    if (currentMode == "Normal" && settings.enableNormalModePenalties)
                    {
                        message += $" (should be ~{settings.noToolStatFactorNormal:P0} without tools)";
                    }
                    else if (currentMode == "Hardcore" || currentMode == "Extra Hardcore")
                    {
                        message += " (should be 0% without tools)";
                    }
                    else if (currentMode == "Normal" && !settings.enableNormalModePenalties)
                    {
                        message += " (should be 100% - no penalties)";
                    }
                    message += "\n";
                }

                // Test optional stats
                var optionalStats = new[] { ST_StatDefOf.CleaningSpeed, ST_StatDefOf.ResearchSpeed };
                message += $"\nOptional stats (should be unaffected in normal mode):\n";
                foreach (var stat in optionalStats.Where(s => s != null))
                {
                    var value = pawn.GetStatValue(stat);
                    message += $"â€¢ {stat.label}: {value:P0}";

                    if (currentMode == "Normal")
                    {
                        message += " (should be 100% - no penalties)";
                    }
                    else
                    {
                        message += " (may be 0% in hardcore modes)";
                    }
                    message += "\n";
                }
            }
            else
            {
                message += "No colonists available for testing.";
            }

            Log.Message(message);
            Messages.Message("Penalty test completed - check log for details", MessageTypeDefOf.PositiveEvent);
        }

        [DebugAction("Survival Tools", "Test WorkSpeedGlobal Stat", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestWorkSpeedGlobalStat()
        {
            if (ST_StatDefOf.WorkSpeedGlobal == null)
            {
                Messages.Message("WorkSpeedGlobal stat not found - ensure mod is properly loaded", MessageTypeDefOf.RejectInput);
                return;
            }

            var settings = SurvivalTools.Settings;
            if (settings == null)
            {
                Messages.Message("Survival Tools settings not available", MessageTypeDefOf.RejectInput);
                return;
            }

            // Determine current mode
            string currentMode;
            if (settings.extraHardcoreMode)
            {
                currentMode = "Extra Hardcore";
            }
            else if (settings.hardcoreMode)
            {
                currentMode = "Hardcore";
            }
            else
            {
                currentMode = "Normal";
            }

            var message = $"WorkSpeedGlobal Stat Test ({currentMode} Mode):\n" +
                         $"â€¢ Stat Def: {ST_StatDefOf.WorkSpeedGlobal.defName}\n" +
                         $"â€¢ Label: {ST_StatDefOf.WorkSpeedGlobal.label}\n" +
                         $"â€¢ Base Value: {ST_StatDefOf.WorkSpeedGlobal.defaultBaseValue}\n" +
                         $"â€¢ Current Mode: {currentMode}\n\n";

            if (Find.CurrentMap?.mapPawns?.FreeColonistsSpawned?.FirstOrDefault() is Pawn pawn)
            {
                var value = pawn.GetStatValue(ST_StatDefOf.WorkSpeedGlobal);
                message += $"Sample pawn ({pawn.Name}) WorkSpeedGlobal: {value:P0}";

                // Add context about what we expect to see
                if (currentMode == "Normal" && settings.enableNormalModePenalties)
                {
                    message += $" (should be ~{settings.noToolStatFactorNormal:P0} without tools)";
                }
                else if (currentMode == "Hardcore" || currentMode == "Extra Hardcore")
                {
                    message += " (should be 0% without tools)";
                }
                else if (currentMode == "Normal" && !settings.enableNormalModePenalties)
                {
                    message += " (should be 100% - no penalties)";
                }
                message += "\n\n";

                // Check if pawn has tools that might affect this stat
                var tools = pawn.GetAllUsableSurvivalTools();
                if (tools.Any())
                {
                    message += $"Available tools: {tools.Count()}\n";
                    foreach (var tool in tools.Take(3))
                    {
                        message += $"â€¢ {tool.Label}\n";
                    }
                    message += "Note: Tools should improve WorkSpeedGlobal for crafting activities.\n";
                }
                else
                {
                    message += "No survival tools available for testing.\n";
                    message += $"In {currentMode.ToLower()} mode, this explains the low work speed.\n";
                    message += "Try spawning tools with the 'Spawn tool with stuff...' debug action to test improvements.\n";
                }
            }
            else
            {
                message += "No colonists available for testing.";
            }

            Log.Message(message);
            Messages.Message("WorkSpeedGlobal test completed - check log for details", MessageTypeDefOf.PositiveEvent);
        }

        [DebugAction("Survival Tools", "Dump Pawn Tool State (Floater)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DumpPawnToolStateFloater()
        {
            Pawn pawn = null;
            // Prefer selected pawn if any
            if (Find.Selector?.SingleSelectedThing is Pawn selPawn)
                pawn = selPawn;

            // Fallback to first free colonist on current map
            if (pawn == null && Find.CurrentMap?.mapPawns?.FreeColonistsSpawned != null)
                pawn = Find.CurrentMap.mapPawns.FreeColonistsSpawned.FirstOrDefault();

            if (pawn == null)
            {
                Messages.Message("No pawn available to dump tool state.", MessageTypeDefOf.RejectInput);
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Survival Tools - Pawn dump: {pawn.LabelShort}");
            sb.AppendLine($"Mode: {(SurvivalTools.Settings?.extraHardcoreMode == true ? "Extra Hardcore" : SurvivalTools.Settings?.hardcoreMode == true ? "Hardcore" : "Normal")}");
            sb.AppendLine("");

            // Core stats list
            var stats = new[] { ST_StatDefOf.TreeFellingSpeed, ST_StatDefOf.PlantHarvestingSpeed, ST_StatDefOf.DiggingSpeed, StatDefOf.ConstructionSpeed, ST_StatDefOf.WorkSpeedGlobal };
            foreach (var stat in stats.Where(s => s != null))
            {
                // Base/default value from the StatDef
                float baseVal = stat.defaultBaseValue;
                // Raw value without post-processing (applyPostProcess = false)
                float rawVal = pawn.GetStatValue(stat, false);
                // Final value with post-processing (applyPostProcess = true)
                float finalVal = pawn.GetStatValue(stat, true);

                var best = pawn.GetBestSurvivalTool(stat);
                float f = best != null ? best.WorkStatFactors.ToList().GetStatFactorFromList(stat) : -1f;
                sb.AppendLine($"{stat.label}: base={baseVal:F2} raw={rawVal:F2} post={finalVal:F2}  bestTool={(best != null ? best.LabelCapNoCount : "none")} factor={(f > 0 ? f.ToString("F2") : "n/a")}");

                // If rawVal is zero or finalVal is zero, include the StatDef.parts to help trace which StatPart(s) are applied
                try
                {
                    if (rawVal <= 0f || finalVal <= 0f)
                    {
                        var parts = stat.parts;
                        if (parts != null && parts.Count > 0)
                        {
                            sb.AppendLine($"  StatDef.parts ({parts.Count}):");
                            for (int i = 0; i < parts.Count; i++)
                                sb.AppendLine($"   [{i}] {parts[i].GetType().FullName} -> {parts[i].ToString()}");
                        }
                        else
                        {
                            sb.AppendLine("  StatDef.parts: <none>");
                        }
                    }
                }
                catch { }
            }

            // List top available tools
            var tools = pawn.GetAllUsableSurvivalTools().ToList();
            sb.AppendLine("");
            if (tools.Any())
            {
                sb.AppendLine($"Available tools ({tools.Count}):");
                foreach (var t in tools.Take(6))
                {
                    var lab = t.LabelCapNoCount;
                    sb.AppendLine($" - {lab}");
                }
            }
            else
            {
                sb.AppendLine("No usable survival tools available.");
            }

            var message = sb.ToString();
            // Show message and a short floater above pawn so it's visible in-game without opening logs
            Messages.Message(message, MessageTypeDefOf.NeutralEvent);
            if (pawn.Map != null)
            {
                var shortText = "SurvivalTools: pawn tool state dumped";
                try
                {
                    MoteMaker.ThrowText(pawn.Position.ToVector3Shifted(), pawn.Map, shortText, 3.5f);
                }
                catch { }
            }
        }
#endif
    }
}
