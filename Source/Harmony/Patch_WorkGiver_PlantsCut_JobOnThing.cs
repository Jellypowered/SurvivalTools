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
            var plant = t?.def?.plant;
            if (plant != null && plant.IsTree)
            {
                __result = null;
            }
        }
    }
}
