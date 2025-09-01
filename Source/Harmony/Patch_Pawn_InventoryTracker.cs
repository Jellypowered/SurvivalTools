using System.Collections.Generic;
using System.Linq;
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
        public static class FirstUnloadableThing_Postfix
        {
            public static void Postfix(Pawn_InventoryTracker __instance, ref ThingCount __result)
            {
                if (!(__result.Thing is SurvivalTool toolInResult) || !toolInResult.InUse)
                {
                    return;
                }

                // The current unloadable thing is an in-use tool. Find an alternative.
                var alternative = __instance.innerContainer
                    .FirstOrDefault(t => !(t is SurvivalTool st) || !st.InUse);

                __result = (alternative != null)
                    ? new ThingCount(alternative, alternative.stackCount)
                    : default(ThingCount);
            }
        }

        [HarmonyPatch(typeof(Pawn_InventoryTracker))]
        public static class InventoryTracker_Tick_Patch
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                // Patch common tick methods for version compatibility.
                var tickMethod = AccessTools.Method(typeof(Pawn_InventoryTracker), "InventoryTrackerTick");
                var tickRareMethod = AccessTools.Method(typeof(Pawn_InventoryTracker), "InventoryTrackerTickRare");

                // Only return methods that actually exist
                if (tickMethod != null) yield return tickMethod;
                if (tickRareMethod != null) yield return tickRareMethod;

                // If neither method exists, log a warning in debug mode
                if (tickMethod == null && tickRareMethod == null && SurvivalToolUtility.IsDebugLoggingEnabled)
                {
                    Log.Warning("[SurvivalTools] Could not find any inventory tracker tick methods to patch");
                }
            }

            public static void Postfix(Pawn_InventoryTracker __instance)
            {
                var pawn = __instance.pawn;
                if (pawn?.Map == null || !pawn.IsHashIntervalTick(60) || pawn.jobs.curJob != null)
                    return;

                if (SurvivalTools.Settings?.toolLimit != true || !pawn.CanUseSurvivalTools() || !pawn.CanRemoveExcessSurvivalTools())
                    return;

                int heldCount = pawn.HeldSurvivalToolCount();
                float carryCap = pawn.GetStatValue(ST_StatDefOf.SurvivalToolCarryCapacity);

                if (heldCount <= carryCap)
                    return;

                // Find a tool to drop. Prioritize tools that are not "in use" by the optimizer.
                var toolToDrop = pawn.GetHeldSurvivalTools()
                    .OfType<SurvivalTool>()
                    .FirstOrDefault(t => !t.InUse);

                if (toolToDrop == null)
                {
                    if (SurvivalToolUtility.IsDebugLoggingEnabled)
                        Log.Message($"[SurvivalTools.InventoryTick] {pawn.LabelShort} is over tool limit, but all tools are marked 'in-use'. Skipping drop.");
                    return;
                }

                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    Log.Message($"[SurvivalTools.InventoryTick] {pawn.LabelShort} is idle and over tool limit. Creating job to drop {toolToDrop.LabelShort}.");

                var dropJob = pawn.DequipAndTryStoreSurvivalTool(toolToDrop, false);
                if (dropJob != null)
                {
                    pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);
                }
            }
        }

        [HarmonyPatch(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.Notify_ItemRemoved))]
        public static class Notify_ItemRemoved_Postfix
        {
            public static void Postfix(Pawn_InventoryTracker __instance, Thing item)
            {
                if (item is SurvivalTool)
                {
                    __instance.pawn?.GetComp<Pawn_SurvivalToolAssignmentTracker>()?.forcedHandler?.SetForced(item, false);
                }
            }
        }
    }
}
