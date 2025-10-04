// RimWorld 1.6 / C# 7.3
// Source/Harmony/Patch_WorkGiver_PlantsCut_JobOnThing.cs
// Legacy Code: Patch to block WorkGiver_PlantsCut from assigning CutPlant jobs on trees, so that specialized felling logic can be used instead.
// Retain for compatibility with existing saves and to avoid regressions.

using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(WorkGiver_PlantsCut))]
    [HarmonyPatch(nameof(WorkGiver_PlantsCut.JobOnThing))]
    public static class Patch_WorkGiver_PlantsCut_JobOnThing
    {
        public static void Postfix(ref Job __result, Thing t, Pawn pawn)
        {
            // Defensive: bail fast if nothing to do or the call has already decided "no job".
            if (__result == null || t == null) return;

            // Only interfere with the PlantsCut-produced CutPlant job, and only for trees.
            // (Avoids touching other job types or non-plant targets.)
            var plant = t.def?.plant;
            if (plant == null || !plant.IsTree) return;
            if (__result.def != JobDefOf.CutPlant) return;

            // STC-aware: If STC is active (external tree authority), let it handle trees entirely
            // Otherwise, block PlantsCut on trees so our specialized felling logic handles them
            bool stcActive = Helpers.TreeSystemArbiterActiveHelper.IsSTCAuthorityActive();
            if (stcActive)
            {
                // STC is handling trees, don't interfere - but still block to avoid tool confusion
                __result = null;
                if (IsDebugLoggingEnabled)
                {
                    var key = $"PlantsCut_BlockTree_STC_{pawn?.ThingID ?? "nullPawn"}_{t.def.defName}";
                    if (ShouldLogWithCooldown(key))
                    {
                        LogDecision(key, $"[SurvivalTools] PlantsCut blocked on tree '{t.def.defName}' (STC active) — deferring to STC.");
                    }
                }
                return;
            }

            // ST authority: Block PlantsCut on trees; specialized felling logic will handle them instead.
            __result = null;

            // Cooldowned debug log to avoid spam.
            if (IsDebugLoggingEnabled)
            {
                // Keyed by pawn + thing def to keep noise down while still being informative.
                var key = $"PlantsCut_BlockTree_{pawn?.ThingID ?? "nullPawn"}_{t.def.defName}";
                if (ShouldLogWithCooldown(key))
                {
                    LogDecision(key, $"[SurvivalTools] PlantsCut blocked on tree '{t.def.defName}' (pawn: {pawn?.LabelShort ?? "null"}) — use felling job instead.");
                }
            }
        }
    }
}
