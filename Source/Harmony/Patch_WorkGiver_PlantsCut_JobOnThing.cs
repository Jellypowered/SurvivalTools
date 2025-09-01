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
            // Prevent PlantsCut work giver from handling trees
            // Trees should be handled by our specialized tree felling logic
            if (t?.def?.plant?.IsTree == true)
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                {
                    Log.Message($"[SurvivalTools] PlantsCut work giver blocked from cutting tree {t.def.defName} - should use tree felling instead");
                }
                __result = null;
            }
        }
    }
}
