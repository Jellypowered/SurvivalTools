//Rimworld 1.6 / C# 7.3
//Patch_WorkGiver_Scanner_ToolGate.cs
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

            // Check if any of the required stats would block this job
            var settings = SurvivalTools.Settings;
            foreach (var stat in requiredStats)
            {
                // For optional stats, check extra hardcore mode settings
                bool isOptionalStat = IsOptionalStat(stat);
                bool shouldBlock = false;

                if (isOptionalStat)
                {
                    // Optional stats only block in extra hardcore mode when specifically enabled
                    if (settings.extraHardcoreMode && settings.IsStatRequiredInExtraHardcore(stat))
                    {
                        shouldBlock = true;
                    }
                }
                else
                {
                    // Required stats always block in hardcore mode
                    shouldBlock = true;
                }

                if (shouldBlock && !pawn.MeetsWorkGiverStatRequirements(new List<StatDef> { stat }, instance.def))
                {
                    result = false;
                    if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    {
                        Log.Message($"[SurvivalTools.ToolGate] {pawn.LabelShort} blocked from job {instance.def.defName} due to missing tool for stat {stat.defName}.");
                    }
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if a stat is considered "optional" (cleaning, butchery, medical)
        /// vs required (mining, construction, etc.)
        /// </summary>
        private static bool IsOptionalStat(StatDef stat)
        {
            return stat == ST_StatDefOf.CleaningSpeed ||
                   stat == ST_StatDefOf.ButcheryFleshSpeed ||
                   stat == ST_StatDefOf.ButcheryFleshEfficiency ||
                   stat == ST_StatDefOf.MedicalOperationSpeed ||
                   stat == ST_StatDefOf.MedicalSurgerySuccessChance;
        }

        private static List<StatDef> GetRequiredToolStats(WorkGiverDef wgDef)
        {
            return wgDef?.GetModExtension<WorkGiverExtension>()
                        ?.requiredStats?.Where(s => s.RequiresSurvivalTool())
                        .ToList();
        }
    }
}
