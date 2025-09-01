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
                if (actor?.CanUseSurvivalTools() != true) return;

                var job = actor.CurJob;
                if (job?.playerForced != true) return;

                var thing = job.GetTarget(ind).Thing;
                if (!(thing is SurvivalTool tool)) return;

                // Only mark as forced if the tool is actually in inventory after the vanilla action
                if (actor.inventory?.Contains(tool) == true)
                {
                    var tracker = actor.GetComp<Pawn_SurvivalToolAssignmentTracker>();
                    tracker?.forcedHandler.SetForced(tool, true);

                    if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    {
                        Log.Message($"[SurvivalTools] Marked {tool.Label} as forced for {actor.Name}");
                    }
                }
            };
        }
    }
}
