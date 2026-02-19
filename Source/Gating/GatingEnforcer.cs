// RimWorld 1.6 / C# 7.3
// Source/Gating/GatingEnforcer.cs
//
// Enforces tool gating by canceling now-invalid jobs on mode changes and save loads
// - Cancels current job if blocked in new mode
// - Prunes queued jobs that would be blocked
// - Allocation-free hot loops for performance
// - Throttled to avoid spam

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Gating
{
    public enum ST_CancelReason
    {
        ST_Gate_MissingToolStat,
        ST_Gate_Internal
    }

    public static class GatingEnforcer
    {
        // Throttle so we don't spam in the same tick
        private static int _lastRunTick = -999999;

        public static int EnforceAllRunningJobs(bool fromSettingsChange, string reasonKey = "ST_Gate_ModeChanged")
        {
            // Early exit if no game is active (e.g., on main menu or during load)
            if (Current.Game == null || Current.ProgramState != ProgramState.Playing)
                return 0;

            var settings = SurvivalToolsMod.Settings;
            if (settings == null) return 0;

            var mode = settings.CurrentMode;
            if (mode == DifficultyMode.Normal) return 0;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now == _lastRunTick) return 0;
            _lastRunTick = now;

            int cancelled = 0;
            var maps = Find.Maps;
            if (maps == null) return 0;

            // Iterate maps without LINQ (allocation-free)
            for (int mi = 0; mi < maps.Count; mi++)
            {
                var map = maps[mi];
                if (map?.mapPawns?.FreeColonistsSpawned == null) continue;

                var pawns = map.mapPawns.FreeColonistsSpawned; // only colonists; skip animals/mechs/guests
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (p == null || p.Dead || p.Downed) continue;

                    if (IsDebugLoggingEnabled)
                    {
                        LogDebug($"[GatingEnforcer] Scan {p.LabelShort} curJob={p.jobs?.curJob?.def?.defName ?? "(none)"} queueCount={p.jobs?.jobQueue?.Count ?? 0}", $"GatingEnforcer.Scan|{p.ThingID}");
                        LogJobQueueSummary(p, "GatingEnforcer.Before");
                    }

                    // Cancel current job if blocked
                    cancelled += CancelIfBlocked(p, reasonKey);

                    // Prune queued jobs that would be blocked
                    cancelled += PruneQueue(p, reasonKey);

                    if (IsDebugLoggingEnabled)
                    {
                        LogJobQueueSummary(p, "GatingEnforcer.After");
                    }
                }
            }
            return cancelled;
        }

        /// <summary>
        /// Cancel the current job on the pawn if it matches the provided job (or unconditionally if job null) using a standard gating pathway.
        /// Safe no-op if pawn/job invalid. Returns true if a job was cancelled.
        /// </summary>
        public static bool CancelCurrentJob(Pawn pawn, Job job, ST_CancelReason reason = ST_CancelReason.ST_Gate_MissingToolStat)
        {
            try
            {
                if (pawn?.jobs?.curJob == null) return false;
                if (job != null && pawn.jobs.curJob != job) return false;
                var cur = pawn.jobs.curJob;
                if (Prefs.DevMode && IsDebugLoggingEnabled)
                {
                    LogDebug($"[GatingEnforcer] Cancel current job {cur.def.defName} for {pawn.LabelShort} due to {reason}", $"GatingEnforcer.CancelCur|{pawn.ThingID}|{cur.def.defName}|{reason}");
                }
                pawn.jobs.EndCurrentJob(JobCondition.Incompletable, startNewJob: true);
                return true;
            }
            catch { return false; }
        }

        private static int CancelIfBlocked(Pawn pawn, string reasonKey)
        {
            var jobs = pawn.jobs;
            if (jobs == null) return 0;

            var cur = jobs.curJob;
            if (cur == null) return 0;

            // Hard gating scope: only player-controlled humanlikes with tool-using jobs.
            try
            {
                if (!SurvivalTools.Helpers.PawnEligibility.IsEligibleColonistHuman(pawn))
                    return 0;
                if (cur.def == JobDefOf.Ingest) return 0; // never interfere with eating
                // Fast tool-using job check (mirrors PreWork JobUsesTools heuristic)
                if (!LikelyJobUsesTools(cur)) return 0;
            }
            catch { }

            // Use exact JobGate logic
            if (JobGate.ShouldBlock(pawn, null, cur.def, forced: false, out var k, out var a1, out var a2))
            {
                // End current job; choose a non-spam condition
                if (Prefs.DevMode && IsDebugLoggingEnabled)
                    LogDebug($"[GatingEnforcer] Cancel current job {cur.def.defName} for {pawn.LabelShort} due to {k}", $"GatingEnforcer.CancelCur|{pawn.ThingID}|{cur.def.defName}");
                jobs.EndCurrentJob(JobCondition.Incompletable, startNewJob: true);
                return 1;
            }
            return 0;
        }

        private static int PruneQueue(Pawn pawn, string reasonKey)
        {
            var jobs = pawn.jobs;
            if (jobs == null) return 0;

            var q = jobs.jobQueue;
            if (q == null || q.Count == 0) return 0;

            int removed = 0;
            // Create a list of jobs to remove (avoid modifying while iterating)
            var toRemove = new List<Job>();

            for (int idx = 0; idx < q.Count; idx++)
            {
                var item = q[idx];
                if (item?.job == null) continue;

                try
                {
                    if (!SurvivalTools.Helpers.PawnEligibility.IsEligibleColonistHuman(pawn))
                        continue;
                    if (item.job.def == JobDefOf.Ingest) continue;
                    if (!LikelyJobUsesTools(item.job)) continue;
                }
                catch { }

                // Use exact JobGate logic
                if (JobGate.ShouldBlock(pawn, null, item.job.def, forced: false, out var k, out var a1, out var a2))
                {
                    toRemove.Add(item.job);
                }
            }

            // Remove blocked jobs using the job queue's dequeue method
            for (int i = 0; i < toRemove.Count; i++)
            {
                var jobToRemove = toRemove[i];
                // Find and remove the job from queue
                for (int qIdx = q.Count - 1; qIdx >= 0; qIdx--)
                {
                    if (q[qIdx]?.job == jobToRemove)
                    {
                        var removedItem = q[qIdx];
                        // Use reflection-safe removal via list manipulation
                        var queueList = q as System.Collections.IList;
                        if (queueList != null)
                        {
                            queueList.RemoveAt(qIdx);
                            removed++;
                            if (Prefs.DevMode && IsDebugLoggingEnabled)
                                LogDebug($"[GatingEnforcer] Pruned queued job {removedItem.job?.def?.defName ?? "(null)"} for {pawn.LabelShort}", $"GatingEnforcer.Prune|{pawn.ThingID}|{removedItem.job?.def?.defName}");
                            break;
                        }
                    }
                }
            }

            return removed;
        }

        // Lightweight heuristic replicate of PreWork.JobUsesTools without allocations.
        private static bool LikelyJobUsesTools(Job job)
        {
            if (job == null) return false; var jd = job.def; if (jd == null) return false;
            if (jd == JobDefOf.Ingest || jd == JobDefOf.LayDown || jd == JobDefOf.Wait || jd == JobDefOf.Wait_MaintainPosture || jd == JobDefOf.Goto || jd == JobDefOf.GotoWander)
                return false;
            try
            {
                var wg = SurvivalTools.Helpers.JobDefToWorkGiverDefHelper.GetWorkGiverDefForJob(jd);
                var statsJob = SurvivalToolUtility.RelevantStatsFor(wg, job);
                if (statsJob != null && statsJob.Count > 0) return true;
                var statsDef = SurvivalToolUtility.RelevantStatsFor(wg, jd);
                return statsDef != null && statsDef.Count > 0;
            }
            catch { return true; }
        }
    }
}