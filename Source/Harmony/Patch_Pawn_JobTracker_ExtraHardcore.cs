// RimWorld 1.6 / C# 7.3
// Patch_Pawn_JobTracker_ExtraHardcore.cs
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

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

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Pawn_JobTracker.StartJob))]
        [HarmonyPriority(Priority.Last)] // run after other mods
        public static bool Prefix_StartJob(Pawn_JobTracker __instance, Job newJob)
        {
            bool ignore = false; // StartJob doesn't use __result; we just decide whether to skip original
            return CheckJobAssignment(__instance, newJob, setOrderedResult: ref ignore);
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
                    if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    {
                        var key = $"JobBlock_{pawn.ThingID}_{job.def.defName}_{stat.defName}";
                        if (SurvivalToolUtility.ShouldLogWithCooldown(key))
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
        /// Required stats always block in hardcore. Optional stats block only if Extra Hardcore requires them.
        /// </summary>
        private static bool ShouldBlockJobForMissingStat(StatDef stat, SurvivalToolsSettings settings)
        {
            if (stat == null) return false;

            // Required stats: always block under hardcore
            if (!IsOptionalStat(stat)) return true;

            // Optional stats: only block if Extra Hardcore says they’re required
            return settings.extraHardcoreMode && settings.IsStatRequiredInExtraHardcore(stat);
        }

        /// <summary>
        /// Optional stats (cleaning, butchery, medical, research).
        /// </summary>
        private static bool IsOptionalStat(StatDef stat)
        {
            if (stat == null) return false;

            return stat == ST_StatDefOf.CleaningSpeed
                || stat == ST_StatDefOf.ButcheryFleshSpeed
                || stat == ST_StatDefOf.ButcheryFleshEfficiency
                || stat == ST_StatDefOf.MedicalOperationSpeed
                || stat == ST_StatDefOf.MedicalSurgerySuccessChance
                || stat == ST_StatDefOf.ResearchSpeed
                || stat.defName == "FieldResearchSpeedMultiplier"; // compatibility hook
        }
    }
}
