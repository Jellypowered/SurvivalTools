// RimWorld 1.6 / C# 7.3
// Source/DebugTools/DebugAction_AssignmentSystem.cs
//
// Phase 6: Debug tools for assignment system testing
// - Preview assignment decisions for selected pawns
// - Performance benchmarking for AssignmentSearch
// - Tool analysis and gating diagnostics

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using Verse;
using LudeonTK;
using SurvivalTools.Assign;
using SurvivalTools.Scoring;
using SurvivalTools.Gating;

namespace SurvivalTools.DebugTools
{
    public static class DebugAction_AssignmentSystem
    {
        /// <summary>
        /// Preview assignment decisions for the selected pawn.
        /// Shows what tools would be assigned for various work stats.
        /// </summary>
        [DebugAction("SurvivalTools", "Preview Assignments", false, false)]
        public static void PreviewAssignments()
        {
            var selectedPawn = Find.Selector.SingleSelectedThing as Pawn;
            if (selectedPawn == null)
            {
                Messages.Message("Select a pawn first", MessageTypeDefOf.RejectInput);
                return;
            }

            var settings = SurvivalTools.Settings;
            if (settings?.enableAssignments != true)
            {
                Messages.Message("Assignment system is disabled in settings", MessageTypeDefOf.RejectInput);
                return;
            }

            var report = new List<string>();
            report.Add($"Assignment Preview for {selectedPawn.LabelShort}:");
            report.Add("");

            var workStats = new[]
            {
                ST_StatDefOf.TreeFellingSpeed,
                ST_StatDefOf.PlantHarvestingSpeed,
                ST_StatDefOf.DiggingSpeed,
                StatDefOf.ConstructionSpeed,
                StatDefOf.SmoothingSpeed
            };

            foreach (var workStat in workStats)
            {
                if (workStat == null) continue;

                try
                {
                    var currentTool = ToolScoring.GetBestTool(selectedPawn, workStat, out float currentScore);
                    float baseline = SurvivalToolUtility.GetNoToolBaseline(workStat);

                    report.Add($"--- {workStat.label} ---");
                    report.Add($"Current tool: {currentTool?.LabelShort ?? "none"}");
                    report.Add($"Current score: {currentScore:F3} (baseline: {baseline:F3})");

                    bool isGated = currentScore <= baseline + 0.001f;
                    report.Add($"Gated: {(isGated ? "YES" : "no")}");

                    // Test assignment search
                    float minGainPct = settings.assignMinGainPct;
                    float searchRadius = settings.assignSearchRadius;
                    int pathCostBudget = settings.assignPathCostBudget;

                    bool wouldAssign = AssignmentSearch.TryUpgradeFor(selectedPawn, workStat, minGainPct, searchRadius, pathCostBudget, AssignmentSearch.QueuePriority.Append);
                    report.Add($"Would assign: {(wouldAssign ? "YES" : "no")}");

                    if (wouldAssign)
                    {
                        // Get updated score after assignment
                        var newTool = ToolScoring.GetBestTool(selectedPawn, workStat, out float newScore);
                        if (newTool != currentTool)
                        {
                            float gainPct = (newScore - currentScore) / Math.Max(currentScore, 0.001f);
                            report.Add($"Assigned tool: {newTool?.LabelShort ?? "none"}");
                            report.Add($"New score: {newScore:F3} (+{gainPct:P1})");
                        }
                    }

                    report.Add("");
                }
                catch (Exception ex)
                {
                    report.Add($"ERROR testing {workStat.label}: {ex.Message}");
                    report.Add("");
                }
            }

            Find.WindowStack.Add(new Dialog_MessageBox(string.Join("\n", report)));
        }

        /// <summary>
        /// Run performance benchmark on AssignmentSearch.TryUpgradeFor.
        /// Tests 100 iterations to ensure zero GC allocation requirement.
        /// </summary>
        [DebugAction("SurvivalTools", "Benchmark Assignment Search", false, false)]
        public static void BenchmarkAssignmentSearch()
        {
            var selectedPawn = Find.Selector.SingleSelectedThing as Pawn;
            if (selectedPawn == null)
            {
                Messages.Message("Select a pawn first", MessageTypeDefOf.RejectInput);
                return;
            }

            var settings = SurvivalTools.Settings;
            if (settings?.enableAssignments != true)
            {
                Messages.Message("Assignment system is disabled in settings", MessageTypeDefOf.RejectInput);
                return;
            }

            // Test with common work stat
            var workStat = ST_StatDefOf.TreeFellingSpeed ?? ST_StatDefOf.DiggingSpeed ?? StatDefOf.ConstructionSpeed;
            if (workStat == null)
            {
                Messages.Message("No valid work stat found for testing", MessageTypeDefOf.RejectInput);
                return;
            }

            const int iterations = 100;
            var report = new List<string>();
            report.Add($"Assignment Search Benchmark - {iterations} iterations");
            report.Add($"Pawn: {selectedPawn.LabelShort}");
            report.Add($"Work Stat: {workStat.label}");
            report.Add("");

            // Warm up
            for (int i = 0; i < 10; i++)
            {
                AssignmentSearch.TryUpgradeFor(selectedPawn, workStat, settings.assignMinGainPct,
                                             settings.assignSearchRadius, settings.assignPathCostBudget, AssignmentSearch.QueuePriority.Append);
            }

            // Measure GC before
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long gcBefore = GC.GetTotalMemory(false);

            // Benchmark
            var stopwatch = Stopwatch.StartNew();

            int successCount = 0;
            for (int i = 0; i < iterations; i++)
            {
                if (AssignmentSearch.TryUpgradeFor(selectedPawn, workStat, settings.assignMinGainPct,
                                                 settings.assignSearchRadius, settings.assignPathCostBudget, AssignmentSearch.QueuePriority.Append))
                {
                    successCount++;
                }
            }

            stopwatch.Stop();

            // Measure GC after
            long gcAfter = GC.GetTotalMemory(false);
            long gcAllocated = gcAfter - gcBefore;

            // Results
            double avgMs = stopwatch.Elapsed.TotalMilliseconds / iterations;
            report.Add($"Total time: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            report.Add($"Average per call: {avgMs:F3}ms");
            report.Add($"Successful assignments: {successCount}/{iterations}");
            report.Add($"GC allocated: {gcAllocated} bytes");

            if (gcAllocated > 1000)
            {
                report.Add("");
                report.Add("⚠️ WARNING: High GC allocation detected!");
                report.Add("Phase 6 requirement: Zero allocation in hot path");
            }
            else
            {
                report.Add("");
                report.Add("✅ GC allocation within acceptable range");
            }

            Find.WindowStack.Add(new Dialog_MessageBox(string.Join("\n", report)));
        }

        /// <summary>
        /// Show detailed tool analysis for a pawn and work stat.
        /// Displays scoring breakdown and available alternatives.
        /// </summary>
        [DebugAction("SurvivalTools", "Analyze Tool Scoring", false, false)]
        public static void AnalyzeToolScoring()
        {
            var selectedPawn = Find.Selector.SingleSelectedThing as Pawn;
            if (selectedPawn == null)
            {
                Messages.Message("Select a pawn first", MessageTypeDefOf.RejectInput);
                return;
            }

            // Get primary work stat for analysis
            var workStat = ST_StatDefOf.TreeFellingSpeed ?? ST_StatDefOf.DiggingSpeed ?? StatDefOf.ConstructionSpeed;
            if (workStat == null)
            {
                Messages.Message("No valid work stat found for analysis", MessageTypeDefOf.RejectInput);
                return;
            }

            var report = new List<string>();
            report.Add($"Tool Scoring Analysis");
            report.Add($"Pawn: {selectedPawn.LabelShort}");
            report.Add($"Work Stat: {workStat.label}");
            report.Add("");

            try
            {
                // Current best tool
                var currentTool = ToolScoring.GetBestTool(selectedPawn, workStat, out float currentScore);
                float baseline = SurvivalToolUtility.GetNoToolBaseline(workStat);

                report.Add("=== CURRENT BEST TOOL ===");
                report.Add($"Tool: {currentTool?.LabelShort ?? "none"}");
                report.Add($"Score: {currentScore:F3}");
                report.Add($"Baseline: {baseline:F3}");
                report.Add($"Improvement: {((currentScore - baseline) / baseline):P1}");
                report.Add("");

                // Available tools analysis
                report.Add("=== AVAILABLE TOOLS ===");
                var availableTools = selectedPawn.GetAllUsableSurvivalTools().ToList();
                if (availableTools.Any())
                {
                    var toolScores = new List<(Thing tool, float score)>();

                    foreach (var tool in availableTools)
                    {
                        if (tool != null)
                        {
                            float score = ToolScoring.Score(tool, selectedPawn, workStat);
                            toolScores.Add((tool, score));
                        }
                    }

                    toolScores.Sort((a, b) => b.score.CompareTo(a.score));

                    foreach (var (tool, score) in toolScores.Take(5))
                    {
                        var isCurrent = tool == currentTool ? " (CURRENT)" : "";
                        var improvement = score > baseline ? $" (+{((score - baseline) / baseline):P1})" : "";
                        report.Add($"{tool.LabelShort}: {score:F3}{improvement}{isCurrent}");
                    }
                }
                else
                {
                    report.Add("No tools available");
                }

                report.Add("");

                // Tool contributors breakdown
                if (currentTool != null)
                {
                    report.Add("=== SCORE CONTRIBUTORS ===");
                    var contributors = ToolScoring.TopContributors(currentTool, selectedPawn, workStat, 3);
                    foreach (var (contributorTool, contribution) in contributors)
                    {
                        report.Add($"{contributorTool.LabelShort}: +{contribution:F3}");
                    }
                }
            }
            catch (Exception ex)
            {
                report.Add($"ERROR: {ex.Message}");
            }

            Find.WindowStack.Add(new Dialog_MessageBox(string.Join("\n", report)));
        }

        /// <summary>
        /// Test assignment system integration with different settings.
        /// Validates that settings changes affect assignment behavior correctly.
        /// </summary>
        [DebugAction("SurvivalTools", "Test Assignment Settings", false, false)]
        public static void TestAssignmentSettings()
        {
            var settings = SurvivalTools.Settings;
            if (settings == null)
            {
                Messages.Message("Settings not available", MessageTypeDefOf.RejectInput);
                return;
            }

            var report = new List<string>();
            report.Add("Assignment Settings Test");
            report.Add("");

            report.Add("=== CURRENT SETTINGS ===");
            report.Add($"Enabled: {settings.enableAssignments}");
            report.Add($"Min Gain %: {settings.assignMinGainPct:P1}");
            report.Add($"Search Radius: {settings.assignSearchRadius}");
            report.Add($"Path Cost Budget: {settings.assignPathCostBudget}");
            report.Add($"Rescue on Gate: {settings.assignRescueOnGate}");
            report.Add("");

            report.Add("=== DIFFICULTY SCALING ===");
            report.Add($"Current Mode: {settings.CurrentMode}");

            // Calculate scaled values
            float minGainPct = settings.assignMinGainPct;
            float searchRadius = settings.assignSearchRadius;
            int pathCostBudget = settings.assignPathCostBudget;

            if (settings.extraHardcoreMode)
            {
                minGainPct *= 1.5f;
                searchRadius *= 0.5f;
                pathCostBudget /= 2;
            }
            else if (settings.hardcoreMode)
            {
                minGainPct *= 1.25f;
                searchRadius *= 0.75f;
                pathCostBudget = (pathCostBudget * 3) / 4;
            }

            report.Add($"Scaled Min Gain %: {minGainPct:P1}");
            report.Add($"Scaled Search Radius: {searchRadius}");
            report.Add($"Scaled Path Cost Budget: {pathCostBudget}");

            Find.WindowStack.Add(new Dialog_MessageBox(string.Join("\n", report)));
        }
    }
}