// RimWorld 1.6 / C# 7.3
// Source/Compatibility/ResearchReinvented/RRGatedPatches.cs
//
// Applies gating patches so RR jobs respect SurvivalTools' hardcore tool requirements.

using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Compat
{
    [StaticConstructorOnStartup]
    internal static class ResearchReinventedGatingPatches
    {
        private const string HARMONY_ID = "SurvivalTools.Compat.ResearchReinventedGating";
        private static readonly Harmony harmony;

        static ResearchReinventedGatingPatches()
        {
            harmony = new Harmony(HARMONY_ID);

            try
            {
                if (!CompatAPI.IsResearchReinventedActive)
                {
                    if (IsCompatLogging())
                        LogCompat("RR gating: Research Reinvented not detected — skipping patches.");
                    return;
                }

                if (IsCompatLogging())
                    LogCompat("RR gating: Research Reinvented detected — applying patches.");

                // 1) Patch RR pawn extension methods
                TryPatchPawnExtensionMethod("PeteTimesSix.ResearchReinvented.Utilities.PawnExtensions", "CanEverDoResearch");
                TryPatchPawnExtensionMethod("PeteTimesSix.ResearchReinvented.Utilities.PawnExtensions", "CanNowDoResearch");

                // 2) Patch base WorkGiver_Scanner methods
                var baseType = typeof(WorkGiver_Scanner);

                var miThing = AccessTools.Method(baseType, "HasJobOnThing");
                if (miThing != null)
                {
                    harmony.Patch(miThing,
                        prefix: new HarmonyMethod(typeof(ResearchReinventedGatingPatches), nameof(Prefix_WorkGiverScanner_HasJobOnThing)) { priority = Priority.First });
                }

                var miCell = AccessTools.Method(baseType, "HasJobOnCell");
                if (miCell != null)
                {
                    harmony.Patch(miCell,
                        prefix: new HarmonyMethod(typeof(ResearchReinventedGatingPatches), nameof(Prefix_WorkGiverScanner_HasJobOnCell)) { priority = Priority.First });
                }
            }
            catch (Exception e)
            {
                Log.Error($"[SurvivalTools Compat] RR gating patch init failed: {e}");
            }
        }

        private static void TryPatchPawnExtensionMethod(string typeFullName, string methodName)
        {
            try
            {
                var targetType = Type.GetType(typeFullName + ", ResearchReinvented");
                if (targetType == null) return;

                var method = AccessTools.Method(targetType, methodName);
                if (method == null) return;

                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(ResearchReinventedGatingPatches), nameof(Prefix_CanDoResearchExtension)) { priority = Priority.First });

                if (IsCompatLogging())
                    LogCompat($"Compat RR: successfully patched {typeFullName}.{methodName}");
            }
            catch (Exception e)
            {
                LogCompatWarning($"Compat RR: failed to patch {typeFullName}.{methodName}: {e.Message}");
            }
        }

        private static bool Prefix_CanDoResearchExtension(ref bool __result, Pawn pawn)
        {
            try
            {
                if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                    return true;

                var s = SurvivalTools.Settings;
                bool enforce = SurvivalToolUtility.IsHardcoreModeEnabled || (s != null && s.extraHardcoreMode);
                if (!enforce) return true;

                if (!CompatAPI.PawnHasResearchTools(pawn))
                {
                    __result = false;
                    if (IsCompatLogging())
                        LogCompat($"Blocking research check for {pawn.LabelShort} - no research tool.");
                    return false;
                }
            }
            catch (Exception e)
            {
                LogCompatWarning($"Prefix_CanDoResearchExtension threw: {e.Message}");
            }

            return true;
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

                if (!RequiresResearchTools(wgDef)) return true;

                if (!CompatAPI.PawnHasResearchTools(pawn))
                {
                    __result = false;
                    if (IsCompatLogging())
                        LogCompat($"Blocking {wgDef.defName} {context} for {pawn.LabelShort} - no research tool.");
                    return false;
                }
            }
            catch (Exception e)
            {
                LogCompatWarning($"CheckWorkGiverGate threw in {context}: {e.Message}");
            }

            return true;
        }

        private static bool RequiresResearchTools(WorkGiverDef wgDef)
        {
            try
            {
                var ext = wgDef.GetModExtension<WorkGiverExtension>();
                if (ext == null || ext.requiredStats.NullOrEmpty())
                    return false;

                var researchStat = CompatAPI.GetResearchSpeedStat() ?? ST_StatDefOf.ResearchSpeed;
                if (researchStat != null && ext.requiredStats.Contains(researchStat))
                    return true;

                var fieldStat = CompatAPI.GetFieldResearchSpeedStat() ??
                                DefDatabase<StatDef>.GetNamedSilentFail("FieldResearchSpeedMultiplier");
                if (fieldStat != null && ext.requiredStats.Contains(fieldStat))
                    return true;
            }
            catch
            {
                // fail safe
            }

            return false;
        }
    }
}
