// RimWorld 1.6 / C# 7.3
// Source/Assign/PreWork_AutoEquip.cs
//
// Phase 6: Pre-work auto-equip integration
// - Harmony prefix for Pawn_JobTracker.TryTakeOrderedJob
// - Provides seamless tool upgrading before work begins
// - Settings-driven behavior with performance safeguards

using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using SurvivalTools.Gating;
using SurvivalTools.Scoring;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Assign
{
    [HarmonyPatch]
    public static class PreWork_AutoEquip
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_JobTracker), "pawn");
        // Track pending WorkGiver tool checks between prefix and postfix
        private static readonly System.Collections.Generic.Dictionary<int, StatDef> _wgPendingStat = new System.Collections.Generic.Dictionary<int, StatDef>(64);

        // (removed unused _patchApplied flag)

        /// <summary>
        /// Static constructor to verify patch application
        /// </summary>
        static PreWork_AutoEquip()
        {
            try
            {
                Log.Warning("[SurvivalTools.PreWork] PreWork_AutoEquip static constructor called - class is being loaded");
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools.PreWork] Static constructor failed: {ex}");
            }
        }

        /// <summary>
        /// Harmony prefix for Pawn_JobTracker.TryTakeOrderedJob.
        /// Checks if job requires tools and attempts auto-equip if beneficial.
        /// </summary>
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
        [HarmonyPrefix]
        public static bool TryTakeOrderedJob_Prefix(Pawn_JobTracker __instance, Job job, ref bool __result)
        {
            try
            {
                // IMMEDIATE LOGGING - This should ALWAYS show up to verify patch is working
                LogDebug("[SurvivalTools.PreWork] TryTakeOrderedJob_Prefix CALLED - patch is working!", "PreWork.TryTakeOrderedJob_Prefix");

                // Early-out checks
                if (__instance == null || job?.def == null)
                    return true; // Continue with original

                var pawn = (Pawn)PawnField.GetValue(__instance);
                if (pawn == null)
                    return true;

                // VALIDATION: Use JobUtils to validate job before processing
                if (!JobUtils.IsJobStillValid(job, pawn))
                {
                    LogWarning($"[SurvivalTools.PreWork] Job {job?.def?.defName} is invalid for {pawn.LabelShort}, blocking");
                    __result = false;
                    return false;
                }

                // IMMEDIATE LOGGING for ALL ordered jobs to see what's happening
                LogDebug($"[SurvivalTools.PreWork] ORDERED JOB: {pawn.LabelShort} ordered to do {job.def.defName}", $"PreWork.OrderedJob|{pawn.ThingID}|{job.def.defName}");

                // Only process colonists
                if (!pawn.IsColonist || !pawn.Awake())
                {
                    LogDebug($"[SurvivalTools.PreWork] Skipping non-colonist or sleeping pawn: {pawn.LabelShort}", $"PreWork.SkipPawn|{pawn.ThingID}");
                    return true;
                }

                // ANTI-CONFLICT: Skip tool management jobs to avoid loops
                if (JobUtils.IsToolManagementJob(job))
                {
                    LogDebug($"[SurvivalTools.PreWork] Skipping tool management job {job.def.defName} for {pawn.LabelShort}", $"PreWork.SkipToolJob|{pawn.ThingID}|{job.def.defName}");
                    return true;
                }

                // Check if this job requires tools
                var workStat = GetRelevantWorkStat(job);
                if (workStat == null)
                {
                    LogDebug($"[SurvivalTools.PreWork] Job {job.def.defName} doesn't require tools, allowing", $"PreWork.NoToolJob|{pawn.ThingID}|{job.def.defName}");
                    return true; // No relevant work stat, continue normally
                }

                LogDebug($"[SurvivalTools.PreWork] TOOL-REQUIRING ORDERED JOB: {pawn.LabelShort} ordered {job.def.defName} (needs {workStat.defName})", $"PreWork.ToolJob|{pawn.ThingID}|{job.def.defName}|{workStat.defName}");

                var settings = SurvivalTools.Settings;

                // Skip if assignment disabled
                if (!GetEnableAssignments(settings))
                {
                    LogDebug($"[SurvivalTools.PreWork] Assignments disabled in settings", $"PreWork.AssignDisabled|{pawn.ThingID}");
                    return true;
                }

                // SAFETY: Don't interfere if pawn is already doing something critical
                if (pawn.jobs?.curJob != null && !pawn.jobs.curJob.def.casualInterruptible)
                {
                    LogDebug($"[SurvivalTools.PreWork] Pawn {pawn.LabelShort} has non-interruptible job {pawn.jobs.curJob.def.defName}, skipping assignment", $"PreWork.NonInterruptible|{pawn.ThingID}|{pawn.jobs.curJob.def.defName}");
                    return true;
                }

                // SAFETY: Don't interfere with job queue manipulation during job execution
                if (pawn.jobs?.IsCurrentJobPlayerInterruptible() == false)
                {
                    LogDebug($"[SurvivalTools.PreWork] Pawn {pawn.LabelShort} current job is not player-interruptible, skipping assignment", $"PreWork.NotPlayerInterruptible|{pawn.ThingID}");
                    return true;
                }

                LogDebug($"[SurvivalTools.PreWork] Attempting tool upgrade for ordered job...", $"PreWork.AttemptUpgrade|{pawn.ThingID}|{job.def.defName}");

                // Try to upgrade tool for this work
                bool upgradedTool = TryUpgradeForWork(pawn, workStat, job, settings, AssignmentSearch.QueuePriority.Front);

                if (upgradedTool)
                {
                    LogDebug($"[SurvivalTools.PreWork] Tool upgrade was queued for {pawn.LabelShort}, blocking original job {job.def.defName}", $"PreWork.UpgradeQueued|{pawn.ThingID}|{job.def.defName}");
                    // Tool upgrade was queued. Requeue the original job at the front so it will retry after equip.
                    try
                    {
                        if (job != null && JobUtils.IsJobStillValid(job, pawn))
                        {
                            var cloned = JobUtils.CloneJobForQueue(job);
                            pawn.jobs?.jobQueue?.EnqueueFirst(cloned, JobTag.Misc);
                            LogDebug($"[SurvivalTools.PreWork] Re-queued original job {job.def.defName} for {pawn.LabelShort} at front of queue", $"PreWork.Requeued|{pawn.ThingID}|{job.def.defName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[SurvivalTools.PreWork] Exception while re-queuing original job {job?.def?.defName}: {ex}");
                    }

                    // Block now; job will be retried from queue after tool job(s)
                    __result = false;
                    return false;
                }

                LogDebug($"[SurvivalTools.PreWork] No tool upgrade needed for {pawn.LabelShort}, continuing with job {job.def.defName}", $"PreWork.NoUpgrade|{pawn.ThingID}|{job.def.defName}");
                // No upgrade needed/possible, continue with original job
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Exception in PreWork_AutoEquip.TryTakeOrderedJob_Prefix: {ex}");
                return true; // Always continue on error to avoid breaking gameplay
            }
        }

        /// <summary>
        /// Alternative patch for StartJob - another common job creation path
        /// NOTE: StartJob returns void, so we cannot block it or return a result
        /// </summary>
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
        [HarmonyPrefix]
        public static bool StartJob_Prefix(Pawn_JobTracker __instance, Job newJob)
        {
            try
            {
                if (__instance == null || newJob?.def == null)
                    return true;

                var pawn = (Pawn)PawnField.GetValue(__instance);
                if (pawn == null)
                    return true;

                // EARLY FILTER: Only log and process colonists to reduce spam
                if (!pawn.IsColonist || !pawn.Awake())
                    return true;

                // Check if this job requires tools FIRST to reduce logging spam
                var workStat = GetRelevantWorkStat(newJob);
                if (workStat == null)
                    return true; // No tool needed, exit quietly

                // NOW log since this is a tool-requiring job
                LogDebug($"[SurvivalTools.PreWork] TOOL JOB: {pawn.LabelShort} starting {newJob.def.defName} (requires {workStat.defName})", $"StartJob.ToolJob|{pawn.ThingID}|{newJob.def.defName}|{workStat.defName}");

                var settings = SurvivalTools.Settings;
                if (!GetEnableAssignments(settings))
                {
                    LogDebug($"[SurvivalTools.PreWork] Assignments disabled, skipping tool check", $"StartJob.AssignDisabled|{pawn.ThingID}");
                    return true;
                }

                // Try to upgrade tool for this work using FRONT priority (rescue style)
                LogDebug($"[SurvivalTools.PreWork] Attempting tool upgrade for {pawn.LabelShort}...", $"StartJob.AttemptUpgrade|{pawn.ThingID}|{newJob.def.defName}");
                bool upgraded = TryUpgradeForWork(pawn, workStat, newJob, settings, AssignmentSearch.QueuePriority.Front);
                LogDebug($"[SurvivalTools.PreWork] Tool upgrade result: {upgraded}", $"StartJob.UpgradeResult|{pawn.ThingID}|{newJob.def.defName}");

                if (upgraded)
                {
                    // Requeue the original job at the front, then SKIP starting it now.
                    try
                    {
                        if (newJob != null && JobUtils.IsJobStillValid(newJob, pawn))
                        {
                            var cloned = JobUtils.CloneJobForQueue(newJob);
                            pawn.jobs?.jobQueue?.EnqueueFirst(cloned, JobTag.Misc);
                            LogDebug($"[SurvivalTools.PreWork] Re-queued StartJob job {newJob.def.defName} for {pawn.LabelShort} at front of queue", $"StartJob.Requeued|{pawn.ThingID}|{newJob.def.defName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Exception requeuing StartJob job {newJob?.def?.defName}: {ex}");
                    }
                    return false; // Skip StartJob now; equip job(s) will run first
                }

                return true; // Continue when no upgrade queued
            }
            catch (Exception ex)
            {
                LogError($"Exception in PreWork_AutoEquip.StartJob_Prefix: {ex}");
                return true;
            }
        }

        /// <summary>
        /// Patch WorkGiver_Scanner.JobOnThing - this catches work assignments before they become jobs
        /// </summary>
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.JobOnThing))]
        [HarmonyPrefix]
        public static bool WorkGiver_JobOnThing_Prefix(WorkGiver_Scanner __instance, Pawn pawn, Thing t)
        {
            try
            {
                if (__instance == null || pawn == null)
                    return true;

                // EARLY FILTER: Colonists only, avoid noise
                if (!pawn.IsColonist || !pawn.Awake())
                    return true;

                // Determine relevant stat for this work type; store for postfix to act on
                var workTypeDef = __instance.def?.workType;
                if (workTypeDef == null)
                    return true;

                // If the pawn cannot perform this work type OR it's not active for this pawn, and this isn't a forced job, skip rescue entirely
                bool isForced = pawn.CurJob != null && pawn.CurJob.playerForced;
                var ws = pawn.workSettings;
                if (!isForced && (pawn.WorkTypeIsDisabled(workTypeDef) || (ws != null && !ws.WorkIsActive(workTypeDef))))
                {
                    LogDebug($"[SurvivalTools.PreWork] Skipping WG rescue: {pawn.LabelShort} inactive/disabled work type {workTypeDef.defName}", $"PreWork.WG.SkipInactive|{pawn.ThingID}|{workTypeDef.defName}");
                    return true;
                }

                StatDef relevantStat = null;
                if (workTypeDef == WorkTypeDefOf.Mining)
                    relevantStat = ST_StatDefOf.DiggingSpeed;
                else if (workTypeDef == WorkTypeDefOf.PlantCutting)
                    relevantStat = ST_StatDefOf.TreeFellingSpeed;
                else if (workTypeDef == WorkTypeDefOf.Growing)
                    relevantStat = ST_StatDefOf.PlantHarvestingSpeed;
                else if (workTypeDef == WorkTypeDefOf.Construction)
                    relevantStat = StatDefOf.ConstructionSpeed;

                if (relevantStat != null)
                {
                    _wgPendingStat[pawn.thingIDNumber] = relevantStat;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError($"Exception in PreWork_AutoEquip.WorkGiver_JobOnThing_Prefix: {ex}");
                return true;
            }
        }

        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.JobOnThing))]
        [HarmonyPostfix]
        public static void WorkGiver_JobOnThing_Postfix(WorkGiver_Scanner __instance, Pawn pawn, Thing t, ref Job __result)
        {
            try
            {
                if (__instance == null || pawn == null)
                    return;

                // Nothing to do if no job was produced
                if (__result == null)
                {
                    _wgPendingStat.Remove(pawn.thingIDNumber);
                    return;
                }

                // Only for colonists with pending stat from prefix
                if (!pawn.IsColonist || !pawn.Awake())
                {
                    _wgPendingStat.Remove(pawn.thingIDNumber);
                    return;
                }

                if (!_wgPendingStat.TryGetValue(pawn.thingIDNumber, out var relevantStat) || relevantStat == null)
                {
                    return;
                }

                var settings = SurvivalTools.Settings;
                if (!GetEnableAssignments(settings))
                {
                    _wgPendingStat.Remove(pawn.thingIDNumber);
                    return;
                }

                // Avoid loops: don't intervene for tool management jobs
                if (JobUtils.IsToolManagementJob(__result))
                {
                    _wgPendingStat.Remove(pawn.thingIDNumber);
                    return;
                }

                // Determine thresholds (reuse defaults similar to TryUpgradeForWork)
                float minGainPct = settings != null ? settings.assignMinGainPct : 0.1f;
                float searchRadius = settings != null ? settings.assignSearchRadius : 25f;
                int pathCostBudget = settings != null ? settings.assignPathCostBudget : 500;

                // Opportunistically queue an upgrade but DO NOT block WorkGiver jobs
                // Blocking here can cause the selected AI job to disappear. Let it proceed.
                bool upgraded = AssignmentSearch.TryUpgradeFor(pawn, relevantStat, minGainPct, searchRadius, pathCostBudget, AssignmentSearch.QueuePriority.Front, $"WorkGiver.JobOnThing({__instance?.def?.defName})");
                if (upgraded)
                {
                    Log.Warning($"[SurvivalTools.PreWork] WorkGiver: queued tool upgrade for {pawn.LabelShort} (job={__result?.def?.defName}), not blocking");
                }
            }
            catch (Exception ex)
            {
                LogError($"Exception in PreWork_AutoEquip.WorkGiver_JobOnThing_Postfix: {ex}");
            }
            finally
            {
                // Cleanup per-call state
                if (pawn != null) _wgPendingStat.Remove(pawn.thingIDNumber);
            }
        }

        /// <summary>
        /// Determine the primary work stat for a job, if any.
        /// </summary>
        private static StatDef GetRelevantWorkStat(Job job)
        {
            if (job?.def == null)
                return null;

            // Map common job types to their primary work stats
            var jobDef = job.def;

            // SAFETY: Skip complex jobs that shouldn't be interrupted by tool assignment
            if (jobDef == JobDefOf.DoBill ||
                jobDef.defName.Contains("Bill") ||
                jobDef.defName.Contains("Craft") ||
                jobDef.defName.Contains("Cook"))
            {
                LogDebug($"Skipping complex job {jobDef.defName} - not suitable for tool assignment", "PreWork.SkipComplexJob");
                return null; // Don't interfere with crafting/cooking jobs
            }

            // Tree cutting/harvesting
            if (jobDef == JobDefOf.CutPlant ||
                jobDef.defName == "FellTree" ||
                jobDef.defName == "HarvestTree")
            {
                return ST_StatDefOf.TreeFellingSpeed;
            }

            // Plant harvesting
            if (jobDef == JobDefOf.Harvest ||
                jobDef.defName == "HarvestDesignated")
            {
                return ST_StatDefOf.PlantHarvestingSpeed;
            }

            // Mining
            if (jobDef == JobDefOf.Mine)
            {
                return ST_StatDefOf.DiggingSpeed;
            }

            // Construction
            if (jobDef == JobDefOf.FinishFrame ||
                jobDef == JobDefOf.Repair)
            {
                return StatDefOf.ConstructionSpeed;
            }

            // Deconstruction
            if (jobDef == JobDefOf.Deconstruct)
            {
                return StatDefOf.ConstructionSpeed; // Same tools as construction
            }

            // Smoothing (if enabled)
            if (jobDef == JobDefOf.SmoothFloor ||
                jobDef == JobDefOf.SmoothWall)
            {
                return StatDefOf.SmoothingSpeed;
            }

            return null; // No relevant work stat found
        }

        /// <summary>
        /// Try to upgrade pawn's tool for the given work stat.
        /// Returns true if upgrade was queued (original job should be blocked).
        /// </summary>
        private static bool TryUpgradeForWork(Pawn pawn, StatDef workStat, Job originalJob, SurvivalToolsSettings settings, AssignmentSearch.QueuePriority priority)
        {
            LogDebug($"[SurvivalTools.PreWork] TryUpgradeForWork called for {pawn.LabelShort}, stat: {workStat.defName}", $"PreWork.TryUpgradeForWork|{pawn.ThingID}|{workStat.defName}");

            // Get assignment parameters from settings (with defaults)
            float minGainPct = GetMinGainPct(settings);
            float searchRadius = GetSearchRadius(settings);
            int pathCostBudget = GetPathCostBudget(settings);

            LogDebug($"[SurvivalTools.PreWork] Parameters: minGain={minGainPct:P1}, radius={searchRadius}, budget={pathCostBudget}", $"PreWork.Params|{pawn.ThingID}|{workStat.defName}");

            // Check if gating would block this job (simplified check using current tool score)
            bool wouldBeGated = IsLikelyGated(pawn, workStat);

            LogDebug($"[SurvivalTools.PreWork] Gating check: wouldBeGated={wouldBeGated}, rescueOnGate={GetAssignRescueOnGate(settings)}", $"PreWork.GatingCheck|{pawn.ThingID}|{workStat.defName}");

            if (wouldBeGated && GetAssignRescueOnGate(settings))
            {
                // Gating rescue mode: any improvement is acceptable
                minGainPct = 0.001f; // Minimal threshold for any improvement
                LogDebug($"[SurvivalTools.PreWork] GATING RESCUE MODE: lowering threshold to {minGainPct:P1}", $"PreWork.RescueMode|{pawn.ThingID}|{workStat.defName}");
            }
            else if (!wouldBeGated)
            {
                // Normal assignment mode: require meaningful gain
                // Use configured minimum gain percentage
                LogDebug($"[SurvivalTools.PreWork] Normal mode: using threshold {minGainPct:P1}", $"PreWork.NormalMode|{pawn.ThingID}|{workStat.defName}");
            }
            else
            {
                // Gating would block but rescue is disabled
                LogDebug($"[SurvivalTools.PreWork] Gating would block but rescue disabled, skipping", $"PreWork.GateNoRescue|{pawn.ThingID}|{workStat.defName}");
                return false;
            }

            // Delegate to AssignmentSearch
            LogDebug($"[SurvivalTools.PreWork] Calling AssignmentSearch.TryUpgradeFor...", $"PreWork.CallAssign|{pawn.ThingID}|{workStat.defName}");
            string caller = originalJob != null ? $"PreWork.TryTakeOrderedJob({originalJob.def?.defName})" : "PreWork.StartJob";
            bool result = AssignmentSearch.TryUpgradeFor(pawn, workStat, minGainPct, searchRadius, pathCostBudget, priority, caller);
            LogDebug($"[SurvivalTools.PreWork] AssignmentSearch result: {result}", $"PreWork.AssignResult|{pawn.ThingID}|{workStat.defName}");
            return result;
        }

        /// <summary>
        /// Get minimum gain percentage from settings with difficulty scaling.
        /// </summary>
        private static float GetMinGainPct(SurvivalToolsSettings settings)
        {
            if (settings == null)
                return 0.1f; // Default 10%

            // Use configured value with difficulty scaling
            float baseGainPct = settings.assignMinGainPct;

            // Scale by difficulty
            if (settings.extraHardcoreMode)
                return baseGainPct * 1.5f; // Nightmare: higher threshold
            if (settings.hardcoreMode)
                return baseGainPct * 1.25f; // Hardcore: moderate increase

            return baseGainPct; // Normal: as configured
        }

        /// <summary>
        /// Get search radius from settings with difficulty scaling.
        /// </summary>
        private static float GetSearchRadius(SurvivalToolsSettings settings)
        {
            if (settings == null)
                return 25f; // Default radius

            float baseRadius = settings.assignSearchRadius;

            // Scale by difficulty  
            if (settings.extraHardcoreMode)
                return baseRadius * 0.5f; // Nightmare: half radius
            if (settings.hardcoreMode)
                return baseRadius * 0.75f; // Hardcore: reduced radius

            return baseRadius; // Normal: full radius
        }

        /// <summary>
        /// Get path cost budget from settings with difficulty scaling.
        /// </summary>
        private static int GetPathCostBudget(SurvivalToolsSettings settings)
        {
            if (settings == null)
                return 500; // Default budget

            int baseBudget = settings.assignPathCostBudget;

            // Scale by difficulty
            if (settings.extraHardcoreMode)
                return baseBudget / 2; // Nightmare: half budget
            if (settings.hardcoreMode)
                return (baseBudget * 3) / 4; // Hardcore: 75% budget

            return baseBudget; // Normal: full budget
        }

        /// <summary>
        /// Get rescue on gate setting.
        /// </summary>
        private static bool GetAssignRescueOnGate(SurvivalToolsSettings settings)
        {
            if (settings == null)
                return true; // Default enabled

            return settings.assignRescueOnGate;
        }

        /// <summary>
        /// Get assignment enabled setting.
        /// </summary>
        private static bool GetEnableAssignments(SurvivalToolsSettings settings)
        {
            if (settings == null)
                return true; // Default enabled

            return settings.enableAssignments;
        }

        /// <summary>
        /// Check if a pawn would likely be gated for this work stat.
        /// Simplified check based on current tool availability.
        /// </summary>
        private static bool IsLikelyGated(Pawn pawn, StatDef workStat)
        {
            if (pawn == null || workStat == null)
                return false;

            // Get current best tool and score
            var currentTool = ToolScoring.GetBestTool(pawn, workStat, out float currentScore);
            float baseline = SurvivalToolUtility.GetNoToolBaseline(workStat);

            // If current score is at or below baseline, likely gated
            return currentScore <= baseline + 0.001f;
        }
    }
}