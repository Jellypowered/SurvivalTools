//Rimworld 1.6 / C# 7.3
// Source/Harmony/Patch_MassUtility.cs

// Legacy Code: KEEP, but likely needs integration into our refactor.
using HarmonyLib;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.HarmonyStuff
{
    public static class Patch_MassUtility
    {
        [HarmonyPatch(typeof(MassUtility), nameof(MassUtility.CountToPickUpUntilOverEncumbered))]
        public static class CountToPickUpUntilOverEncumbered_Postfix
        {
            public static void Postfix(ref int __result, Pawn pawn, Thing thing)
            {
                if (__result <= 0 || pawn == null || thing == null) return;

                bool isSurvivalThing = thing is SurvivalTool || thing.def.IsToolStuff();

                if (!isSurvivalThing) return;

                // For tool-stuff stacks, treat the entire stack as a single "tool unit"
                int additionalToolUnits = thing.def.IsToolStuff() ? 1 : 1; // picking up any survival tool normally counts as 1
                // (if you wanted to count multiple individual items for stackable tools, change above accordingly)

                // If picking up the additionalToolUnits would exceed carry limit, disallow picking up any
                if (!pawn.CanCarryAnyMoreSurvivalTools(additionalToolUnits))
                {
                    __result = 0;
                    if (IsDebugLoggingEnabled)
                    {
                        const string key = "MassUtility_CountToPickUp_OverLimit";
                        if (ShouldLogWithCooldown(key))
                            LogDecision(key, $"[SurvivalTools] Preventing pickup: {pawn.LabelShort} cannot carry more survival tools (would pick up {thing.LabelCap}).");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(MassUtility), nameof(MassUtility.WillBeOverEncumberedAfterPickingUp))]
        public static class WillBeOverEncumberedAfterPickingUp_Postfix
        {
            public static void Postfix(ref bool __result, Pawn pawn, Thing thing, int count)
            {
                if (__result || pawn == null || thing == null) return;

                bool isSurvivalThing = thing is SurvivalTool || thing.def.IsToolStuff();
                if (!isSurvivalThing) return;

                // For tool-stuff stacks, treat the pickup as one additional tool unit (stack counts as single virtual tool).
                int additionalToolUnits = thing.def.IsToolStuff() ? 1 : count;

                if (!pawn.CanCarryAnyMoreSurvivalTools(additionalToolUnits))
                {
                    __result = true;
                    if (IsDebugLoggingEnabled)
                    {
                        const string key = "MassUtility_WillBeOver_OverLimit";
                        if (ShouldLogWithCooldown(key))
                            LogDecision(key, $"[SurvivalTools] WillBeOverEncumberedAfterPickingUp: {pawn.LabelShort} would exceed tool limit by picking up {thing.LabelCap} (count={count}).");
                    }
                }
            }
        }
    }
}
