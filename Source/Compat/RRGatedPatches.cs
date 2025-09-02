// RRGatedPatches.cs
// RimWorld 1.6 / C# 7.3
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

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
                // Only wire any of this if RR is present.
                if (!CompatAPI.IsResearchReinventedActive)
                {
                    if (CompatAPI.IsCompatLoggingEnabled)
                        CompatAPI.LogCompat("RR gating: Research Reinvented not detected — skipping RR-specific patches.");
                    return;
                }

                if (CompatAPI.IsCompatLoggingEnabled)
                    CompatAPI.LogCompat("RR gating: Research Reinvented detected — applying gating patches.");

                // 1) Patch RR pawn extension methods if present.
                TryPatchPawnExtensionMethod("PeteTimesSix.ResearchReinvented.Utilities.PawnExtensions", "CanEverDoResearch");
                TryPatchPawnExtensionMethod("PeteTimesSix.ResearchReinvented.Utilities.PawnExtensions", "CanNowDoResearch");

                // 2) Patch base WorkGiver_Scanner declared methods (covers RR-derived scanners).
                var baseType = typeof(WorkGiver_Scanner);

                var miHasJobOnThing = AccessTools.Method(baseType, "HasJobOnThing");
                if (miHasJobOnThing != null)
                {
                    var prefix = new HarmonyMethod(AccessTools.Method(typeof(ResearchReinventedGatingPatches), nameof(Prefix_WorkGiverScanner_HasJobOnThing)))
                    {
                        priority = Priority.First
                    };
                    harmony.Patch(miHasJobOnThing, prefix: prefix);
                    if (CompatAPI.IsCompatLoggingEnabled)
                        CompatAPI.LogCompat("Compat RR: patched WorkGiver_Scanner.HasJobOnThing (base)");
                }

                var miHasJobOnCell = AccessTools.Method(baseType, "HasJobOnCell");
                if (miHasJobOnCell != null)
                {
                    var prefix = new HarmonyMethod(AccessTools.Method(typeof(ResearchReinventedGatingPatches), nameof(Prefix_WorkGiverScanner_HasJobOnCell)))
                    {
                        priority = Priority.First
                    };
                    harmony.Patch(miHasJobOnCell, prefix: prefix);
                    if (CompatAPI.IsCompatLoggingEnabled)
                        CompatAPI.LogCompat("Compat RR: patched WorkGiver_Scanner.HasJobOnCell (base)");
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
                // Explicitly look in RR's assembly
                var targetType = Type.GetType(typeFullName + ", ResearchReinvented");
                if (targetType == null) return;

                var method = AccessTools.Method(targetType, methodName);
                if (method == null) return;

                var prefix = new HarmonyMethod(AccessTools.Method(typeof(ResearchReinventedGatingPatches), nameof(Prefix_CanDoResearchExtension)))
                {
                    priority = Priority.First
                };

                harmony.Patch(method, prefix: prefix);

                if (CompatAPI.IsCompatLoggingEnabled)
                    CompatAPI.LogCompat($"Compat RR: successfully patched {typeFullName}.{methodName}");
            }
            catch (Exception e)
            {
                CompatAPI.LogCompatWarning($"Compat RR: failed to patch {typeFullName}.{methodName}: {e.Message}");
            }
        }

        // Prefix for RR extension methods like CanEverDoResearch / CanNowDoResearch
        // signature target: static bool X(Pawn pawn, ...)
        // our prefix: static bool Prefix(ref bool __result, Pawn pawn)
        private static bool Prefix_CanDoResearchExtension(ref bool __result, Pawn pawn)
        {
            try
            {
                if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                    return true; // don't gate animals/mechs/etc.

                // enforce only when hardcore or extra-hardcore enabled
                var s = SurvivalTools.Settings;
                bool enforce = SurvivalToolUtility.IsHardcoreModeEnabled || (s != null && s.extraHardcoreMode);
                if (!enforce) return true;

                // If pawn lacks required research tool, block.
                if (!ResearchReinventedCompat.PawnHasResearchTool(pawn))
                {
                    __result = false;
                    if (CompatAPI.IsCompatLoggingEnabled)
                        CompatAPI.LogCompat($"Blocking research check for {pawn.LabelShort} - no research tool (SurvivalTools enforcement).");
                    return false; // skip original
                }
            }
            catch (Exception e)
            {
                CompatAPI.LogCompatWarning($"Prefix_CanDoResearchExtension threw: {e.Message}");
            }

            return true; // continue original
        }

        // Prefix for WorkGiver_Scanner.HasJobOnThing
        private static bool Prefix_WorkGiverScanner_HasJobOnThing(WorkGiver_Scanner __instance, Pawn pawn, Thing t, ref bool __result)
        {
            try
            {
                if (__instance == null || pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                    return true;

                var s = SurvivalTools.Settings;
                bool enforce = SurvivalToolUtility.IsHardcoreModeEnabled || (s != null && s.extraHardcoreMode);
                if (!enforce) return true;

                var wgDef = __instance.def;
                if (wgDef == null) return true;

                // Only gate if this WG requires research-related stats (so we don't affect unrelated WGs).
                if (!RequiresResearchTools(wgDef)) return true;

                if (!ResearchReinventedCompat.PawnHasResearchTool(pawn))
                {
                    __result = false;
                    if (CompatAPI.IsCompatLoggingEnabled)
                        CompatAPI.LogCompat($"Blocking {wgDef.defName} HasJobOnThing for {pawn.LabelShort} - no research tool (SurvivalTools enforcement).");
                    return false;
                }
            }
            catch (Exception e)
            {
                CompatAPI.LogCompatWarning($"Prefix_WorkGiverScanner_HasJobOnThing threw: {e.Message}");
            }

            return true;
        }

        // Prefix for WorkGiver_Scanner.HasJobOnCell
        private static bool Prefix_WorkGiverScanner_HasJobOnCell(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, ref bool __result)
        {
            try
            {
                if (__instance == null || pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                    return true;

                var s = SurvivalTools.Settings;
                bool enforce = SurvivalToolUtility.IsHardcoreModeEnabled || (s != null && s.extraHardcoreMode);
                if (!enforce) return true;

                var wgDef = __instance.def;
                if (wgDef == null) return true;

                if (!RequiresResearchTools(wgDef)) return true;

                if (!ResearchReinventedCompat.PawnHasResearchTool(pawn))
                {
                    __result = false;
                    if (CompatAPI.IsCompatLoggingEnabled)
                        CompatAPI.LogCompat($"Blocking {wgDef.defName} HasJobOnCell for {pawn.LabelShort} - no research tool (SurvivalTools enforcement).");
                    return false;
                }
            }
            catch (Exception e)
            {
                CompatAPI.LogCompatWarning($"Prefix_WorkGiverScanner_HasJobOnCell threw: {e.Message}");
            }

            return true;
        }

        // Helper: does this WorkGiver require research-related stats (our extension)?
        private static bool RequiresResearchTools(WorkGiverDef wgDef)
        {
            try
            {
                var ext = wgDef.GetModExtension<WorkGiverExtension>();
                if (ext == null || ext.requiredStats == null || ext.requiredStats.Count == 0)
                    return false;

                // Prefer CompatAPI (RR present) and fall back to defs by name if needed.
                var researchStat = CompatAPI.GetResearchSpeedStat() ?? ST_StatDefOf.ResearchSpeed;
                if (researchStat != null && ext.requiredStats.Contains(researchStat))
                    return true;

                var fieldStat = CompatAPI.GetFieldResearchSpeedStat();
                if (fieldStat == null)
                    fieldStat = DefDatabase<StatDef>.GetNamedSilentFail("FieldResearchSpeedMultiplier");
                if (fieldStat != null && ext.requiredStats.Contains(fieldStat))
                    return true;
            }
            catch
            {
                // ignore — treat as not research-related
            }

            return false;
        }
    }
}
