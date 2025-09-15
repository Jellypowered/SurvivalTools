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

            // Consider both real SurvivalTool things and tool-stuff on the ground (wrapped as VirtualSurvivalTool)
            var seen = new HashSet<int>(); // avoid scoring same backing thing twice
            foreach (var thing in GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, SearchRadius, true))
            {
                if (thing == null) continue;

                SurvivalTool candidate = null;

                // Real SurvivalTool instance on the ground
                if (thing is SurvivalTool st)
                {
                    candidate = st;
                }
                else if (thing.def != null && thing.def.IsToolStuff())
                {
                    // Wrap tool-stuff into a virtual survival tool for scoring
                    candidate = VirtualSurvivalTool.FromThing(thing);
                }

                if (candidate == null) continue;
                var backing = SurvivalToolUtility.BackingThing(candidate, pawn) ?? (candidate as Thing);
                if (backing == null) continue;
                if (seen.Contains(backing.thingIDNumber)) continue;
                seen.Add(backing.thingIDNumber);

                if (!IsViableCandidate(candidate, pawn, requiredStats)) continue;

                float score = ToolScoring.CalculateToolScore(candidate, pawn, requiredStats);

                if (backing != null) score -= 0.01f * backing.Position.DistanceTo(pawn.Position);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTool = candidate;
                }
            }
            // Next: consider items in storage/stockpiles and haulable things on the map within a reasonable fallback radius.
            // Prioritize storage items first (they're often closer-to-hand) then stockpiles/haulables.
            const int StorageSearchRadius = 80; // tiles to search for storage items
            const int FallbackSearchRadius = 200; // wider map fallback for haulable items

            try
            {
                var map = pawn.Map;
                if (map != null)
                {
                    // Scan haulable ever group but order: storage first, then nearby stockpiles, then fallback area
                    var candidates = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);

                    // 1) Storage items (IsInAnyStorage) within StorageSearchRadius
                    foreach (var item in candidates)
                    {
                        if (item == null) continue;
                        if (!item.IsInAnyStorage()) continue;
                        if (item.Map != map) continue;
                        if (item.Position.DistanceTo(pawn.Position) > StorageSearchRadius) continue;

                        if (seen.Contains(item.thingIDNumber)) continue;
                        seen.Add(item.thingIDNumber);

                        SurvivalTool candidate2 = null;
                        if (item is SurvivalTool st2) candidate2 = st2;
                        else if (item.def != null && item.def.IsToolStuff()) candidate2 = VirtualSurvivalTool.FromThing(item);
                        if (candidate2 == null) continue;
                        if (!IsViableCandidate(candidate2, pawn, requiredStats)) continue;

                        float score = ToolScoring.CalculateToolScore(candidate2, pawn, requiredStats);
                        var backing2 = SurvivalToolUtility.BackingThing(candidate2, pawn) ?? (candidate2 as Thing);
                        if (backing2 != null) score -= 0.01f * backing2.Position.DistanceTo(pawn.Position);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestTool = candidate2;
                        }
                    }

                    // 2) Nearby haulable (stockpiles, scattered) within StorageSearchRadius
                    foreach (var item in candidates)
                    {
                        if (item == null) continue;
                        if (item.IsInAnyStorage()) continue; // already considered
                        if (item.Map != map) continue;
                        if (item.Position.DistanceTo(pawn.Position) > StorageSearchRadius) continue;

                        if (seen.Contains(item.thingIDNumber)) continue;
                        seen.Add(item.thingIDNumber);

                        SurvivalTool candidate2 = null;
                        if (item is SurvivalTool st2) candidate2 = st2;
                        else if (item.def != null && item.def.IsToolStuff()) candidate2 = VirtualSurvivalTool.FromThing(item);
                        if (candidate2 == null) continue;
                        if (!IsViableCandidate(candidate2, pawn, requiredStats)) continue;

                        float score = ToolScoring.CalculateToolScore(candidate2, pawn, requiredStats);
                        var backing2 = SurvivalToolUtility.BackingThing(candidate2, pawn) ?? (candidate2 as Thing);
                        if (backing2 != null) score -= 0.01f * backing2.Position.DistanceTo(pawn.Position);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestTool = candidate2;
                        }
                    }

                    // 3) Fallback wider-area scan for haulable items (but limit to avoid perf issues)
                    foreach (var item in candidates)
                    {
                        if (item == null) continue;
                        if (item.Map != map) continue;
                        if (item.Position.DistanceTo(pawn.Position) > FallbackSearchRadius) continue;

                        if (seen.Contains(item.thingIDNumber)) continue;
                        seen.Add(item.thingIDNumber);

                        SurvivalTool candidate2 = null;
                        if (item is SurvivalTool st2) candidate2 = st2;
                        else if (item.def != null && item.def.IsToolStuff()) candidate2 = VirtualSurvivalTool.FromThing(item);
                        if (candidate2 == null) continue;
                        if (!IsViableCandidate(candidate2, pawn, requiredStats)) continue;

                        float score = ToolScoring.CalculateToolScore(candidate2, pawn, requiredStats);
                        var backing2 = SurvivalToolUtility.BackingThing(candidate2, pawn) ?? (candidate2 as Thing);
                        if (backing2 != null) score -= 0.01f * backing2.Position.DistanceTo(pawn.Position);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestTool = candidate2;
                        }
                    }
                }
            }
            catch { /* best-effort: do not break auto-tool if map scanning fails */ }

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
