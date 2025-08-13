using HarmonyLib;
using RimWorld;
using Verse;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(WorkGiver))]
    [HarmonyPatch(nameof(WorkGiver.MissingRequiredCapacity))]
    public static class Patch_WorkGiver_MissingRequiredCapacity
    {
        public static void Postfix(WorkGiver __instance, ref PawnCapacityDef __result, Pawn pawn)
        {
            if (__result != null) return;                 // vanilla already set a blocker
            if (pawn == null) return;

            var def = __instance?.def;
            if (def == null) return;

            var ext = def.GetModExtension<WorkGiverExtension>();
            var required = ext?.requiredStats;
            if (required == null || required.Count == 0) return;

            if (!pawn.MeetsWorkGiverStatRequirements(required))
            {
                __result = PawnCapacityDefOf.Manipulation;
            }
        }
    }
}
