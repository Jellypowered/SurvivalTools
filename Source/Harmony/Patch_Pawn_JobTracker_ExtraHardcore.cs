// RimWorld 1.6 / C# 7.3
// Patch_Pawn_JobTracker_ExtraHardcore.cs
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;
using SurvivalTools.Helpers;

namespace SurvivalTools.HarmonyStuff
{
    /// <summary>
    /// Blocks jobs that bypass WorkGiver gating when tools are missing in Hardcore / Extra Hardcore.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker))]
    public static class Patch_Pawn_JobTracker_ExtraHardcore
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_JobTracker), "pawn");

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Pawn_JobTracker.TryTakeOrderedJob))]
        [HarmonyPriority(Priority.Last)] // run after other mods
        public static bool Prefix_TryTakeOrderedJob(Pawn_JobTracker __instance, Job job, ref bool __result)
        {
            return CheckJobAssignment(__instance, job, setOrderedResult: ref __result);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Pawn_JobTracker.StartJob))]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix_StartJob(Pawn_JobTracker __instance, Job newJob)
        {
            var pawnField = AccessTools.Field(typeof(Pawn_JobTracker), "pawn");
            var pawn = (Pawn)pawnField.GetValue(__instance);
            if (pawn == null || newJob == null) return;

            var settings = SurvivalTools.Settings;
            if (settings?.hardcoreMode != true || !pawn.CanUseSurvivalTools()) return;

            var requiredStats = SurvivalToolUtility.RelevantStatsFor(null, newJob.def);
            if (requiredStats.NullOrEmpty()) return;

            foreach (var stat in requiredStats)
            {
                if (ShouldBlockJobForMissingStat(stat, settings) && !pawn.HasSurvivalToolFor(stat))
                {
                    if (IsDebugLoggingEnabled)
                        Log.Message($"[SurvivalTools.JobBlock] Aborting just-started job {newJob.def.defName} on {pawn.LabelShort} (missing {stat.defName}).");

                    if (__instance.curJob == newJob)
                        __instance.EndCurrentJob(JobCondition.Incompletable, startNewJob: true, canReturnToPool: true);

                    break;
                }
            }
        }
        private static bool CheckJobAssignment(Pawn_JobTracker jobTracker, Job job, ref bool setOrderedResult)
        {
            // Resolve pawn safely (cached FieldInfo)
            var pawn = jobTracker != null ? (Pawn)PawnField.GetValue(jobTracker) : null;
            var settings = SurvivalTools.Settings;

            // Only gate in hardcore mode, with a valid pawn that can use tools, and a valid job def
            if (settings == null || settings.hardcoreMode != true || pawn == null || !pawn.CanUseSurvivalTools() || job?.def == null)
                return true;

            // Which stats matter for this job?
            var requiredStats = SurvivalToolUtility.RelevantStatsFor(null, job.def);
            if (requiredStats == null || requiredStats.Count == 0)
                return true;

            // Block if any relevant stat requires a tool we don't have (per Hardcore/Extra Hardcore rules)
            for (int i = 0; i < requiredStats.Count; i++)
            {
                var stat = requiredStats[i];
                if (stat == null) continue;

                if (ShouldBlockJobForMissingStat(stat, settings) && !pawn.HasSurvivalToolFor(stat))
                {
                    // Cooldown the log to avoid spam
                    if (IsDebugLoggingEnabled)
                    {
                        var key = $"JobBlock_{pawn.ThingID}_{job.def.defName}_{stat.defName}";
                        if (ShouldLogWithCooldown(key))
                            Log.Message($"[SurvivalTools.JobBlock] Blocking direct job: {pawn.LabelShort} -> {job.def.defName} (missing tool for {stat.defName})");
                    }

                    // Tell TryTakeOrderedJob we handled it (fail) and skip original; for StartJob, just skip original.
                    setOrderedResult = false;
                    return false;
                }
            }

            return true; // allow original method
        }

        /// <summary>
        /// Check if a stat should block jobs based on user settings.
        /// </summary>
        private static bool ShouldBlockJobForMissingStat(StatDef stat, SurvivalToolsSettings settings)
        {
            if (stat == null) return false;

            // Core work stats that always block jobs
            if (StatFilters.ShouldBlockJobForMissingStat(stat))
                return true;

            // Optional stats that have specific user configuration
            if (stat == ST_StatDefOf.CleaningSpeed)
                return settings.requireCleaningTools;
            if (stat == ST_StatDefOf.ButcheryFleshSpeed || stat == ST_StatDefOf.ButcheryFleshEfficiency)
                return settings.requireButcheryTools;
            if (stat == ST_StatDefOf.MedicalOperationSpeed || stat == ST_StatDefOf.MedicalSurgerySuccessChance)
                return settings.requireMedicalTools;
            if (stat == ST_StatDefOf.ResearchSpeed)
                return false; // Research is currently always optional in normal hardcore mode

            // In extra hardcore mode, check additional requirements
            if (settings.extraHardcoreMode && settings.IsStatRequiredInExtraHardcore(stat))
                return true;

            return false;
        }

        /// <summary>
        /// Optional stats (cleaning, butchery, medical, research).
        /// </summary>
        private static bool IsOptionalStat(StatDef stat)
        {
            return StatFilters.IsOptionalStat(stat);
        }
    }
}
