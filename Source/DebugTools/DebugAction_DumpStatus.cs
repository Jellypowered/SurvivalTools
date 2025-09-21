// RimWorld 1.6 / C# 7.3
// Source/DebugTools/DebugAction_DumpStatus.cs
using System;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using LudeonTK;

namespace SurvivalTools
{
    internal static class ST_DebugActions
    {
        [LudeonTK.DebugAction("ST", "Dump ST status → Desktop", false, false)]
        private static void DumpStatus()
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("[SurvivalTools] Status dump");
            sb.AppendLine("----------------------------");
            try { sb.AppendLine($"Settings: hardcore={(SurvivalTools.Settings?.hardcoreMode == true)} extraHardcore={(SurvivalTools.Settings?.extraHardcoreMode == true)}"); } catch { }
            try
            {
                sb.AppendLine($"Resolver version: {Helpers.ToolStatResolver.Version}");
                var cacheStats = Helpers.ScoreCache.GetCacheStats();
                sb.AppendLine($"Score cache: {cacheStats.entryCount} entries, {cacheStats.hits} hits, {cacheStats.misses} misses (v{cacheStats.resolverVersion})");
            }
            catch { }
            try { sb.AppendLine($"Filtered tool candidates: {Helpers.ToolStatResolver.GetToolCandidates().Count()}"); } catch { }
            try { sb.AppendLine($"WorkSpeedGlobal jobs discovered: {Helpers.WorkSpeedGlobalHelper.GetWorkSpeedGlobalJobs().Count()}"); } catch { }
            try { sb.AppendLine("Active mods carrying ST hooks:"); sb.AppendLine(CompatLine()); } catch { }
            sb.AppendLine();
            sb.AppendLine("Tip: use this after loading a save and mining a tile to verify wear/penalties.");
            var path = ST_FileIO.WriteUtf8Atomic($"ST_Status_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt", sb.ToString());
            try { Messages.Message("Survival Tools: wrote " + path, MessageTypeDefOf.TaskCompletion); } catch { }
        }

        [LudeonTK.DebugAction("ST", "Dump resolver comparison → Desktop", false, false)]
        private static void DumpResolverComparison()
        {
            var sb = new StringBuilder(8192);
            sb.AppendLine("[SurvivalTools] Resolver Comparison (Phase 2)");
            sb.AppendLine("============================================");
            sb.AppendLine();

            try
            {
                // Resolver state summary
                sb.AppendLine($"Resolver version: {Helpers.ToolStatResolver.Version}");
                sb.AppendLine($"Registered quirks: {Helpers.ToolStatResolver.GetQuirkCount()}");
                sb.AppendLine();

                // Get sample of filtered tool candidates
                var sampleTools = Helpers.ToolStatResolver.GetToolCandidates()
                    .Where(t => !string.IsNullOrEmpty(t.label))
                    .Take(20)
                    .ToList();

                var sampleStats = new[]
                {
                    ST_StatDefOf.DiggingSpeed,
                    ST_StatDefOf.TreeFellingSpeed,
                    ST_StatDefOf.PlantHarvestingSpeed,
                    StatDefOf.ConstructionSpeed,
                    ST_StatDefOf.ButcheryFleshSpeed
                }.Where(s => s != null).ToList();

                sb.AppendLine($"Comparing {sampleTools.Count} filtered tool candidates across {sampleStats.Count} stats:");
                sb.AppendLine();

                foreach (var tool in sampleTools)
                {
                    sb.AppendLine($"Tool: {tool.defName} ({tool.label})");
                    sb.AppendLine($"  Source: {tool.modContentPack?.Name ?? "Core"}");
                    sb.AppendLine($"  Tech Level: {tool.techLevel}");

                    foreach (var stat in sampleStats)
                    {
                        var info = Helpers.ToolStatResolver.GetToolStatInfo(tool, null, stat);

                        string quirkInfo = !string.IsNullOrEmpty(info.QuirkSummary)
                            ? $", Quirks: {info.QuirkSummary}"
                            : "";

                        sb.AppendLine($"  {stat.defName}: {info.Factor:F3} (Source: {info.Source}, Clamped: {info.IsClamped}{quirkInfo})");
                    }
                    sb.AppendLine();
                }

                // Cache and quirk stats
                var cacheInfo = Helpers.ToolStatResolver.GetAllCachedInfos().ToList();
                var quirkedInfos = cacheInfo.Where(info => !string.IsNullOrEmpty(info.QuirkSummary)).ToList();

                sb.AppendLine($"Cached entries: {cacheInfo.Count}");
                sb.AppendLine($"Entries with quirks applied: {quirkedInfos.Count}");

                if (quirkedInfos.Any())
                {
                    sb.AppendLine();
                    sb.AppendLine("Sample quirk applications (max 5):");
                    foreach (var info in quirkedInfos.Take(5))
                    {
                        sb.AppendLine($"  {info.ToolDef.defName} + {info.Stat.defName}: {info.QuirkSummary}");
                    }
                }

                sb.AppendLine();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error during resolver comparison: {ex}");
            }

            var path = ST_FileIO.WriteUtf8Atomic($"ST_ResolverComparison_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt", sb.ToString());
            try { Messages.Message("Survival Tools: wrote resolver comparison to " + path, MessageTypeDefOf.TaskCompletion); } catch { }
        }

        [LudeonTK.DebugAction("ST", "Test tool quirk system", false, false)]
        private static void TestToolQuirkSystem()
        {
            if (!Prefs.DevMode)
            {
                Messages.Message("Debug actions require dev mode enabled", MessageTypeDefOf.RejectInput);
                return;
            }

            try
            {
                // Clear existing quirks for clean test
                Helpers.ToolStatResolver.ClearQuirks();

                // Register a test quirk: axes get 10% bonus to tree felling
                Compat.CompatAPI.RegisterToolQuirk(
                    toolDef => toolDef.label?.ToLowerInvariant().Contains("axe") == true,
                    applier =>
                    {
                        if (applier.Stat == ST_StatDefOf.TreeFellingSpeed)
                        {
                            applier.MultiplyFactor(1.1f, "axe bonus");
                        }
                    }
                );

                // Register another test quirk: steel tools get small construction bonus
                Compat.CompatAPI.RegisterToolQuirk(
                    toolDef => toolDef.MadeFromStuff && toolDef.stuffCategories?.Contains(StuffCategoryDefOf.Metallic) == true,
                    applier =>
                    {
                        if (applier.StuffLabelContains("steel") && applier.Stat == StatDefOf.ConstructionSpeed)
                        {
                            applier.AddBonus(0.05f, "steel construction");
                        }
                    }
                );

                var sb = new StringBuilder(2048);
                sb.AppendLine("[SurvivalTools] Quirk System Test Results");
                sb.AppendLine("========================================");
                sb.AppendLine($"Test executed at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Resolver version: {Helpers.ToolStatResolver.Version}");
                sb.AppendLine($"Registered quirks: {Helpers.ToolStatResolver.GetQuirkCount()}");
                sb.AppendLine();

                // Test the quirks by checking some tools
                var testAxe = DefDatabase<ThingDef>.AllDefs.FirstOrDefault(t => t.label?.ToLowerInvariant().Contains("axe") == true);
                if (testAxe != null)
                {
                    var info = Helpers.ToolStatResolver.GetToolStatInfo(testAxe, ThingDefOf.Steel, ST_StatDefOf.TreeFellingSpeed);
                    sb.AppendLine($"Test axe ({testAxe.defName}): {info.Factor:F3} (quirks: {info.QuirkSummary ?? "none"})");
                }

                var testHammer = DefDatabase<ThingDef>.AllDefs.FirstOrDefault(t => t.label?.ToLowerInvariant().Contains("hammer") == true);
                if (testHammer != null)
                {
                    var info = Helpers.ToolStatResolver.GetToolStatInfo(testHammer, ThingDefOf.Steel, StatDefOf.ConstructionSpeed);
                    sb.AppendLine($"Test hammer ({testHammer.defName}): {info.Factor:F3} (quirks: {info.QuirkSummary ?? "none"})");
                }

                string fileName = $"ST_QuirkTest_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                ST_FileIO.WriteUtf8Atomic(fileName, sb.ToString());

                Messages.Message($"Tool quirk system test completed - results saved to Desktop/{fileName}", MessageTypeDefOf.TaskCompletion);
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools] Quirk system test failed: {ex}");
                Messages.Message("Tool quirk system test failed - check log for errors", MessageTypeDefOf.RejectInput);
            }
        }

        [LudeonTK.DebugAction("ST", "Bench Score() 10k → Desktop", false, false)]
        private static void BenchmarkScoring()
        {
            if (!Prefs.DevMode)
            {
                Messages.Message("Debug actions require dev mode enabled", MessageTypeDefOf.RejectInput);
                return;
            }

            try
            {
                // Find first humanlike pawn on current map
                var map = Find.CurrentMap;
                if (map == null)
                {
                    Messages.Message("No current map found for benchmark", MessageTypeDefOf.RejectInput);
                    return;
                }

                var pawn = map.mapPawns.FreeColonists.FirstOrDefault();
                if (pawn == null)
                {
                    Messages.Message("No colonist found on current map for benchmark", MessageTypeDefOf.RejectInput);
                    return;
                }

                // Find first tool they hold (or skip gracefully)
                var tool = pawn.GetAllUsableSurvivalTools().FirstOrDefault();
                if (tool == null)
                {
                    Messages.Message($"Pawn {pawn.Name?.ToStringShort ?? "Unknown"} has no tools for benchmark - giving them a steel knife", MessageTypeDefOf.CautionInput);

                    // Create a basic tool for testing
                    var knife = DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Knife", false);
                    if (knife != null)
                    {
                        tool = ThingMaker.MakeThing(knife, ThingDefOf.Steel);
                        pawn.inventory.innerContainer.TryAdd(tool);
                    }
                    else
                    {
                        Messages.Message("Could not create test tool for benchmark", MessageTypeDefOf.RejectInput);
                        return;
                    }
                }

                var workStat = ST_StatDefOf.DiggingSpeed;
                if (workStat == null)
                {
                    Messages.Message("MiningSpeed stat not found for benchmark", MessageTypeDefOf.RejectInput);
                    return;
                }

                // Pre-warm cache
                Scoring.ToolScoring.Score(tool, pawn, workStat);

                var sb = new StringBuilder(2048);
                sb.AppendLine("[SurvivalTools] Scoring Benchmark Results");
                sb.AppendLine("======================================");
                sb.AppendLine($"Test executed at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Pawn: {pawn.Name?.ToStringShort ?? "Unknown"} ({pawn.def.defName})");
                sb.AppendLine($"Tool: {tool.def.defName} (stuff: {tool.Stuff?.defName ?? "none"})");
                sb.AppendLine($"Work stat: {workStat.defName}");
                sb.AppendLine();

                // Get baseline GC and cache stats
                var initialCacheStats = Helpers.ScoreCache.GetCacheStats();
                int initialGen0 = GC.CollectionCount(0);
                int initialGen1 = GC.CollectionCount(1);
                int initialGen2 = GC.CollectionCount(2);

                // Benchmark 10,000 calls
                const int iterations = 10000;
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                float totalScore = 0f;
                for (int i = 0; i < iterations; i++)
                {
                    totalScore += Scoring.ToolScoring.Score(tool, pawn, workStat);
                }

                stopwatch.Stop();

                // Get final GC and cache stats
                var finalCacheStats = Helpers.ScoreCache.GetCacheStats();
                int finalGen0 = GC.CollectionCount(0);
                int finalGen1 = GC.CollectionCount(1);
                int finalGen2 = GC.CollectionCount(2);

                // Calculate results
                double avgScorePerCall = totalScore / iterations;
                double msPerCall = stopwatch.Elapsed.TotalMilliseconds / iterations;
                double callsPerSecond = iterations / stopwatch.Elapsed.TotalSeconds;

                int gcGen0 = finalGen0 - initialGen0;
                int gcGen1 = finalGen1 - initialGen1;
                int gcGen2 = finalGen2 - initialGen2;

                // Report results
                sb.AppendLine("Benchmark Results:");
                sb.AppendLine($"  Iterations: {iterations:N0}");
                sb.AppendLine($"  Total time: {stopwatch.Elapsed.TotalMilliseconds:F2} ms");
                sb.AppendLine($"  Time per call: {msPerCall:F6} ms");
                sb.AppendLine($"  Calls per second: {callsPerSecond:F0}");
                sb.AppendLine($"  Average score: {avgScorePerCall:F6}");
                sb.AppendLine();
                sb.AppendLine("Memory/GC Impact:");
                sb.AppendLine($"  Gen 0 collections: {gcGen0}");
                sb.AppendLine($"  Gen 1 collections: {gcGen1}");
                sb.AppendLine($"  Gen 2 collections: {gcGen2}");
                sb.AppendLine($"  Total GC events: {gcGen0 + gcGen1 + gcGen2}");
                sb.AppendLine();
                sb.AppendLine("Cache Performance:");
                sb.AppendLine($"  Initial cache entries: {initialCacheStats.entryCount}");
                sb.AppendLine($"  Final cache entries: {finalCacheStats.entryCount}");
                sb.AppendLine($"  Cache hits gained: {finalCacheStats.hits - initialCacheStats.hits}");
                sb.AppendLine($"  Cache misses gained: {finalCacheStats.misses - initialCacheStats.misses}");

                if (gcGen0 + gcGen1 + gcGen2 == 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("✓ ZERO GC COLLECTIONS - Benchmark passed!");
                }

                string fileName = $"ST_ScoreBenchmark_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var path = ST_FileIO.WriteUtf8Atomic(fileName, sb.ToString());

                Messages.Message($"Scoring benchmark completed: {callsPerSecond:F0} calls/sec, {gcGen0 + gcGen1 + gcGen2} GC events - results saved to {path}", MessageTypeDefOf.TaskCompletion);
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools] Scoring benchmark failed: {ex}");
                Messages.Message("Scoring benchmark failed - check log for errors", MessageTypeDefOf.RejectInput);
            }
        }

        [LudeonTK.DebugAction("ST", "Compare StatPart vs ToolScoring → Desktop", false, false)]
        private static void CompareStatPartVsToolScoring()
        {
            if (!Prefs.DevMode)
            {
                Messages.Message("Debug actions require dev mode enabled", MessageTypeDefOf.RejectInput);
                return;
            }

            try
            {
                // Find selected pawn
                var selectedPawn = Find.Selector.SingleSelectedThing as Pawn;
                if (selectedPawn == null)
                {
                    Messages.Message("No pawn selected for StatPart comparison", MessageTypeDefOf.RejectInput);
                    return;
                }

                // Use MiningSpeed for comparison
                var testStat = StatDefOf.MiningSpeed;
                if (testStat == null)
                {
                    testStat = ST_StatDefOf.DiggingSpeed;
                    if (testStat == null)
                    {
                        Messages.Message("No mining/digging stat available for comparison", MessageTypeDefOf.RejectInput);
                        return;
                    }
                }

                var sb = new StringBuilder(1024);
                sb.AppendLine("[SurvivalTools] StatPart vs ToolScoring Comparison");
                sb.AppendLine("================================================");
                sb.AppendLine($"Test executed at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Pawn: {selectedPawn.Name?.ToStringShort ?? "Unknown"} ({selectedPawn.def.defName})");
                sb.AppendLine($"Test stat: {testStat.defName} ({testStat.label})");
                sb.AppendLine();

                // Get StatPart-modified value via dry run
                float baseValue = testStat.defaultBaseValue;
                float statPartValue = baseValue;

                var statPart = new Stats.StatPart_SurvivalTools();
                statPart.parentStat = testStat;
                var req = StatRequest.For(selectedPawn);
                statPart.TransformValue(req, ref statPartValue);

                // Get ToolScoring expectation
                var bestTool = Scoring.ToolScoring.GetBestTool(selectedPawn, testStat, out float toolScore);
                float expectedFactor = 1f;
                if (bestTool != null && toolScore > 0.001f)
                {
                    expectedFactor = Helpers.ToolStatResolver.GetToolStatFactor(bestTool.def, bestTool.Stuff, testStat);
                }
                else
                {
                    // No tool case
                    var settings = SurvivalTools.Settings;
                    if (settings != null && !settings.hardcoreMode && !settings.extraHardcoreMode && settings.enableNormalModePenalties)
                    {
                        expectedFactor = settings.noToolStatFactorNormal;
                    }
                }
                float toolScoringValue = baseValue * expectedFactor;

                // Report results
                sb.AppendLine("Comparison Results:");
                sb.AppendLine($"  Base stat value: {baseValue:F3}");
                sb.AppendLine($"  StatPart result: {statPartValue:F3}");
                sb.AppendLine($"  ToolScoring expected: {toolScoringValue:F3}");
                sb.AppendLine($"  Values match: {Math.Abs(statPartValue - toolScoringValue) < 0.001f}");
                sb.AppendLine($"  Difference: {Math.Abs(statPartValue - toolScoringValue):F6}");
                sb.AppendLine();

                if (bestTool != null)
                {
                    sb.AppendLine($"Effective tool: {bestTool.LabelCap}");
                    sb.AppendLine($"Tool score: {toolScore:F3}");
                    sb.AppendLine($"Tool factor: {expectedFactor:F3}");
                }
                else
                {
                    sb.AppendLine("No effective tool found");
                    sb.AppendLine($"Penalty factor: {expectedFactor:F3}");
                }

                string fileName = $"ST_StatPartComparison_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var path = ST_FileIO.WriteUtf8Atomic(fileName, sb.ToString());

                Messages.Message($"StatPart comparison completed - results saved to {path}", MessageTypeDefOf.TaskCompletion);
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools] StatPart comparison failed: {ex}");
                Messages.Message("StatPart comparison failed - check log for errors", MessageTypeDefOf.RejectInput);
            }
        }

        [LudeonTK.DebugAction("ST", "Test gating (selected pawn)", false, false)]
        private static void TestGating()
        {
            if (!Prefs.DevMode)
            {
                Messages.Message("Debug actions require dev mode enabled", MessageTypeDefOf.RejectInput);
                return;
            }

            try
            {
                var selected = Find.Selector.SelectedPawns.FirstOrDefault();
                if (selected == null)
                {
                    Messages.Message("No pawn selected", MessageTypeDefOf.RejectInput);
                    return;
                }

                var sb = new StringBuilder(2048);
                sb.AppendLine($"[SurvivalTools] Tool Gating Test for {selected.LabelCap}");
                sb.AppendLine("=======================================================");
                sb.AppendLine($"Test time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();

                var settings = SurvivalTools.Settings;
                sb.AppendLine($"Settings: hardcore={settings?.hardcoreMode == true}, extraHardcore={settings?.extraHardcoreMode == true}, showGatingAlert={settings?.showGatingAlert == true}");
                sb.AppendLine();

                // Show pawn's current tools
                sb.AppendLine("Pawn Tool Inventory:");
                var allTools = selected.GetAllUsableSurvivalTools().ToList();
                var equippedTool = selected.equipment?.Primary;

                // Clear tool stat resolver cache for accurate results
                Helpers.ToolStatResolver.ClearCaches();

                if (allTools.Any())
                {
                    foreach (var tool in allTools)
                    {
                        bool isEquipped = tool == equippedTool;
                        string location = isEquipped ? "[EQUIPPED]" : "[INVENTORY]";
                        string stuffInfo = tool.Stuff != null ? $" (stuff: {tool.Stuff.defName})" : "";
                        string qualityInfo = "";

                        // Get quality if available
                        if (tool.TryGetQuality(out QualityCategory quality))
                        {
                            qualityInfo = $" (quality: {quality})";
                        }

                        // Get condition
                        string conditionInfo = "";
                        if (tool.HitPoints < tool.MaxHitPoints)
                        {
                            float condition = (float)tool.HitPoints / tool.MaxHitPoints;
                            conditionInfo = $" (condition: {condition:P0})";
                        }

                        sb.AppendLine($"  {location} {tool.LabelCap}{stuffInfo}{qualityInfo}{conditionInfo}");

                        // Show tool stats for key work types
                        var keyStats = new[] { ST_StatDefOf.DiggingSpeed, ST_StatDefOf.TreeFellingSpeed, ST_StatDefOf.PlantHarvestingSpeed, StatDefOf.ConstructionSpeed }
                            .Where(s => s != null).ToList();

                        foreach (var stat in keyStats)
                        {
                            var info = Helpers.ToolStatResolver.GetToolStatInfo(tool.def, tool.Stuff, stat);
                            if (Math.Abs(info.Factor - 1f) > 0.001f) // Only show if meaningfully different from 1.0
                            {
                                sb.AppendLine($"    - {stat.defName}: {info.Factor:F3}x (Source: {info.Source})");

                                // Debug: Show why this factor was applied
                                if (info.Source == "Explicit")
                                {
                                    sb.AppendLine($"      -> Explicit mod extension or tool properties");
                                }
                                else if (info.Source == "StatBases")
                                {
                                    sb.AppendLine($"      -> Found in tool's statBases");
                                }
                                else if (info.Source == "NameHint")
                                {
                                    sb.AppendLine($"      -> Name hint: '{tool.def.label}' matched pattern");
                                }
                                else if (info.Source == "Default")
                                {
                                    sb.AppendLine($"      -> Default fallback (should be 1.0x)");
                                }
                            }
                        }
                    }
                }
                else
                {
                    sb.AppendLine("  No survival tools found in inventory or equipped");
                }
                sb.AppendLine();

                // Test representative WorkGivers with null guards (exact same pattern as live)
                var testWorkGivers = new[]
                {
                    DefDatabase<WorkGiverDef>.GetNamedSilentFail("Mine"),
                    DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToBlueprints"),
                    DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructFinishFrames"),
                    DefDatabase<WorkGiverDef>.GetNamedSilentFail("CutPlants"),
                    DefDatabase<WorkGiverDef>.GetNamedSilentFail("PlantsCut"),
                    DefDatabase<WorkGiverDef>.GetNamedSilentFail("PlantHarvest"),
                    DefDatabase<WorkGiverDef>.GetNamedSilentFail("GrowerHarvest"),
                    DefDatabase<WorkGiverDef>.GetNamedSilentFail("SmithWeapons"),
                    DefDatabase<WorkGiverDef>.GetNamedSilentFail("Smith"),
                    DefDatabase<WorkGiverDef>.GetNamedSilentFail("Repair")
                }.Where(wg => wg != null).ToArray();

                sb.AppendLine($"Testing {testWorkGivers.Length} representative WorkGivers:");
                sb.AppendLine();
                sb.AppendLine("WorkGiver\t\t\tResult\tTranslated Reason");
                sb.AppendLine("--------\t\t\t------\t-----------------");

                foreach (var wg in testWorkGivers)
                {
                    // Use exact JobGate.ShouldBlock call matching live logic
                    bool blocked = Gating.JobGate.ShouldBlock(selected, wg, null, false, out var reasonKey, out var a1, out var a2);
                    string result = blocked ? "BLOCK" : "ALLOW";
                    string reason = blocked ? reasonKey.Translate(a1, a2).ToString() : "No blocking";

                    string wgName = (wg.label ?? wg.defName).PadRight(24);
                    sb.AppendLine($"{wgName}\t{result}\t{reason}");
                }

                sb.AppendLine();
                sb.AppendLine("Notes:");
                sb.AppendLine("- This uses exact JobGate.ShouldBlock() logic matching live behavior");
                sb.AppendLine("- JobDef-specific gating tested during actual job creation (WorkGiver → Job)");
                sb.AppendLine("- 'forced=false' simulates normal work assignment (not player-forced)");
                sb.AppendLine($"- Current mode: {(settings?.hardcoreMode == true ? "Hardcore" : settings?.extraHardcoreMode == true ? "Nightmare" : "Normal")}");

                string fileName = $"ST_GatingTest_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var path = ST_FileIO.WriteUtf8Atomic(fileName, sb.ToString());

                Messages.Message($"Tool gating test completed - results saved to {path}", MessageTypeDefOf.TaskCompletion);
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools] Gating test failed: {ex}");
                Messages.Message("Gating test failed - check log for errors", MessageTypeDefOf.RejectInput);
            }
        }

        private static string CompatLine()
        {
            try { return string.Join(", ", Helpers.WorkSpeedGlobalHelper.GetWorkSpeedGlobalJobs().Select(wg => wg.defName)); } catch { return "(n/a)"; }
        }
    }
}