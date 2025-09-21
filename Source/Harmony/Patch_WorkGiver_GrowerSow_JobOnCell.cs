// RimWorld 1.6 / C# 7.3
// Source/Harmony/Patch_WorkGiver_GrowerSow_JobOnCell.cs
// Legacy patch to block vanilla tree cutting when tree-felling tools are required.
// TODO: evaluate removal after full tool integration is complete.

using HarmonyLib;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;
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

            if (IsDebugLoggingEnabled && pawn != null)
            {
                var key = $"ST_BlockSowCutTree_{pawn.ThingID}";
                if (ShouldLogWithCooldown(key))
                    LogDecision(key, $"[SurvivalTools] Blocked CutPlant on tree '{thing?.LabelShort ?? "unknown"}' for {pawn.LabelShort}: missing tree-felling tools.");
            }
        }
    }
}
