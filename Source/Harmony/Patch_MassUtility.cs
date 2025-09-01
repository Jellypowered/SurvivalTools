using HarmonyLib;
using RimWorld;
using Verse;

namespace SurvivalTools.HarmonyStuff
{
    public static class Patch_MassUtility
    {
        [HarmonyPatch(typeof(MassUtility), nameof(MassUtility.CountToPickUpUntilOverEncumbered))]
        public static class CountToPickUpUntilOverEncumbered_Postfix
        {
            public static void Postfix(ref int __result, Pawn pawn, Thing thing)
            {
                if (__result > 0 && thing is SurvivalTool && !pawn.CanCarryAnyMoreSurvivalTools())
                {
                    __result = 0;
                }
            }
        }

        [HarmonyPatch(typeof(MassUtility), nameof(MassUtility.WillBeOverEncumberedAfterPickingUp))]
        public static class WillBeOverEncumberedAfterPickingUp_Postfix
        {
            public static void Postfix(ref bool __result, Pawn pawn, Thing thing, int count)
            {
                if (!__result && thing is SurvivalTool && !pawn.CanCarryAnyMoreSurvivalTools(count))
                {
                    __result = true;
                }
            }
        }
    }
}
