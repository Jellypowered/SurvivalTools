// RimWorld 1.6 / C# 7.3
// Source/AI/AutoToolPickup_UtilityIntegrated.cs

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;
using SurvivalTools.Helpers;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    public static class Patch_JobGiver_Work_TryIssueJobPackage_AutoTool
    {
        private const int SearchRadius = 28;

        #region Entry

        public static void Postfix(Pawn pawn, JobIssueParams jobParams, ref ThinkResult __result)
        {
            try
            {
                if (pawn == null || __result.Job == null) return;

                if (!ShouldAttemptAutoTool(__result.Job, pawn, out var requiredStats))
                    return;

                bool isDebug = IsDebugLoggingEnabled;
                var jobName = __result.Job.def?.defName ?? "unknown";

                // Already has a suitable tool?
                if (PawnHasHelpfulTool(pawn, requiredStats))
                    return;

                // Hardcore gating: if we can't acquire something, block the job
                if (SurvivalTools.Settings?.hardcoreMode == true)
                {
                    foreach (var stat in requiredStats ?? Enumerable.Empty<StatDef>())
                    {
                        if (StatGatingHelper.ShouldBlockJobForStat(stat, SurvivalTools.Settings, pawn) &&
                            !CanAcquireHelpfulToolNow(pawn, requiredStats, isDebug))
                        {
                            __result = ThinkResult.NoJob;
                            return;
                        }
                    }
                }

                // Find & enqueue pickup
                var bestTool = FindBestHelpfulTool(pawn, requiredStats, isDebug);
                if (bestTool != null)
                    __result = CreateToolPickupJobs(pawn, __result.Job, bestTool, requiredStats, __result.SourceNode);
            }
            catch (Exception ex)
            {
                if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"AutoTool_Exception_{pawn?.ThingID ?? "null"}"))
                    Log.Error($"[SurvivalTools.AutoTool] Exception in AutoTool postfix: {ex}");
            }
        }

        #endregion

        #region Scan preconditions

        private static bool ShouldAttemptAutoTool(Job job, Pawn pawn, out List<StatDef> requiredStats)
        {
            requiredStats = null;

            if (SurvivalTools.Settings?.autoTool != true ||
                job == null || pawn == null || pawn.Map == null ||
                !pawn.CanUseSurvivalTools() || pawn.Drafted || pawn.InMentalState ||
                job.def == JobDefOf.TakeInventory)
                return false;

            // Special-case tree felling
            if (job.def == ST_JobDefOf.FellTree ||
                job.def == ST_JobDefOf.FellTreeDesignated ||
                (job.def == JobDefOf.CutPlant && job.targetA.Thing?.def?.plant?.IsTree == true))
            {
                requiredStats = new List<StatDef> { ST_StatDefOf.TreeFellingSpeed };
                return true;
            }

            requiredStats = SurvivalToolUtility.RelevantStatsFor(job.workGiverDef, job) ?? new List<StatDef>();
            return !requiredStats.NullOrEmpty();
        }

        private static bool PawnHasHelpfulTool(Pawn pawn, List<StatDef> requiredStats) =>
            pawn?.GetAllUsableSurvivalTools()
                .OfType<SurvivalTool>()
                .Any(st => ToolScoring.ToolImprovesAnyRequiredStat(st, requiredStats)) == true;

        #endregion

        #region Acquire check

        private static bool CanAcquireHelpfulToolNow(Pawn pawn, List<StatDef> requiredStats, bool isDebug)
        {
            var best = FindBestHelpfulTool(pawn, requiredStats, isDebug);
            if (best == null) return false;

            var backing = SurvivalToolUtility.BackingThing(best, pawn) ?? (best as Thing);
            if (backing == null || !backing.Spawned) return false;

            return pawn.CanReserveAndReach(backing, PathEndMode.OnCell, pawn.NormalMaxDanger());
        }

        #endregion

        #region Pickup job creation

        private static ThinkResult CreateToolPickupJobs(
            Pawn pawn,
            Job originalJob,
            SurvivalTool toolToGet,
            List<StatDef> requiredStats,
            ThinkNode sourceNode)
        {
            if (pawn == null || originalJob == null || toolToGet == null)
                return new ThinkResult(originalJob, sourceNode, JobTag.Misc, false);

            var spawnTarget = SurvivalToolUtility.BackingThing(toolToGet, pawn) ?? (Thing)toolToGet;
            if (spawnTarget == null || !spawnTarget.Spawned || spawnTarget.Map != pawn.Map ||
                spawnTarget.IsForbidden(pawn) ||
                !pawn.CanReserveAndReach(spawnTarget, PathEndMode.OnCell, pawn.NormalMaxDanger()))
            {
                return new ThinkResult(originalJob, sourceNode, JobTag.Misc, false);
            }

            var pickupJob = JobMaker.MakeJob(JobDefOf.TakeInventory, spawnTarget);
            pickupJob.count = 1;

            // Simple: queue pickup, then resume original job
            if (originalJob.def != JobDefOf.TakeInventory)
                pawn.jobs.jobQueue.EnqueueFirst(CloneJobForQueue(originalJob));

            return new ThinkResult(pickupJob, sourceNode, JobTag.Misc, false);
        }

        private static Job CloneJobForQueue(Job j)
        {
            if (j == null) return null;

            // Make a new job with the same def/targets
            var copy = JobMaker.MakeJob(j.def, j.targetA, j.targetB, j.targetC);

            // Copy fields manually (Job fields are public, not properties in RW 1.6)
            copy.count = j.count;
            copy.playerForced = j.playerForced;
            copy.expiryInterval = j.expiryInterval;
            copy.checkOverrideOnExpire = j.checkOverrideOnExpire;
            copy.locomotionUrgency = j.locomotionUrgency;
            copy.overeat = j.overeat;
            copy.killIncappedTarget = j.killIncappedTarget;
            copy.ignoreForbidden = j.ignoreForbidden;
            copy.ignoreDesignations = j.ignoreDesignations;
            copy.ignoreJoyTimeAssignment = j.ignoreJoyTimeAssignment;

            // Copy queues safely
            if (j.targetQueueA != null)
                copy.targetQueueA = new List<LocalTargetInfo>(j.targetQueueA);
            if (j.targetQueueB != null)
                copy.targetQueueB = new List<LocalTargetInfo>(j.targetQueueB);
            if (j.countQueue != null)
                copy.countQueue = new List<int>(j.countQueue);

            return copy;
        }


        #endregion

        #region Scanner & scoring (kept from before)

        private static SurvivalTool FindBestHelpfulTool(Pawn pawn, List<StatDef> requiredStats, bool isDebug)
        {
            requiredStats = requiredStats ?? new List<StatDef>();
            if (pawn == null || pawn.Map == null || !pawn.Position.IsValid || !pawn.Position.InBounds(pawn.Map))
                return null;

            SurvivalTool bestTool = null;
            float bestScore = 0f;

            foreach (var tool in GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, SearchRadius, true).OfType<SurvivalTool>())
            {
                if (!IsViableCandidate(tool, pawn, requiredStats)) continue;

                float score = ToolScoring.CalculateToolScore(tool, pawn, requiredStats);

                var backing = SurvivalToolUtility.BackingThing(tool, pawn) ?? (tool as Thing);
                if (backing != null) score -= 0.01f * backing.Position.DistanceTo(pawn.Position);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTool = tool;
                }
            }
            return bestTool;
        }

        private static bool IsViableCandidate(SurvivalTool tool, Pawn pawn, List<StatDef> requiredStats)
        {
            if (tool == null || pawn == null || pawn.Map == null) return false;
            var backing = SurvivalToolUtility.BackingThing(tool, pawn);
            if (backing == null || !backing.Spawned || backing.Map != pawn.Map) return false;
            if (backing.IsForbidden(pawn)) return false;
            if (!pawn.CanReserveAndReach(backing, PathEndMode.OnCell, pawn.NormalMaxDanger())) return false;
            return ToolScoring.ToolImprovesAnyRequiredStat(tool, requiredStats);
        }

        #endregion
    }
}
