// RimWorld 1.6 / C# 7.3
// Source/Helpers/SurvivalToolValidation.cs
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;
using System.Reflection;
using System;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Helpers
{
    /// <summary>
    /// Utility class for validating existing jobs when settings change
    /// </summary>
    public static class SurvivalToolValidation
    {
        /// <summary>
        /// Manual test method to trigger job validation for debugging
        /// </summary>
        public static void TestValidation()
        {
            LogDebug("[SurvivalTools.JobValidation] Manual test validation triggered", "JobValidation_ManualTest");
            ValidateExistingJobs("manual test trigger");
        }

        /// <summary>
        /// Last-ditch safety net: cancels obviously invalid jobs that sometimes slip through
        /// normal validation (e.g., Ingest with CleaningSpeed requirement).
        /// Called once after game load, or manually if needed.
        /// </summary>
        private static void CancelBadJobs(Map map)
        {
            if (map == null) return;

            // Snapshot pawns to avoid "collection modified" exceptions when ending jobs
            var pawns = map.mapPawns.FreeColonistsSpawned.ToList();

            foreach (var pawn in pawns)
            {
                var job = pawn.CurJob;
                if (job == null) continue;

                var s = SurvivalToolsMod.Settings;
                bool enforce = SurvivalToolUtility.IsHardcoreModeEnabled || (s != null && s.extraHardcoreMode);
                if (!enforce) continue;

                // Special case: Ingest job that wrongly uses CleaningSpeed
                if (job.def == JobDefOf.Ingest &&
                    StatGatingHelper.ShouldBlockJobForStat(ST_StatDefOf.CleaningSpeed, s, pawn))
                {
                    // End the job - Phase 6 PreWork_AutoEquip handles preventing restart
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true);

                    // LEGACY REMOVED: Patch_Pawn_JobTracker_ExtraHardcore reflection code deleted
                    // Phase 6 JobGate system handles job blocking without needing per-pawn guards

                    // Use centralized debug logging with cooldown to avoid per-pawn spam
                    LogDebug($"[SurvivalTools.JobValidation] Cancelled bad Ingest job for {pawn.LabelShort} (cleaning requirement, no tool).", $"JobValidation_Cancel_{pawn.ThingID}");

                    return;
                }

                // 📝 Future me: add other edge-case checks here if new jobs show up.
            }
        }


        /// <summary>
        /// Validates all existing jobs across all pawns when settings change.
        /// Cancels jobs that require tools the pawns don't have in hardcore mode.
        /// </summary>
        public static void ValidateExistingJobs(string reason)
        {
            LogDebug($"[SurvivalTools.JobValidation] ValidateExistingJobs called with reason: {reason}", $"JobValidation_Validate_{reason}");

            var settings = SurvivalToolsMod.Settings;
            LogDebug($"[SurvivalTools.JobValidation] Settings state - hardcoreMode: {settings?.hardcoreMode}, extraHardcoreMode: {settings?.extraHardcoreMode}", $"JobValidation_Settings_{settings?.hardcoreMode}_{settings?.extraHardcoreMode}");

            if (settings?.hardcoreMode != true)
            {
                LogDebug($"[SurvivalTools.JobValidation] Skipping validation - hardcore mode not enabled (reason: {reason})", $"JobValidation_Skip_{reason}");
                return;
            }

            LogDebug($"[SurvivalTools.JobValidation] Starting job validation (reason: {reason})", $"JobValidation_Start_{reason}");

            int cancelledJobs = 0;
            var allPawns = PawnsFinder.AllMaps_FreeColonists
                .Where(p => p.CanUseSurvivalTools()).ToList();

            LogDebug($"[SurvivalTools.JobValidation] Found {allPawns.Count} pawns to check", $"JobValidation_PawnCount_{allPawns.Count}");

            foreach (var pawn in allPawns)
            {
                if (pawn.jobs?.curJob?.def == null) continue;

                var currentJob = pawn.jobs.curJob;
                LogDebug($"[SurvivalTools.JobValidation] Checking {pawn.LabelShort}: job {currentJob.def.defName}", $"JobValidation_Check_{pawn.ThingID}_{currentJob.def.defName}");

                // Map JobDef to WorkGiverDef for gating logic
                var workGiverDef = JobDefToWorkGiverDefHelper.GetWorkGiverDefForJob(currentJob.def);
                if (workGiverDef == null)
                {
                    LogDebug($"[SurvivalTools.JobValidation] No WorkGiverDef found for job {currentJob.def.defName}, skipping.", $"JobValidation_NoWorkGiver_{currentJob.def.defName}");
                    continue;
                }

                // Only gate jobs that are eligible and enabled in settings
                if (!SurvivalToolUtility.ShouldGateByDefault(workGiverDef))
                {
                    LogDebug($"[SurvivalTools.JobValidation] Job {currentJob.def.defName} is not gate-eligible, skipping.", $"JobValidation_NotGateEligible_{currentJob.def.defName}");
                    continue;
                }

                // Phase 11.10: workSpeedGlobalJobGating check removed - only gate explicitly declared jobs

                var requiredStats = SurvivalToolUtility.RelevantStatsFor(null, currentJob.def);

                if (requiredStats.NullOrEmpty())
                {
                    LogDebug($"[SurvivalTools.JobValidation] No required stats for {currentJob.def.defName}", $"JobValidation_NoStats_{currentJob.def.defName}");
                    continue;
                }

                LogDebug($"[SurvivalTools.JobValidation] Required stats for {currentJob.def.defName}: {string.Join(", ", requiredStats.Select(s => s.defName))}", $"JobValidation_Stats_{currentJob.def.defName}");


                bool shouldCancel = false;
                foreach (var stat in requiredStats)
                {
                    // ✅ Unified gating logic via StatGatingHelper
                    bool shouldBlock = StatGatingHelper.ShouldBlockJobForStat(stat, settings, pawn);

                    LogDebug($"[SurvivalTools.JobValidation] Stat {stat.defName}: shouldBlock={shouldBlock}, hasTool={pawn.HasSurvivalToolFor(stat)}", $"JobValidation_Stat_{pawn.ThingID}_{currentJob.def.defName}_{stat.defName}");

                    if (shouldBlock)
                    {
                        shouldCancel = true;
                        LogDebug($"[SurvivalTools.JobValidation] {pawn.LabelShort} job {currentJob.def.defName} cancelled: missing tool for {stat.defName} ({reason})", $"JobValidation_Cancel_{pawn.ThingID}_{currentJob.def.defName}_{stat.defName}");
                        break;
                    }
                }

                if (shouldCancel)
                {
                    LogDebug($"[SurvivalTools.JobValidation] Ending job for {pawn.LabelShort}", $"JobValidation_End_{pawn.ThingID}_{currentJob.def.defName}");
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable, startNewJob: true, canReturnToPool: true);
                    cancelledJobs++;
                }
            }

            if (cancelledJobs > 0)
            {
                Messages.Message(
                    $"SurvivalTools: Cancelled {cancelledJobs} job(s) due to missing required tools ({reason})",
                    MessageTypeDefOf.NeutralEvent,
                    historical: false
                );
            }

            foreach (var map in Find.Maps)
            {
                //CancelBadIngestJobs(map);
                CancelBadJobs(map); // Catch all bad jobs blocker on load *FUTURE ME* 
            }

        }
    }
}
