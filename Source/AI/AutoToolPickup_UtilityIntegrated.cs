using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    public static class Patch_JobGiver_Work_TryIssueJobPackage_AutoTool
    {
        private const int SearchRadius = 28;

        public static void Postfix(Pawn pawn, JobIssueParams jobParams, ref ThinkResult __result)
        {
            // Initial checks for applicability
            if (!ShouldAttemptAutoTool(__result.Job, pawn, out var requiredStats))
            {
                return;
            }

            bool isDebug = SurvivalToolUtility.IsDebugLoggingEnabled;
            string logKey = $"AutoTool_Running_{pawn.ThingID}_{__result.Job.def.defName}";
            if (isDebug && SurvivalToolUtility.ShouldLogWithCooldown(logKey))
                Log.Message($"[SurvivalTools.AutoTool] Running for {pawn.LabelShort} doing {__result.Job.def.defName} (needs {string.Join(", ", requiredStats.Select(s => s.defName))})");

            // If pawn already has a suitable tool, we're done.
            if (PawnHasHelpfulTool(pawn, requiredStats))
            {
                string skipLogKey = $"AutoTool_Skip_{pawn.ThingID}";
                if (isDebug && SurvivalToolUtility.ShouldLogWithCooldown(skipLogKey))
                    Log.Message($"[SurvivalTools.AutoTool] Pawn already has a helpful tool. Skipping.");
                return;
            }

            // In hardcore mode, if no tool is held or can be acquired, cancel the job entirely.
            // EXCEPTION: Cleaning, butchery, and medical jobs are usually allowed (just less effective without tools)
            // EXTRA HARDCORE: Even optional jobs can be blocked if extra hardcore mode is enabled
            if (SurvivalTools.Settings.hardcoreMode)
            {
                bool shouldBlockJob = false;

                foreach (var stat in requiredStats)
                {
                    // Check if this stat should block work when tools are missing
                    if (ShouldBlockJobForMissingStat(stat) && !CanAcquireHelpfulToolNow(pawn, requiredStats, isDebug))
                    {
                        shouldBlockJob = true;
                        break;
                    }
                }

                if (shouldBlockJob)
                {
                    if (isDebug) Log.Message($"[SurvivalTools.AutoTool] Hardcore mode: No tool available for {pawn.LabelShort} doing {__result.Job.def.defName}. Cancelling job.");
                    __result = ThinkResult.NoJob;
                    return;
                }
            }

            // Find the best tool available on the map
            var bestTool = FindBestHelpfulTool(pawn, requiredStats, isDebug);
            if (bestTool == null)
            {
                if (isDebug) Log.Message($"[SurvivalTools.AutoTool] No suitable tool found for {pawn.LabelShort}.");
                return;
            }

            // We found a tool. Now, construct the job sequence to get it.
            __result = CreateToolPickupJobs(pawn, __result.Job, bestTool, requiredStats, __result.SourceNode);
        }

        private static bool ShouldAttemptAutoTool(Job job, Pawn pawn, out List<StatDef> requiredStats)
        {
            requiredStats = null;

            if (SurvivalTools.Settings?.autoTool != true ||
                job == null ||
                pawn?.Map == null ||
                !pawn.CanUseSurvivalTools() ||
                pawn.Drafted ||
                pawn.InMentalState ||
                job.def == JobDefOf.TakeInventory)
            {
                return false;
            }

            // Special case: if this is a tree felling job (FellTree or tree CutPlant), use TreeFellingSpeed
            if (job.def == ST_JobDefOf.FellTree || job.def == ST_JobDefOf.FellTreeDesignated ||
                (job.def == JobDefOf.CutPlant && job.targetA.Thing?.def?.plant?.IsTree == true))
            {
                requiredStats = new List<StatDef> { ST_StatDefOf.TreeFellingSpeed };
                return true;
            }

            requiredStats = SurvivalToolUtility.RelevantStatsFor(job.workGiverDef, job);
            return !requiredStats.NullOrEmpty();
        }

        private static bool PawnHasHelpfulTool(Pawn pawn, List<StatDef> requiredStats)
        {
            return pawn.GetAllUsableSurvivalTools().Any(t => t is SurvivalTool st && ToolImprovesAnyRequiredStat(st, requiredStats));
        }

        private static bool CanAcquireHelpfulToolNow(Pawn pawn, List<StatDef> stats, bool isDebug)
        {
            return FindBestHelpfulTool(pawn, stats, isDebug) != null;
        }

        private static SurvivalTool FindBestHelpfulTool(Pawn pawn, List<StatDef> requiredStats, bool isDebug)
        {
            var assignment = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>()?.CurrentSurvivalToolAssignment;

            SurvivalTool bestTool = null;
            float bestScore = 0f;

            var potentialTools = GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, SearchRadius, true)
                                          .OfType<SurvivalTool>();

            if (isDebug)
                Log.Message($"[SurvivalTools.AutoTool] Found {potentialTools.Count()} potential tools in search radius");

            foreach (var tool in potentialTools)
            {
                if (!IsViableCandidate(tool, pawn, assignment, requiredStats, out string reason))
                {
                    if (isDebug)
                        Log.Message($"[SurvivalTools.AutoTool] Rejecting {tool.def.defName}: {reason}");
                    continue;
                }

                float score = SurvivalToolScore(tool, requiredStats);

                // In hardcore mode, allow tools even with score 0 if they have the required stat
                // In non-hardcore mode, don't consider tools that have no relevant stats (score would be 0)
                if (score <= 0f && !SurvivalToolUtility.IsHardcoreModeEnabled)
                    continue;

                score -= 0.01f * tool.Position.DistanceTo(pawn.Position); // Tie-breaker for distance

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTool = tool;
                }
            }

            if (isDebug && bestTool != null)
            {
                Log.Message($"[SurvivalTools.AutoTool] Found best tool: {bestTool.LabelCap} at {bestTool.Position} with score {bestScore:F2}");
            }

            return bestTool;
        }

        private static bool IsViableCandidate(SurvivalTool tool, Pawn pawn, SurvivalToolAssignment assignment, List<StatDef> requiredStats, out string reason)
        {
            reason = null;
            if (!tool.Spawned) { reason = "Not spawned"; return false; }
            if (tool.IsForbidden(pawn)) { reason = "Is forbidden"; return false; }
            if (tool.IsBurning()) { reason = "Is burning"; return false; }
            if (assignment?.filter != null && !assignment.filter.Allows(tool)) { reason = "Disallowed by assignment"; return false; }
            if (!ToolIsAcquirableByPolicy(pawn, tool)) { reason = "Disallowed by storage policy"; return false; }
            if (!ToolImprovesAnyRequiredStat(tool, requiredStats))
            {
                string statsNeeded = requiredStats?.Any() == true ? $" ({string.Join(", ", requiredStats.Select(s => s.defName))})" : "";
                reason = $"Does not improve required stat{statsNeeded}";
                return false;
            }
            if (!pawn.CanReserveAndReach(tool, PathEndMode.OnCell, pawn.NormalMaxDanger())) { reason = "Cannot reserve or reach"; return false; }

            return true;
        }

        private static ThinkResult CreateToolPickupJobs(Pawn pawn, Job originalJob, SurvivalTool toolToGet, List<StatDef> requiredStats, ThinkNode sourceNode)
        {
            var jobQueue = pawn.jobs.jobQueue;
            jobQueue.EnqueueFirst(originalJob);

            var pickupJob = JobMaker.MakeJob(JobDefOf.TakeInventory, toolToGet);
            pickupJob.count = 1;

            // Check if pawn already has a tool of the same type
            var sameTypeTool = FindSameTypeHeldTool(pawn, toolToGet);
            if (sameTypeTool != null)
            {
                // Compare the tools to see which is better for the required stats
                float currentToolScore = SurvivalToolScore(sameTypeTool, requiredStats);
                float newToolScore = SurvivalToolScore(toolToGet, requiredStats);

                if (newToolScore > currentToolScore)
                {
                    if (SurvivalToolUtility.IsDebugLoggingEnabled)
                        Log.Message($"[SurvivalTools.AutoTool] {pawn.LabelShort} will replace {sameTypeTool.LabelShort} (score: {currentToolScore:F2}) with better {toolToGet.LabelShort} (score: {newToolScore:F2}).");

                    // Drop the worse tool of the same type
                    jobQueue.EnqueueFirst(pickupJob);
                    var dropJob = pawn.DequipAndTryStoreSurvivalTool(sameTypeTool, false);
                    return new ThinkResult(dropJob, sourceNode, JobTag.Misc, false);
                }
                else
                {
                    if (SurvivalToolUtility.IsDebugLoggingEnabled)
                        Log.Message($"[SurvivalTools.AutoTool] {pawn.LabelShort} already has better tool of same type: {sameTypeTool.LabelShort} (score: {currentToolScore:F2}) vs {toolToGet.LabelShort} (score: {newToolScore:F2}). Skipping pickup.");

                    // Don't pick up the inferior tool of the same type
                    return new ThinkResult(originalJob, sourceNode, JobTag.Misc, false);
                }
            }

            // If we need to make space for a different type of tool, find a tool to drop.
            if (!pawn.CanCarryAnyMoreSurvivalTools())
            {
                var toolToDrop = FindDroppableHeldTool(pawn, requiredStats, toolToGet);
                if (toolToDrop != null)
                {
                    if (SurvivalToolUtility.IsDebugLoggingEnabled)
                        Log.Message($"[SurvivalTools.AutoTool] {pawn.LabelShort} will drop {toolToDrop.LabelShort} to pick up {toolToGet.LabelShort}.");

                    jobQueue.EnqueueFirst(pickupJob);
                    var dropJob = pawn.DequipAndTryStoreSurvivalTool(toolToDrop, false);
                    return new ThinkResult(dropJob, sourceNode, JobTag.Misc, false);
                }
            }

            return new ThinkResult(pickupJob, sourceNode, JobTag.Misc, false);
        }

        private static SurvivalTool FindDroppableHeldTool(Pawn pawn, List<StatDef> requiredStats, SurvivalTool toolWeWant)
        {
            var droppableTools = pawn.GetHeldSurvivalTools()
                .OfType<SurvivalTool>()
                .Where(t => pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>()?.forcedHandler.AllowedToAutomaticallyDrop(t) ?? true)
                .ToList();

            if (droppableTools.NullOrEmpty()) return null;

            // First, try to drop a tool that is irrelevant to the current job (specific stats needed).
            var irrelevantTool = droppableTools.FirstOrDefault(t => !ToolImprovesAnyRequiredStat(t, requiredStats));
            if (irrelevantTool != null)
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    Log.Message($"[SurvivalTools.AutoTool] {pawn.LabelShort} will drop irrelevant tool {irrelevantTool.LabelShort} for current job.");
                return irrelevantTool;
            }

            // If all held tools are relevant to the current job, drop the one with the lowest score,
            // but only if the new tool is better.
            float wantScore = SurvivalToolScore(toolWeWant, requiredStats);
            var worstTool = droppableTools
                .Select(t => new { Tool = t, Score = SurvivalToolScore(t, requiredStats) })
                .Where(x => x.Score < wantScore)
                .OrderBy(x => x.Score)
                .FirstOrDefault()?.Tool;

            if (worstTool != null && SurvivalToolUtility.IsDebugLoggingEnabled)
                Log.Message($"[SurvivalTools.AutoTool] {pawn.LabelShort} will drop lower-scoring tool {worstTool.LabelShort} for current job.");

            return worstTool;
        }

        private static bool ToolImprovesAnyRequiredStat(SurvivalTool tool, List<StatDef> requiredStats)
        {
            // Handle special stat groups that only require at least one stat to be present
            var workStatFactors = tool.WorkStatFactors.ToList();

            // Check for medical stats group (either MedicalOperationSpeed OR MedicalSurgerySuccessChance)
            var medicalStats = new[] { ST_StatDefOf.MedicalOperationSpeed, ST_StatDefOf.MedicalSurgerySuccessChance };
            if (requiredStats.Any(s => medicalStats.Contains(s)))
            {
                bool hasMedicalStat = workStatFactors.Any(m => medicalStats.Contains(m.stat));
                if (hasMedicalStat)
                {
                    // In hardcore mode, any medical tool with the required stat is useful
                    if (SurvivalToolUtility.IsHardcoreModeEnabled)
                        return true;
                    // In non-hardcore mode, only if it actually improves the stat
                    return workStatFactors.Any(m => medicalStats.Contains(m.stat) && m.value > 1.0f);
                }
            }

            // Check for butchery stats group (either ButcheryFleshSpeed OR ButcheryFleshEfficiency)
            var butcheryStats = new[] { ST_StatDefOf.ButcheryFleshSpeed, ST_StatDefOf.ButcheryFleshEfficiency };
            if (requiredStats.Any(s => butcheryStats.Contains(s)))
            {
                bool hasButcheryStat = workStatFactors.Any(m => butcheryStats.Contains(m.stat));
                if (hasButcheryStat)
                {
                    // In hardcore mode, any butchery tool with the required stat is useful
                    if (SurvivalToolUtility.IsHardcoreModeEnabled)
                        return true;
                    // In non-hardcore mode, only if it actually improves the stat
                    return workStatFactors.Any(m => butcheryStats.Contains(m.stat) && m.value > 1.0f);
                }
            }

            // For cleaning, it's always optional (never required in hardcore mode)
            if (requiredStats.Contains(ST_StatDefOf.CleaningSpeed))
            {
                bool hasCleaningStat = workStatFactors.Any(m => m.stat == ST_StatDefOf.CleaningSpeed);
                if (hasCleaningStat)
                {
                    // Cleaning tools are always beneficial but never required
                    return workStatFactors.Any(m => m.stat == ST_StatDefOf.CleaningSpeed && m.value > 1.0f);
                }
            }

            // Handle other individual stats (traditional logic)
            var otherStats = requiredStats.Except(medicalStats).Except(butcheryStats).Where(s => s != ST_StatDefOf.CleaningSpeed).ToList();
            if (otherStats.Any())
            {
                bool hasRequiredStat = otherStats.Any(stat => workStatFactors.Any(m => m.stat == stat));

                // In hardcore mode, any tool with the required stat is useful (even 1.0x factor)
                // because it allows the job to proceed when it would otherwise be blocked
                if (hasRequiredStat && SurvivalToolUtility.IsHardcoreModeEnabled)
                    return true;

                // In non-hardcore mode, only consider tools that actually improve stats (>1.0x)
                return otherStats.Any(stat =>
                    workStatFactors.Any(m => m.stat == stat && m.value > 1.0f));
            }

            return false;
        }

        private static float SurvivalToolScore(SurvivalTool tool, List<StatDef> workRelevantStats)
        {
            float optimality = 0f;
            var workStatFactors = tool.WorkStatFactors.ToList();
            bool hasAnyRequiredStat = false;

            foreach (var stat in workRelevantStats)
            {
                // Only count stats that the tool actually has modifiers for
                var modifier = workStatFactors.FirstOrDefault(m => m.stat == stat);
                if (modifier != null)
                {
                    // In hardcore mode, enforce tool type restrictions for specific stats
                    if (SurvivalToolUtility.IsHardcoreModeEnabled && !IsMultitool(tool))
                    {
                        // Only sickles can provide PlantHarvestingSpeed in hardcore mode
                        if (stat == ST_StatDefOf.PlantHarvestingSpeed &&
                            ToolUtility.ToolKindOf(tool) != STToolKind.Sickle)
                        {
                            continue; // Skip this stat modifier for non-sickle tools
                        }

                        // Add restrictions for other stats based on tool type
                        // Note: Medical and butchery stats don't have tool type restrictions
                        // since any tool with those stats should work (scalpels, cleavers, etc.)
                    }

                    hasAnyRequiredStat = true;
                    optimality += modifier.value;
                }
            }

            // In hardcore mode, ensure tools with required stats get a minimum viable score
            if (SurvivalToolUtility.IsHardcoreModeEnabled && hasAnyRequiredStat && optimality <= 0f)
            {
                optimality = 0.1f; // Minimum score to make it viable
            }

            if (tool.def.useHitPoints)
            {
                float hpFrac = tool.MaxHitPoints > 0 ? (float)tool.HitPoints / tool.MaxHitPoints : 0f;
                float lifespanRemaining = tool.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan) * hpFrac;
                optimality *= LifespanDaysToOptimalityMultiplierCurve.Evaluate(lifespanRemaining);
            }
            return optimality;
        }

        private static bool ToolIsAcquirableByPolicy(Pawn pawn, SurvivalTool tool)
        {
            if (tool.IsInAnyStorage())
                return true;

            if (SurvivalTools.Settings?.pickupFromStorageOnly == true)
                return false;

            return pawn.Map.areaManager.Home[tool.Position];
        }

        private static SurvivalTool FindSameTypeHeldTool(Pawn pawn, SurvivalTool targetTool)
        {
            return pawn.GetHeldSurvivalTools()
                .OfType<SurvivalTool>()
                .FirstOrDefault(heldTool => AreSameToolType(heldTool, targetTool));
        }

        private static bool AreSameToolType(SurvivalTool tool1, SurvivalTool tool2)
        {
            // Tools are the same type if they modify the same set of stats
            var tool1Stats = tool1.WorkStatFactors.Select(f => f.stat).ToHashSet();
            var tool2Stats = tool2.WorkStatFactors.Select(f => f.stat).ToHashSet();

            // If both tools have no stat modifiers, they're not useful tools
            if (tool1Stats.Count == 0 || tool2Stats.Count == 0)
                return false;

            // Tools are same type if they have the same set of stat modifiers
            return tool1Stats.SetEquals(tool2Stats);
        }

        /// <summary>
        /// Checks if a tool is a multitool that can perform all jobs regardless of hardcore mode restrictions.
        /// A multitool is identified by either being the specific multitool def or having stat modifiers for 
        /// multiple different job types (3+ different stats covering different tool categories).
        /// </summary>
        private static bool IsMultitool(SurvivalTool tool)
        {
            if (tool?.def == null) return false;

            // Check if it's the specific multitool def
            if (tool.def == ST_ThingDefOf.SurvivalTools_Multitool) return true;

            // Check if it has "multitool" in the name
            string defName = tool.def.defName?.ToLowerInvariant() ?? string.Empty;
            if (defName.Contains("multitool") || defName.Contains("omni") || defName.Contains("universal")) return true;

            // Check if it has stat modifiers for multiple job categories (3+ different stats suggests multitool)
            var statCount = tool.WorkStatFactors.Count();
            return statCount >= 3;
        }

        /// <summary>
        /// Determines if a job should be blocked when tools for the given stat are missing.
        /// In regular hardcore mode, only required stats block jobs.
        /// In extra hardcore mode, optional stats can also block jobs based on user settings.
        /// </summary>
        private static bool ShouldBlockJobForMissingStat(StatDef stat)
        {
            var settings = SurvivalTools.Settings;
            if (settings == null) return false;

            // Always block for required stats in hardcore mode
            bool isOptionalStat = IsOptionalStat(stat);
            if (!isOptionalStat) return true; // Required stats always block in hardcore mode

            // For optional stats, check extra hardcore mode settings
            if (settings.extraHardcoreMode && settings.IsStatRequiredInExtraHardcore(stat))
            {
                return true;
            }

            return false; // Optional stats don't block by default
        }

        /// <summary>
        /// Checks if a stat is considered "optional" (cleaning, butchery, medical)
        /// vs required (mining, construction, etc.)
        /// </summary>
        private static bool IsOptionalStat(StatDef stat)
        {
            return stat == ST_StatDefOf.CleaningSpeed ||
                   stat == ST_StatDefOf.ButcheryFleshSpeed ||
                   stat == ST_StatDefOf.ButcheryFleshEfficiency ||
                   stat == ST_StatDefOf.MedicalOperationSpeed ||
                   stat == ST_StatDefOf.MedicalSurgerySuccessChance;
        }

        private static readonly SimpleCurve LifespanDaysToOptimalityMultiplierCurve = new SimpleCurve
        {
            new CurvePoint(0f,   0.04f),
            new CurvePoint(0.5f, 0.20f),
            new CurvePoint(1f,   0.50f),
            new CurvePoint(2f,   1.00f),
            new CurvePoint(4f,   1.00f),
            new CurvePoint(999f, 1.00f)
        };
    }
}