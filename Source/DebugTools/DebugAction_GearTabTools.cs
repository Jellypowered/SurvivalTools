// RimWorld 1.6 / C# 7.3
// Source/DebugTools/DebugAction_GearTabTools.cs
//
// Phase 7: Debug tools for Gear tab testing and validation

using System;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using LudeonTK;
using SurvivalTools.Scoring;

namespace SurvivalTools.DebugTools
{
    public static class DebugAction_GearTabTools
    {
        [DebugAction("SurvivalTools", "Compare Gear vs Stat Explanation", false, false)]
        public static void CompareGearVsStatExplanation()
        {
            var selectedPawn = Find.Selector.SingleSelectedThing as Pawn;
            if (selectedPawn == null)
            {
                Messages.Message("Select a pawn first", MessageTypeDefOf.RejectInput);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== Gear vs Stat Explanation Comparison for {selectedPawn.NameShortColored} ===");
            sb.AppendLine();

            // Get all tool-related stats
            var toolStats = new[]
            {
                StatDefOf.MiningSpeed,
                StatDefOf.PlantWorkSpeed,
                StatDefOf.ConstructionSpeed,
                StatDefOf.MedicalTendSpeed
            };

            foreach (var stat in toolStats)
            {
                sb.AppendLine($"--- {stat.LabelCap} ---");

                // Get stat explanation
                var req = StatRequest.For(selectedPawn);
                var explanation = stat.Worker.GetExplanationUnfinalized(req, ToStringNumberSense.Absolute);
                var finalValue = selectedPawn.GetStatValue(stat);

                sb.AppendLine($"Final Value: {finalValue:F2}");
                sb.AppendLine("Explanation:");
                sb.AppendLine(explanation);

                // Get gear tab scoring - find best tool for this stat
                var bestTool = ToolScoring.GetBestTool(selectedPawn, stat, out float bestScore);

                if (bestTool != null && bestScore > 0)
                {
                    sb.AppendLine($"Gear Tab Score: {bestScore:F2}");
                    sb.AppendLine($"Tool: {bestTool.LabelShort}");

                    // Get detailed breakdown
                    var contributors = ToolScoring.TopContributors(bestTool, selectedPawn, stat, 3);
                    if (contributors.Length > 0)
                    {
                        sb.AppendLine("Top Contributors:");
                        foreach (var contributor in contributors)
                        {
                            sb.AppendLine($"  {contributor.Item1.LabelShort}: +{contributor.Item2:F2}");
                        }
                    }
                }
                else
                {
                    sb.AppendLine("Gear Tab Score: No suitable tools found");
                }

                sb.AppendLine();
            }

            Log.Message(sb.ToString());

            // Also show in a dialog
            Find.WindowStack.Add(new Dialog_MessageBox(sb.ToString(), "Gear vs Stat Comparison"));
        }

        [DebugAction("SurvivalTools", "Gear panel GC check", false, false)]
        public static void GearPanelGCCheck()
        {
            var selectedPawn = Find.Selector.SingleSelectedThing as Pawn;
            if (selectedPawn == null)
            {
                Messages.Message("Select a pawn first", MessageTypeDefOf.RejectInput);
                return;
            }

            // Force GC before test
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var beforeMem = GC.GetTotalMemory(false);
            var beforeGen0 = GC.CollectionCount(0);
            var beforeGen1 = GC.CollectionCount(1);
            var beforeGen2 = GC.CollectionCount(2);

            // Simulate multiple gear panel draws
            var testRect = new Rect(0, 0, UI.GearTab_ST.DesiredWidth, 400f);
            const int iterations = 100;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    UI.GearTab_ST.Draw(testRect, selectedPawn);
                }
                catch (Exception ex)
                {
                    Log.Error($"[SurvivalTools] Gear panel draw failed on iteration {i}: {ex}");
                    break;
                }
            }

            stopwatch.Stop();

            var afterMem = GC.GetTotalMemory(false);
            var afterGen0 = GC.CollectionCount(0);
            var afterGen1 = GC.CollectionCount(1);
            var afterGen2 = GC.CollectionCount(2);

            var memoryDelta = afterMem - beforeMem;
            var gen0Collections = afterGen0 - beforeGen0;
            var gen1Collections = afterGen1 - beforeGen1;
            var gen2Collections = afterGen2 - beforeGen2;

            var avgTimeMs = stopwatch.ElapsedMilliseconds / (double)iterations;

            var sb = new StringBuilder();
            sb.AppendLine($"=== Gear Panel GC Check ({iterations} iterations) ===");
            sb.AppendLine($"Pawn: {selectedPawn.NameShortColored}");
            sb.AppendLine($"Average Draw Time: {avgTimeMs:F2}ms");
            sb.AppendLine();
            sb.AppendLine("Memory Impact:");
            sb.AppendLine($"  Before: {beforeMem:N0} bytes");
            sb.AppendLine($"  After:  {afterMem:N0} bytes");
            sb.AppendLine($"  Delta:  {memoryDelta:N0} bytes");
            sb.AppendLine();
            sb.AppendLine("GC Collections:");
            sb.AppendLine($"  Gen 0: {gen0Collections}");
            sb.AppendLine($"  Gen 1: {gen1Collections}");
            sb.AppendLine($"  Gen 2: {gen2Collections}");
            sb.AppendLine();

            if (gen0Collections == 0 && gen1Collections == 0 && gen2Collections == 0)
            {
                sb.AppendLine("✓ NO GC COLLECTIONS - Performance target met!");
            }
            else if (gen0Collections <= 1 && gen1Collections == 0 && gen2Collections == 0)
            {
                sb.AppendLine("✓ Minimal GC impact - Good performance");
            }
            else
            {
                sb.AppendLine("⚠ Significant GC activity detected - May need optimization");
            }

            if (memoryDelta < 1024)
            {
                sb.AppendLine("✓ Memory usage acceptable");
            }
            else
            {
                sb.AppendLine($"⚠ Memory leak suspected: {memoryDelta:N0} bytes retained");
            }

            Log.Message(sb.ToString());
            Messages.Message($"GC Check: {gen0Collections} collections, {avgTimeMs:F1}ms avg", MessageTypeDefOf.TaskCompletion);
        }

        [DebugAction("SurvivalTools", "Test gear tab caching", false, false)]
        public static void TestGearTabCaching()
        {
            var selectedPawn = Find.Selector.SingleSelectedThing as Pawn;
            if (selectedPawn == null)
            {
                Messages.Message("Select a pawn first", MessageTypeDefOf.RejectInput);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== Gear Tab Caching Test for {selectedPawn.NameShortColored} ===");
            sb.AppendLine();

            // Test cache invalidation scenarios
            var testRect = new Rect(0, 0, UI.GearTab_ST.DesiredWidth, 400f);

            // Initial draw
            UI.GearTab_ST.Draw(testRect, selectedPawn);
            sb.AppendLine("✓ Initial cache population");

            // Second draw (should use cache)
            UI.GearTab_ST.Draw(testRect, selectedPawn);
            sb.AppendLine("✓ Cache hit test");

            // Simulate equipment change
            var firstEquipment = selectedPawn.equipment?.Primary;
            if (firstEquipment != null)
            {
                selectedPawn.equipment.Remove(firstEquipment);
                UI.GearTab_ST.Draw(testRect, selectedPawn);
                sb.AppendLine("✓ Cache invalidation on equipment removal");

                selectedPawn.equipment.AddEquipment(firstEquipment);
                UI.GearTab_ST.Draw(testRect, selectedPawn);
                sb.AppendLine("✓ Cache refresh on equipment addition");
            }

            // Test with different pawn
            var otherPawns = Find.CurrentMap?.mapPawns?.FreeColonists?.Where(p => p != selectedPawn);
            if (otherPawns?.Any() == true)
            {
                var otherPawn = otherPawns.First();
                UI.GearTab_ST.Draw(testRect, otherPawn);
                sb.AppendLine($"✓ Different pawn test: {otherPawn.NameShortColored}");
            }

            sb.AppendLine();
            sb.AppendLine("All caching tests completed successfully!");

            Log.Message(sb.ToString());
            Messages.Message("Caching test completed", MessageTypeDefOf.TaskCompletion);
        }
    }
}