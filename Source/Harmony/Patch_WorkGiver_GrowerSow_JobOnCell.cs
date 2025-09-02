// RimWorld 1.6 / C# 7.3
// Patch_WorkGiver_GrowerSow_JobOnCell.cs
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(WorkGiver_GrowerSow))]
    [HarmonyPatch(nameof(WorkGiver_GrowerSow.JobOnCell))]
    public static class Patch_WorkGiver_GrowerSow_JobOnCell
    {
        public static void Postfix(ref Job __result, Pawn pawn)
        {
            var job = __result;
            if (job == null) return;
            if (job.def != JobDefOf.CutPlant) return;

            var thing = job.targetA.Thing;
            var plant = thing?.def?.plant;
            if (plant == null || !plant.IsTree) return;

            // Use centralized tree-felling gate
            if (pawn != null && pawn.CanFellTrees())
            {
                __result = new Job(ST_JobDefOf.FellTree, job.targetA);
                return;
            }

            // Block vanilla cut if requirements aren't met
            __result = null;

            if (SurvivalToolUtility.IsDebugLoggingEnabled && pawn != null)
            {
                var key = $"ST_BlockSowCutTree_{pawn.ThingID}";
                if (SurvivalToolUtility.ShouldLogWithCooldown(key))
                    Log.Message($"[SurvivalTools] Blocked CutPlant on tree '{thing?.LabelShort ?? "unknown"}' for {pawn.LabelShort}: missing tree-felling tools.");
            }
        }
    }
}
