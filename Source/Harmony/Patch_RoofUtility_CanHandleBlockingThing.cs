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

            var fellWG = ST_WorkGiverDefOf.FellTrees;
            var ext = fellWG?.GetModExtension<WorkGiverExtension>();
            var req = ext?.requiredStats;
            if (req == null || req.Count == 0) return;

            if (!worker.MeetsWorkGiverStatRequirements(req))
                __result = false;
        }
    }
}
