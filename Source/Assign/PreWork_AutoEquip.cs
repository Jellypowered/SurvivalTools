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

namespace SurvivalTools.Assign
{
    [HarmonyPatch]
    public static class PreWork_AutoEquip
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_JobTracker), "pawn");

        /// <summary>
        /// Harmony prefix for Pawn_JobTracker.TryTakeOrderedJob.
        /// Checks if job requires tools and attempts auto-equip if beneficial.
        /// </summary>
        [HarmonyPriority(Priority.First)]
        //[HarmonyAfter(new[] { "Jelly.SurvivalToolsReborn" })] // if you used an ID for legacy patches
        [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
        [HarmonyPrefix]
        public static bool TryTakeOrderedJob_Prefix(Pawn_JobTracker __instance, Job job, ref bool __result)
        {
            try
            {
                // Early-out checks
                if (__instance == null || job?.def == null)
                    return true; // Continue with original

                var pawn = (Pawn)PawnField.GetValue(__instance);
                if (pawn == null)
                    return true;

                Log.Message($"[SurvivalTools.PreWork] TryTakeOrderedJob_Prefix called for {pawn.LabelShort} with job {job.def.defName}");

                // SAFETY: Don't interfere if pawn is already doing something critical
                if (pawn.jobs?.curJob != null && !pawn.jobs.curJob.def.casualInterruptible)
                {
                    Log.Message($"[SurvivalTools.PreWork] Pawn {pawn.LabelShort} has non-interruptible job {pawn.jobs.curJob.def.defName}, skipping assignment");
                    return true;
                }

                // SAFETY: Don't interfere with job queue manipulation during job execution
                if (pawn.jobs?.IsCurrentJobPlayerInterruptible() == false)
                {
                    Log.Message($"[SurvivalTools.PreWork] Pawn {pawn.LabelShort} current job is not player-interruptible, skipping assignment");
                    return true;
                }

                var settings = SurvivalTools.Settings;

                // Skip if assignment disabled
                if (!GetEnableAssignments(settings))
                {
                    Log.Message($"[SurvivalTools.PreWork] Assignments disabled in settings");
                    return true;
                }

                // Skip if pawn is not a colonist or not awake
                if (!pawn.IsColonist || !pawn.Awake())
                {
                    Log.Message($"[SurvivalTools.PreWork] Skipping {pawn.LabelShort}: IsColonist={pawn.IsColonist}, Awake={pawn.Awake()}");
                    return true;
                }

                // Check if this job type should trigger assignment
                var workStat = GetRelevantWorkStat(job);
                if (workStat == null)
                {
                    // Don't log this as it would be very spammy for non-tool jobs
                    return true; // No relevant work stat, continue normally
                }

                Log.Message($"[SurvivalTools.PreWork] Job {job.def.defName} maps to work stat {workStat.defName} for {pawn.LabelShort}");

                // Try to upgrade tool for this work
                bool upgradedTool = TryUpgradeForWork(pawn, workStat, job, settings);

                if (upgradedTool)
                {
                    Log.Message($"[SurvivalTools.PreWork] Tool upgrade was queued for {pawn.LabelShort}, blocking original job {job.def.defName}");
                    // Tool upgrade was queued, block original job to retry after equip
                    __result = false;
                    return false;
                }

                Log.Message($"[SurvivalTools.PreWork] No tool upgrade needed for {pawn.LabelShort}, continuing with job {job.def.defName}");
                // No upgrade needed/possible, continue with original job
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools] Exception in PreWork_AutoEquip.TryTakeOrderedJob_Prefix: {ex}");
                return true; // Always continue on error to avoid breaking gameplay
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
                Log.Message($"[SurvivalTools.PreWork] Skipping complex job {jobDef.defName} - not suitable for tool assignment");
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
        private static bool TryUpgradeForWork(Pawn pawn, StatDef workStat, Job originalJob, SurvivalToolsSettings settings)
        {
            // Get assignment parameters from settings (with defaults)
            float minGainPct = GetMinGainPct(settings);
            float searchRadius = GetSearchRadius(settings);
            int pathCostBudget = GetPathCostBudget(settings);

            Log.Message($"[SurvivalTools.PreWork] TryUpgradeForWork for {pawn.LabelShort}: minGainPct={minGainPct:P1}, searchRadius={searchRadius}, pathCostBudget={pathCostBudget}");

            // Check if gating would block this job (simplified check using current tool score)
            bool wouldBeGated = IsLikelyGated(pawn, workStat);

            Log.Message($"[SurvivalTools.PreWork] Gating check for {pawn.LabelShort} / {workStat.defName}: wouldBeGated={wouldBeGated}, rescueOnGate={GetAssignRescueOnGate(settings)}");

            if (wouldBeGated && GetAssignRescueOnGate(settings))
            {
                // Gating rescue mode: any improvement is acceptable
                minGainPct = 0.001f; // Minimal threshold for any improvement
                Log.Message($"[SurvivalTools.PreWork] Gating rescue mode activated for {pawn.LabelShort}, lowering threshold to {minGainPct:P1}");
            }
            else if (!wouldBeGated)
            {
                // Normal assignment mode: require meaningful gain
                // Use configured minimum gain percentage
                Log.Message($"[SurvivalTools.PreWork] Normal assignment mode for {pawn.LabelShort}, using threshold {minGainPct:P1}");
            }
            else
            {
                // Gating would block but rescue is disabled
                Log.Message($"[SurvivalTools.PreWork] Gating would block {pawn.LabelShort} but rescue is disabled, skipping assignment");
                return false;
            }

            // Delegate to AssignmentSearch
            bool result = AssignmentSearch.TryUpgradeFor(pawn, workStat, minGainPct, searchRadius, pathCostBudget);
            Log.Message($"[SurvivalTools.PreWork] AssignmentSearch.TryUpgradeFor result for {pawn.LabelShort}: {result}");
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