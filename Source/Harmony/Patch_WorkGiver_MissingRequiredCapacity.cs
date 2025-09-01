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
            // Early exit if vanilla already set a blocker or pawn is null
            if (__result != null || pawn == null) return;

            var required = __instance?.def?.GetModExtension<WorkGiverExtension>()?.requiredStats;
            if (required?.Count > 0 && !pawn.MeetsWorkGiverStatRequirements(required))
            {
                __result = PawnCapacityDefOf.Manipulation;
            }
        }
    }
}
