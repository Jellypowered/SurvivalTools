using HarmonyLib;
using RimWorld;
using Verse;
using System.Collections.Generic;
using System.Linq;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(WorkGiver_Scanner))]
    public static class Patch_WorkGiver_Scanner_ToolGate
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(WorkGiver_Scanner.HasJobOnThing))]
        public static bool Prefix_HasJobOnThing(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            return CheckToolRequirements(__instance, pawn, ref __result);
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(WorkGiver_Scanner.HasJobOnCell))]
        public static bool Prefix_HasJobOnCell(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, bool forced, ref bool __result)
        {
            return CheckToolRequirements(__instance, pawn, ref __result);
        }

        private static bool CheckToolRequirements(WorkGiver_Scanner instance, Pawn pawn, ref bool result)
        {
            if (SurvivalTools.Settings?.hardcoreMode != true || !pawn.CanUseSurvivalTools())
                return true;

            var requiredStats = GetRequiredToolStats(instance.def);
            if (requiredStats.NullOrEmpty())
                return true;

            // EXCEPTION: Cleaning, research, medical, and butchery jobs are always allowed (just less effective without tools)
            // This prevents blocking critical colony functions and allows tribal progression
            bool isCleaningJob = requiredStats.Contains(ST_StatDefOf.CleaningSpeed);
            bool isResearchJob = requiredStats.Contains(ST_StatDefOf.ResearchSpeed);
            bool isMedicalJob = requiredStats.Contains(ST_StatDefOf.MedicalOperationSpeed) ||
                              requiredStats.Contains(ST_StatDefOf.MedicalSurgerySuccessChance);
            bool isButcheryJob = requiredStats.Contains(ST_StatDefOf.ButcheryFleshSpeed) ||
                               requiredStats.Contains(ST_StatDefOf.ButcheryFleshEfficiency);
            if (isCleaningJob || isResearchJob || isMedicalJob || isButcheryJob)
                return true;

            if (pawn.MeetsWorkGiverStatRequirements(requiredStats, instance.def))
                return true;

            result = false;
            if (SurvivalToolUtility.IsDebugLoggingEnabled)
            {
                Log.Message($"[SurvivalTools.ToolGate] {pawn.LabelShort} blocked from job {instance.def.defName} due to missing tool.");
            }
            return false;
        }

        private static List<StatDef> GetRequiredToolStats(WorkGiverDef wgDef)
        {
            return wgDef?.GetModExtension<WorkGiverExtension>()
                        ?.requiredStats?.Where(s => s.RequiresSurvivalTool())
                        .ToList();
        }
    }
}
