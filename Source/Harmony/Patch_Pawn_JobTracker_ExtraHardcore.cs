// RimWorld 1.6 / C# 7.3
// Patch_Pawn_JobTracker_ExtraHardcore.cs
using System.Collections.Generic;
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

        // NEW: per-pawn guard so we don't end/restart >1 time per tick and cause thrash
        // key = pawn.thingIDNumber, value = last game tick we aborted a job
        private static readonly Dictionary<int, int> lastAbortTickByPawn = new Dictionary<int, int>();
        // Track suppressed logs so we can print counts later
        private static readonly Dictionary<string, int> suppressedLogCounts = new Dictionary<string, int>();


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
            // Defensive: resolve pawn and settings
            var pawn = __instance != null ? (Pawn)PawnField.GetValue(__instance) : null;
            var settings = SurvivalTools.Settings;

            if (pawn == null || newJob == null) return;
            if (settings?.hardcoreMode != true || !pawn.CanUseSurvivalTools()) return;

            // Avoid thrash: do not repeatedly abort within the same tick for this pawn
            int tick = Find.TickManager.TicksGame;
            int id = pawn.thingIDNumber;
            if (lastAbortTickByPawn.TryGetValue(id, out int lastTick) && lastTick == tick)
                return; // already handled this pawn this tick

            // Optional: don't auto-cancel explicit player-forced jobs in the postfix
            if (newJob.playerForced) return;

            // 🔧 Special-case: Ingest jobs sometimes sneak in CleaningSpeed as a requirement.
            // They normally don't map to a WorkGiver, so catch them here instead.
            if (newJob.def == JobDefOf.Ingest &&
    StatGatingHelper.ShouldBlockJobForStat(ST_StatDefOf.CleaningSpeed, settings, pawn))
            {
                if (IsDebugLoggingEnabled)
                {
                    var key = $"JobBlock_Ingest_{pawn.ThingID}";
                    if (ShouldLogWithCooldown(key))
                    {
                        // Print normal + suppressed count if any
                        if (suppressedLogCounts.TryGetValue(key, out int count) && count > 0)
                        {
                            Log.Message($"[SurvivalTools.JobBlock] Aborting Ingest job on {pawn.LabelShort} (cleaning requirement, no tool). " +
                                        $"[{count} suppressed]");
                            suppressedLogCounts[key] = 0;
                        }
                        else
                        {
                            Log.Message($"[SurvivalTools.JobBlock] Aborting Ingest job on {pawn.LabelShort} (cleaning requirement, no tool).");
                        }
                    }
                    else
                    {
                        suppressedLogCounts[key] = suppressedLogCounts.TryGetValue(key, out int count) ? count + 1 : 1;
                    }
                }

                lastAbortTickByPawn[id] = tick;

                if (__instance.curJob == newJob)
                    __instance.EndCurrentJob(JobCondition.Incompletable, startNewJob: false, canReturnToPool: true);

                return;
            }

            // Normal gating: check stats from WorkGiver/job defs
            var requiredStats = SurvivalToolUtility.RelevantStatsFor(null, newJob.def);
            if (requiredStats.NullOrEmpty()) return;

            foreach (var stat in requiredStats)
            {
                if (ShouldBlockJobForMissingStat(stat, settings) && !pawn.HasSurvivalToolFor(stat))
                {
                    if (IsDebugLoggingEnabled)
                        Log.Message($"[SurvivalTools.JobBlock] Aborting just-started job {newJob.def.defName} on {pawn.LabelShort} (missing {stat.defName}).");

                    // Record guard BEFORE ending job to prevent immediate same-tick loops
                    lastAbortTickByPawn[id] = tick;

                    // CRITICAL CHANGE: do NOT request immediate requeue here.
                    // Let vanilla re-evaluate on the next normal opportunity.
                    if (__instance.curJob == newJob)
                        __instance.EndCurrentJob(JobCondition.Incompletable, startNewJob: false, canReturnToPool: true);

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

                    // Tell TryTakeOrderedJob we handled it (fail) and skip original
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
