// RimWorld 1.6 / C# 7.3
// Source/Harmony/Patch_Pawn_JobTracker_ExtraHardcore.cs
using System;
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
        // Low-frequency reporting of suppressed log counts (debug only)
        private const int SuppressedLogReportInterval = 600; // 10s
        private static int lastSuppressedReportTick = 0;

        // ------------------------------
        // BuildRoof cooldown gate
        // Prevents repeated abort/requeue spam when roofing jobs are blocked by hardcore gating
        // Keying is by pawn ID -> textual target key (cell or job def) -> tick expiry.
        // Using string keys avoids allocations of complex structs in a hot path and keeps lookups fast.
        private static readonly Dictionary<int, Dictionary<string, int>> _buildRoofCooldowns = new Dictionary<int, Dictionary<string, int>>();
        // Cooldown window in ticks (5s at 60 TPS)
        private const int BuildRoofCooldownTicks = 5 * 60;

        /// <summary>
        /// Build a compact string key for a job target. Prefer target cell when available.
        /// </summary>
        private static string BuildRoofTargetKey(Job job)
        {
            if (job == null || job.def == null) return "job:unknown";
            try
            {
                if (job.targetA.IsValid)
                {
                    var c = job.targetA.Cell;
                    return $"cell:{c.x}:{c.y}:{c.z}:{job.def.defName}";
                }
            }
            catch { }
            return $"job:{job.def.defName}";
        }

        /// <summary>
        /// Returns true if the pawn+target is currently on cooldown. Cleans expired entries.
        /// </summary>
        private static bool IsBuildRoofOnCooldown(Pawn pawn, string key, int now)
        {
            if (pawn == null || key == null) return false;
            int pid = pawn.thingIDNumber;
            if (!_buildRoofCooldowns.TryGetValue(pid, out var inner)) return false;
            if (inner == null) { _buildRoofCooldowns.Remove(pid); return false; }
            if (inner.TryGetValue(key, out int expiry))
            {
                if (expiry > now) return true;
                // expired — remove
                inner.Remove(key);
                if (inner.Count == 0) _buildRoofCooldowns.Remove(pid);
            }
            return false;
        }

        /// <summary>
        /// Set a cooldown for pawn+target until now + BuildRoofCooldownTicks.
        /// </summary>
        private static void SetBuildRoofCooldown(Pawn pawn, string key, int now)
        {
            if (pawn == null || key == null) return;
            int pid = pawn.thingIDNumber;
            if (!_buildRoofCooldowns.TryGetValue(pid, out var inner))
            {
                inner = new Dictionary<string, int>();
                _buildRoofCooldowns[pid] = inner;
            }
            inner[key] = now + BuildRoofCooldownTicks;
        }

        /// <summary>
        /// Clear any cooldown entry for pawn+target (called when job succeeds or is validated ok).
        /// </summary>
        private static void ClearBuildRoofCooldown(Pawn pawn, string key)
        {
            if (pawn == null || key == null) return;
            int pid = pawn.thingIDNumber;
            if (_buildRoofCooldowns.TryGetValue(pid, out var inner) && inner != null)
            {
                inner.Remove(key);
                if (inner.Count == 0) _buildRoofCooldowns.Remove(pid);
            }
        }


        [HarmonyPrefix]
        [HarmonyPatch(nameof(Pawn_JobTracker.TryTakeOrderedJob))]
        [HarmonyPriority(Priority.Last)] // run after other mods
        public static bool Prefix_TryTakeOrderedJob(Pawn_JobTracker __instance, Job job, ref bool __result)
        {
            return CheckJobAssignment(__instance, job, setOrderedResult: ref __result);
        }

        /// <summary>
        /// Prefix for TryFindAndStartJob: intercept the thinker job selection so we can prevent
        /// StartJob from being called when gating determines the job should be blocked.
        /// Uses reflection to call the private DetermineNextJob(out ThinkTreeDef) method and
        /// inspect the returned ThinkResult.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("TryFindAndStartJob")]
        [HarmonyPriority(Priority.Last)]
        public static bool Prefix_TryFindAndStartJob(Pawn_JobTracker __instance)
        {
            if (__instance == null) return true;

            // Resolve pawn and settings defensively
            var pawn = (Pawn)PawnField.GetValue(__instance);
            var settings = SurvivalTools.Settings;
            if (pawn == null || settings == null) return true;
            if (!settings.hardcoreMode || !pawn.CanUseSurvivalTools()) return true;
            //if (Prefs.DevMode) return true; // don't interfere in dev mode

            try
            {
                // Call private DetermineNextJob(out ThinkTreeDef thinkTree) via reflection
                var jobTrackerType = typeof(Pawn_JobTracker);
                var determineMethod = AccessTools.Method(jobTrackerType, "DetermineNextJob");
                if (determineMethod == null) return true;

                object[] parameters = new object[] { null };
                var resultObj = determineMethod.Invoke(__instance, parameters);
                if (resultObj == null) return true;

                // resultObj is a ThinkResult
                var thinkResult = resultObj;
                // Use reflection to read properties IsValid and Job
                var isValidProp = thinkResult.GetType().GetProperty("IsValid");
                var jobProp = thinkResult.GetType().GetProperty("Job");
                if (isValidProp == null || jobProp == null) return true;

                bool isValid = (bool)isValidProp.GetValue(thinkResult);
                if (!isValid) return true;

                var nextJob = (Job)jobProp.GetValue(thinkResult);
                if (nextJob == null || nextJob.def == null) return true;

                // Determine relevant stats for this job and ask StatGatingHelper if any should block
                var requiredStats = SurvivalToolUtility.RelevantStatsFor(null, nextJob.def);
                if (requiredStats == null || requiredStats.Count == 0) return true;

                for (int i = 0; i < requiredStats.Count; i++)
                {
                    var stat = requiredStats[i];
                    if (stat == null) continue;

                    if (StatGatingHelper.ShouldBlockJobForStat(stat, settings, pawn))
                    {
                        try { ST_Logging.LogToolGateEvent(pawn, nextJob.def, stat, "missing required tool"); } catch { }

                        // Prevent TryFindAndStartJob from calling StartJob by returning false.
                        return false;
                    }
                }
            }
            catch
            {
                // Defensive: if reflection fails, allow normal behavior
                return true;
            }

            return true; // allow original method if not blocked
        }

        // NOTE: RimWorld 1.6 does not expose a Pawn_JobTracker.TryStartJob method to patch.
        // Gating is handled via TryTakeOrderedJob (prefix) and StartJob (postfix) above.

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

            // Report suppressed logs at low frequency (debug only)
            try { ReportSuppressedLogsIfDue(tick); } catch { }

            // Special-case: Ingest jobs sometimes include CleaningSpeed as a requirement.
            // We intentionally do NOT hard-abort jobs for CleaningSpeed or WorkSpeedGlobal
            // because those stats are penalty-based only (see StatPart_SurvivalTool).
            // This check ensures we don't incorrectly abort Ingest or similar jobs.
            if (newJob.def == JobDefOf.Ingest &&
                StatGatingHelper.ShouldBlockJobForStat(ST_StatDefOf.CleaningSpeed, settings, pawn))
            {
                // Log via deduped tool-gate logger so repeated denials are suppressed
                try
                {
                    LogToolGateEvent(pawn, JobDefOf.Ingest, ST_StatDefOf.CleaningSpeed, "missing cleaning tool (ingest)");
                }
                catch { }

                // Do NOT abort the job here; StatPart will apply the penalty and the job may proceed.
                return;
            }

            // Normal gating: check stats from WorkGiver/job defs
            var requiredStats = SurvivalToolUtility.RelevantStatsFor(null, newJob.def);
            if (requiredStats.NullOrEmpty()) return;

            for (int i = 0; i < requiredStats.Count; i++)
            {
                var stat = requiredStats[i];
                if (stat == null) continue;

                // Use centralized gating helper which knows about optional stats and exceptions
                if (StatGatingHelper.ShouldBlockJobForStat(stat, settings, pawn))
                {
                    // Special handling for BuildRoof/RoofJob to avoid repeated abort/requeue spam
                    var jobDefName = newJob.def?.defName;
                    bool isRoofJob = jobDefName == "BuildRoof" || jobDefName == "RoofJob" || (jobDefName != null && jobDefName.IndexOf("buildroof", StringComparison.OrdinalIgnoreCase) >= 0);

                    string brKey = null;
                    if (isRoofJob)
                    {
                        brKey = BuildRoofTargetKey(newJob);
                        // If we're on cooldown for this pawn+target, silently skip abort to avoid thrash
                        if (IsBuildRoofOnCooldown(pawn, brKey, tick))
                            return;
                    }

                    if (IsDebugLoggingEnabled)
                    {
                        var logKey = isRoofJob ? $"JobBlock_BuildRoof_{pawn.ThingID}_{brKey}" : $"JobBlock_{pawn.ThingID}_{newJob.def.defName}_{stat.defName}";
                        if (ShouldLogWithCooldown(logKey))
                        {
                            try { ST_Logging.LogToolGateEvent(pawn, newJob.def, stat, "missing required tool"); } catch { }
                        }
                        else
                        {
                            suppressedLogCounts[logKey] = suppressedLogCounts.TryGetValue(logKey, out int c) ? c + 1 : 1;
                        }
                    }

                    // Record guard BEFORE ending job to prevent immediate same-tick loops
                    lastAbortTickByPawn[id] = tick;

                    // Set per-pawn+target cooldown for roofing jobs
                    if (isRoofJob)
                        SetBuildRoofCooldown(pawn, brKey, tick);

                    // CRITICAL CHANGE: do NOT request immediate requeue here.
                    // Let vanilla re-evaluate on the next normal opportunity.
                    try
                    {
                        // End current job forcefully so work ticks won't continue
                        if (__instance != null)
                            __instance.EndCurrentJob(JobCondition.Incompletable, startNewJob: false, canReturnToPool: true);
                        if (pawn?.jobs != null)
                            pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    }
                    catch { }

                    break;
                }
            }
        }

        /// <summary>
        /// When a job ends successfully, clear any BuildRoof cooldown for that pawn+target so
        /// future valid attempts are allowed immediately.
        /// This is a Prefix so we can inspect the current job before it's cleared by EndCurrentJob.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("EndCurrentJob")]
        [HarmonyPriority(Priority.Last)]
        public static void Prefix_EndCurrentJob(Pawn_JobTracker __instance, JobCondition condition)
        {
            try
            {
                if (__instance == null) return;
                var pawn = (Pawn)PawnField.GetValue(__instance);
                if (pawn == null) return;

                // Only care about successful completion
                if (condition != JobCondition.Succeeded)
                    return;

                var job = pawn.jobs?.curJob;
                if (job == null) return;

                var name = job.def?.defName;
                if (name == null) return;

                bool isRoofJob = name == "BuildRoof" || name == "RoofJob" || (name.IndexOf("buildroof", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!isRoofJob) return;

                var key = BuildRoofTargetKey(job);
                ClearBuildRoofCooldown(pawn, key);
            }
            catch
            {
                // Defensive: do not let cleanup fail the game
            }
        }


        private static bool CheckJobAssignment(Pawn_JobTracker jobTracker, Job job, ref bool setOrderedResult)
        {
            // Resolve pawn safely (cached FieldInfo)
            var pawn = jobTracker != null ? (Pawn)PawnField.GetValue(jobTracker) : null;
            var settings = SurvivalTools.Settings;

            // Only gate in hardcore mode, with a valid pawn that can use tools, and a valid job def
            // Also skip gating when developer/debug mode is enabled to avoid interfering with testing.
            if (settings == null || settings.hardcoreMode != true || pawn == null || !pawn.CanUseSurvivalTools() || job?.def == null || Prefs.DevMode)
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

                if (StatGatingHelper.ShouldBlockJobForStat(stat, settings, pawn))
                {
                    // Cooldown the log to avoid spam
                    if (IsDebugLoggingEnabled)
                    {
                        try { ST_Logging.LogToolGateEvent(pawn, job.def, stat, "missing required tool"); } catch { }
                    }

                    // Immediately cancel the job so it cannot continue working ticks.
                    try
                    {
                        // If we have a jobTracker instance, end the current job as Incompletable to force-drop it.
                        jobTracker?.EndCurrentJob(JobCondition.Incompletable);
                        var pawnObj = pawn;
                        if (pawnObj?.jobs != null)
                            pawnObj.jobs.EndCurrentJob(JobCondition.Incompletable);
                    }
                    catch { }

                    // Tell TryTakeOrderedJob we handled it (fail) and skip original
                    setOrderedResult = false;
                    return false;
                }
            }

            return true; // allow original method
        }

        /// <summary>
        /// Emits a low-frequency debug summary of suppressed log counts, then clears them.
        /// Keeps overhead minimal by only running when debug logging is enabled and at a low tick interval.
        /// </summary>
        private static void ReportSuppressedLogsIfDue(int now)
        {
            if (!IsDebugLoggingEnabled) return;
            if (now - lastSuppressedReportTick < SuppressedLogReportInterval) return;
            lastSuppressedReportTick = now;

            try
            {
                if (suppressedLogCounts == null || suppressedLogCounts.Count == 0) return;

                // Build a compact summary
                var sb = new System.Text.StringBuilder();
                sb.Append("[SurvivalTools.JobBlock] Suppressed abort counts:");
                int total = 0;
                foreach (var kv in suppressedLogCounts)
                {
                    sb.Append(' ').Append(kv.Key).Append(':').Append(kv.Value).Append(';');
                    total += kv.Value;
                }

                LogDecision("JobBlock_SuppressedSummary", sb.ToString());

                // Clear counts after reporting
                suppressedLogCounts.Clear();
            }
            catch
            {
                // swallow
            }
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
