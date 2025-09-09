// RimWorld 1.6 / C# 7.3
// Consolidated patches for Research Reinvented (was RRGatedPatches.cs + parts of RRReflectionAPI)
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Compat.ResearchReinvented
{
    [StaticConstructorOnStartup]
    internal static class RRPatches
    {
        private const string HARMONY_ID = "SurvivalTools.Compat.ResearchReinventedGating";
        private static readonly Harmony harmony;

        static RRPatches()
        {
            harmony = new Harmony(HARMONY_ID);

            try
            {
                if (!CompatAPI.IsResearchReinventedActive)
                {
                    if (IsCompatLogging()) LogCompat("RR gating: Research Reinvented not detected — skipping patches.");
                    return;
                }

                if (IsCompatLogging()) LogCompat("RR gating: Research Reinvented detected — applying patches.");

                // Patch RR pawn extension methods (postfixes are applied by RRHelpers.Initialize which calls ApplyHarmonyHooks)
                RRHelpers.Initialize();

                // Patch base WorkGiver_Scanner methods to gate jobs early when hardcore enforcement is active
                var baseType = typeof(WorkGiver_Scanner);

                var miThing = AccessTools.Method(baseType, "HasJobOnThing");
                if (miThing != null)
                {
                    harmony.Patch(miThing,
                        prefix: new HarmonyMethod(typeof(RRPatches), nameof(Prefix_WorkGiverScanner_HasJobOnThing)) { priority = Priority.First });
                }

                var miCell = AccessTools.Method(baseType, "HasJobOnCell");
                if (miCell != null)
                {
                    harmony.Patch(miCell,
                        prefix: new HarmonyMethod(typeof(RRPatches), nameof(Prefix_WorkGiverScanner_HasJobOnCell)) { priority = Priority.First });
                }
            }
            catch (Exception e)
            {
                Log.Error($"[SurvivalTools Compat] RR gating patch init failed: {e}");
            }
        }

        private static bool Prefix_WorkGiverScanner_HasJobOnThing(WorkGiver_Scanner __instance, Pawn pawn, Thing t, ref bool __result)
        {
            return CheckWorkGiverGate(__instance?.def, pawn, ref __result, "HasJobOnThing");
        }

        private static bool Prefix_WorkGiverScanner_HasJobOnCell(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, ref bool __result)
        {
            return CheckWorkGiverGate(__instance?.def, pawn, ref __result, "HasJobOnCell");
        }

        private static bool CheckWorkGiverGate(WorkGiverDef wgDef, Pawn pawn, ref bool __result, string context)
        {
            try
            {
                if (wgDef == null || pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                    return true;

                var s = SurvivalTools.Settings;
                bool enforce = SurvivalToolUtility.IsHardcoreModeEnabled || (s != null && s.extraHardcoreMode);
                if (!enforce) return true;

                if (!RRHelpers.RequiresResearchTools(wgDef)) return true;

                if (!CompatAPI.PawnHasResearchTools(pawn))
                {
                    __result = false;
                    if (IsCompatLogging()) LogCompat($"Blocking {wgDef.defName} {context} for {pawn.LabelShort} - no research tool.");
                    return false;
                }
            }
            catch (Exception e)
            {
                LogCompatWarning($"CheckWorkGiverGate threw in {context}: {e.Message}");
            }

            return true;
        }
    }
}
