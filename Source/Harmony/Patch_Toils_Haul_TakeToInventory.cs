using System;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(Toils_Haul))]
    [HarmonyPatch(nameof(Toils_Haul.TakeToInventory))]
    [HarmonyPatch(new Type[] { typeof(TargetIndex), typeof(Func<int>) })]
    public static class Patch_Toils_Haul_TakeToInventory
    {
        public static void Postfix(Toil __result, TargetIndex ind)
        {
            var origInit = __result.initAction;

            __result.initAction = () =>
            {
                // Run vanilla behavior first
                origInit?.Invoke();

                var actor = __result.actor;
                var job = actor?.CurJob;
                var thing = job?.GetTarget(ind).Thing;

                // Only mark survival tools as forced if:
                // - pawn can use tools
                // - the picked-up thing is a SurvivalTool
                // - the tool is actually in inventory after the vanilla action
                // - the job was player-forced
                if (actor != null
                    && actor.CanUseSurvivalTools()
                    && thing is SurvivalTool
                    && actor.inventory != null
                    && actor.inventory.Contains(thing)
                    && job != null
                    && job.playerForced)
                {
                    actor.GetComp<Pawn_SurvivalToolAssignmentTracker>()?.forcedHandler.SetForced(thing, true);
                }
            };
        }
    }
}
