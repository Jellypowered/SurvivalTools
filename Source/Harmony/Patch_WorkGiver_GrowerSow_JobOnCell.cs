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

            // Must be a cut-plant job on a tree
            var thing = job.targetA.Thing;
            if (job.def != JobDefOf.CutPlant || thing == null) return;

            var plantDef = thing.def?.plant;
            if (plantDef == null || !plantDef.IsTree) return;

            // Use centralized tree felling check
            if (pawn != null && pawn.CanFellTrees())
            {
                __result = new Job(ST_JobDefOf.FellTree, job.targetA);
            }
            else
            {
                // Block the vanilla cut job if requirements aren't met
                if (SurvivalToolUtility.IsDebugLoggingEnabled && pawn != null)
                {
                    Log.Message($"[SurvivalTools] {pawn.LabelShort} cannot cut tree {thing.LabelShort} - missing tree felling tools");
                }
                __result = null;
            }
        }
    }
}
