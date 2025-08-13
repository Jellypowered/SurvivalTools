using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools.HarmonyStuff
{
    public static class Patch_Pawn_InventoryTracker
    {
        [HarmonyPatch(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.FirstUnloadableThing), MethodType.Getter)]
        public static class FirstUnloadableThing
        {
            public static void Postfix(Pawn_InventoryTracker __instance, ref ThingCount __result)
            {
                var toolInResult = __result.Thing as SurvivalTool;
                if (toolInResult != null && toolInResult.InUse)
                {
                    var container = __instance != null ? __instance.innerContainer : null;
                    if (container == null || container.Count == 0)
                    {
                        __result = default(ThingCount);
                        return;
                    }

                    // Pick the first item that is NOT an in-use SurvivalTool
                    for (int i = 0; i < container.Count; i++)
                    {
                        var candidate = container[i];
                        var candidateTool = candidate as SurvivalTool;
                        if (candidateTool == null || !candidateTool.InUse)
                        {
                            __result = new ThingCount(candidate, candidate.stackCount);
                            return;
                        }
                    }

                    // No valid alternative
                    __result = default(ThingCount);
                }
            }
        }

        // -------- Version-agnostic tick patch --------
        [HarmonyPatch(typeof(Pawn_InventoryTracker))]
        public static class InventoryTracker_Tick_Patch
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                var t = typeof(Pawn_InventoryTracker);
                string[] candidates =
                {
                    "InventoryTrackerTick",
                    "InventoryTrackerTickRare",
                    "TickRare" // fallback just in case
                };

                for (int i = 0; i < candidates.Length; i++)
                {
                    var m = AccessTools.Method(t, candidates[i]);
                    if (m != null)
                        yield return m;
                }
            }

            public static void Postfix(Pawn_InventoryTracker __instance)
            {
                // Settings gate
                if (SurvivalTools.Settings == null || !SurvivalTools.Settings.toolLimit)
                    return;

                var pawn = __instance != null ? __instance.pawn : null;
                if (pawn == null || !pawn.Spawned || pawn.Destroyed || pawn.Dead || pawn.jobs == null)
                    return;

                if (!pawn.CanUseSurvivalTools())
                    return;

                // --- Don’t stack/loop jobs: only act when pawn is idle (no current job). ---
                if (pawn.jobs.curJob != null)
                    return;

                // Count tools and pick a NON-in-use candidate if possible.
                int heldCount = 0;
                Thing lastTool = null;
                Thing candidateToDrop = null;

                foreach (var t in pawn.GetHeldSurvivalTools())
                {
                    heldCount++;
                    lastTool = t;

                    var st = t as SurvivalTool;
                    if (st == null || !st.InUse)
                    {
                        // keep the last non-in-use tool we see; avoids fighting with the one being used
                        candidateToDrop = t;
                    }
                }

                if (heldCount == 0)
                    return;

                float carryCap = pawn.GetStatValue(ST_StatDefOf.SurvivalToolCarryCapacity, applyPostProcess: true);
                if (heldCount <= carryCap || !pawn.CanRemoveExcessSurvivalTools())
                    return;

                // If all tools are currently in use, don’t force a drop this tick.
                if (candidateToDrop == null)
                    return;

                // Build the drop job for the chosen tool.
                var dropJob = pawn.DequipAndTryStoreSurvivalTool(candidateToDrop);
                if (dropJob == null)
                    return;

                // Extra safety: if for some reason curJob just became our drop job, skip.
                if (pawn.jobs.curJob != null && pawn.jobs.curJob.def == dropJob.def)
                    return;

                try
                {
                    // Start now only because we’re idle; avoids the IsCurrentJobPlayerInterruptible()
                    // path entirely and prevents StartJob from queue-spamming via finalizer/opportunistic jobs.
                    pawn.jobs.StartJob(
                        dropJob,
                        JobCondition.InterruptForced,
                        jobGiver: null,
                        resumeCurJobAfterwards: false,
                        cancelBusyStances: false,
                        thinkTree: null,
                        tag: JobTag.Misc,
                        fromQueue: false,
                        canReturnCurJobToPool: false,
                        keepCarryingThingOverride: null,
                        continueSleeping: false,
                        addToJobsThisTick: true,
                        preToilReservationsCanFail: false
                    );
                }
                catch
                {
                    // transient state; skip this tick
                }
            }

        }

        [HarmonyPatch(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.Notify_ItemRemoved))]
        public static class Notify_ItemRemoved
        {
            public static void Postfix(Pawn_InventoryTracker __instance, Thing item)
            {
                if (item is SurvivalTool && __instance != null && __instance.pawn != null)
                {
                    var tracker = __instance.pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
                    if (tracker != null && tracker.forcedHandler != null)
                    {
                        tracker.forcedHandler.SetForced(item, false);
                    }
                }
            }
        }
    }
}
