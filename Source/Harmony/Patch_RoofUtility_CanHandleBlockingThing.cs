using HarmonyLib;
using RimWorld;
using Verse;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(RoofUtility))]
    [HarmonyPatch(nameof(RoofUtility.CanHandleBlockingThing))]
    public static class Patch_RoofUtility_CanHandleBlockingThing
    {
        public static void Postfix(ref bool __result, Thing blocker, Pawn worker)
        {
            if (!__result) return;                      // already false; nothing to do
            if (worker == null) return;                 // no worker info
            if (blocker?.def?.plant?.IsTree != true) return;

            // Use centralized tree felling check
            if (!worker.CanFellTrees())
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                {
                    Log.Message($"[SurvivalTools] {worker.LabelShort} cannot handle tree blocker {blocker.LabelShort} - missing tree felling tools");
                }
                __result = false;
            }
        }
    }
}
