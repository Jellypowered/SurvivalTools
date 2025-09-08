// RimWorld 1.6 / C# 7.3
// Source/Helpers/JobUtils.cs
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Helpers
{
    /// <summary>
    /// Centralized helper methods for Job management:
    /// - cloning (shallow vs deep)
    /// - type checks (inventory/tool jobs)
    /// - validation & logging utilities
    /// </summary>
    public static class JobUtils
    {
        /// <summary>
        /// Create a shallow clone of a Job for immediate execution.
        /// Preserves references (queues, lists, etc.) from the original.
        /// ⚠️ Only use this if the clone is short-lived and won’t be mutated
        /// in parallel with the original.
        /// </summary>
        public static Job ShallowClone(Job originalJob)
        {
            if (originalJob == null) return null;

            var clone = new Job(originalJob.def, originalJob.targetA, originalJob.targetB, originalJob.targetC)
            {
                count = originalJob.count,
                haulOpportunisticDuplicates = originalJob.haulOpportunisticDuplicates,
                haulMode = originalJob.haulMode,
                bill = originalJob.bill,
                commTarget = originalJob.commTarget,
                targetQueueA = originalJob.targetQueueA,
                targetQueueB = originalJob.targetQueueB,
                countQueue = originalJob.countQueue,
                placedThings = originalJob.placedThings,
                maxNumMeleeAttacks = originalJob.maxNumMeleeAttacks,
                maxNumStaticAttacks = originalJob.maxNumStaticAttacks,
                exitMapOnArrival = originalJob.exitMapOnArrival,
                failIfCantJoinOrCreateCaravan = originalJob.failIfCantJoinOrCreateCaravan,
                killIncappedTarget = originalJob.killIncappedTarget,
                ignoreForbidden = originalJob.ignoreForbidden,
                ignoreDesignations = originalJob.ignoreDesignations,
                canBashDoors = originalJob.canBashDoors,
                canUseRangedWeapon = originalJob.canUseRangedWeapon,
                haulDroppedApparel = originalJob.haulDroppedApparel,
                restUntilHealed = originalJob.restUntilHealed,
                ignoreJoyTimeAssignment = originalJob.ignoreJoyTimeAssignment,
                overeat = originalJob.overeat,
                attackDoorIfTargetLost = originalJob.attackDoorIfTargetLost,
                takeExtraIngestibles = originalJob.takeExtraIngestibles,
                expireRequiresEnemiesNearby = originalJob.expireRequiresEnemiesNearby,
                locomotionUrgency = originalJob.locomotionUrgency,
                def = originalJob.def // redundant but explicit
            };

            return clone;
        }

        /// <summary>
        /// Create a deep clone of a Job for queue storage.
        /// Clones collections (queues/lists) so the copy is safe to enqueue
        /// without being affected by mutations to the original.
        /// </summary>
        public static Job CloneJobForQueue(Job originalJob)
        {
            if (originalJob == null) return null;

            var clone = ShallowClone(originalJob);

            // Deep copy mutable collections
            if (originalJob.targetQueueA?.Count > 0)
                clone.targetQueueA = new List<LocalTargetInfo>(originalJob.targetQueueA);

            if (originalJob.targetQueueB?.Count > 0)
                clone.targetQueueB = new List<LocalTargetInfo>(originalJob.targetQueueB);

            if (originalJob.countQueue?.Count > 0)
                clone.countQueue = new List<int>(originalJob.countQueue);

            if (originalJob.placedThings?.Count > 0)
                clone.placedThings = new List<ThingCountClass>(originalJob.placedThings);

            return clone;
        }

        /// <summary>
        /// True if job is related to inventory hauling and shouldn’t be re-queued.
        /// </summary>
        public static bool IsInventoryJob(Job job)
        {
            return job?.def == JobDefOf.TakeInventory ||
                   job?.def == JobDefOf.UnloadInventory ||
                   job?.def == JobDefOf.UnloadYourInventory;
        }

        /// <summary>
        /// True if job is survival tool management (drop/equip).
        /// Used to avoid conflicts with auto-tool logic.
        /// </summary>
        public static bool IsToolManagementJob(Job job)
        {
            return job?.def == ST_JobDefOf.DropSurvivalTool ||
                   job?.def == JobDefOf.TakeInventory ||
                   job?.def == JobDefOf.Equip;
        }

        /// <summary>
        /// Build a compact string description for debug logging.
        /// </summary>
        public static string GetJobDescription(Job job)
        {
            if (job?.def == null) return "null job";

            var desc = job.def.defName;
            if (job.targetA.IsValid)
                desc += $" on {job.targetA}";

            return desc;
        }

        /// <summary>
        /// Returns true if this job requires any survival-tool-relevant stats.
        /// Caller gets the resolved stat list.
        /// </summary>
        public static bool RequiresSurvivalToolStats(Job job, out List<StatDef> requiredStats)
        {
            requiredStats = null;
            if (job?.def == null) return false;

            try
            {
                requiredStats = SurvivalToolUtility.StatsForJob(job);
                return requiredStats?.Count > 0;
            }
            catch
            {
                // Defensive: don’t propagate exceptions from stat resolution
                requiredStats = new List<StatDef>();
                return false;
            }
        }

        /// <summary>
        /// Validate that a job is still executable:
        /// - targets exist, are not destroyed, and are on the same map
        /// - job def is still defined (protects against mod unloads)
        /// </summary>
        public static bool IsJobStillValid(Job job, Pawn pawn)
        {
            if (job?.def == null || pawn?.Map == null) return false;

            // Check primary target
            if (job.targetA.IsValid)
            {
                if (job.targetA.HasThing &&
                    (job.targetA.Thing?.Destroyed != false || job.targetA.Thing.Map != pawn.Map))
                    return false;

                if (job.targetA.Cell != IntVec3.Invalid &&
                    !job.targetA.Cell.InBounds(pawn.Map))
                    return false;
            }

            // Ensure the JobDef still exists (def removal compatibility guard)
            return DefDatabase<JobDef>.AllDefs.Any(def => def == job.def);
        }
    }
}
