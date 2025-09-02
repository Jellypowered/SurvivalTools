// RimWorld 1.6 / C# 7.3
// Patch_WorkGiver_PlantsCut_JobOnThing.cs
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

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

            // Block PlantsCut on trees; specialized felling logic will handle them instead.
            __result = null;

            // Cooldowned debug log to avoid spam.
            if (SurvivalToolUtility.IsDebugLoggingEnabled)
            {
                // Keyed by pawn + thing def to keep noise down while still being informative.
                var key = $"PlantsCut_BlockTree_{pawn?.ThingID ?? "nullPawn"}_{t.def.defName}";
                if (SurvivalToolUtility.ShouldLogWithCooldown(key))
                {
                    Log.Message($"[SurvivalTools] PlantsCut blocked on tree '{t.def.defName}' (pawn: {pawn?.LabelShort ?? "null"}) — use felling job instead.");
                }
            }
        }
    }
}
