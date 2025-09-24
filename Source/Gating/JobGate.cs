// RimWorld 1.6 / C# 7.3
// Source/Gating/JobGate.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using SurvivalTools.Assign;
// using SurvivalTools.Helpers; // already included above
using SurvivalTools.Compat;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Gating
{
    public static class JobGate
    {
        // micro-cache: WG/Job -> required stats (resolved once)
        static readonly Dictionary<WorkGiverDef, StatDef[]> _wgReq = new Dictionary<WorkGiverDef, StatDef[]>(64);
        static readonly Dictionary<JobDef, StatDef[]> _jobReq = new Dictionary<JobDef, StatDef[]>(128);

        // epsilon to avoid float jitter
        const float Eps = 0.001f;

        // hot path: LINQ-free
        public static bool ShouldBlock(Pawn pawn, WorkGiverDef wg, JobDef job, bool forced, out string reasonKey, out string a1, out string a2)
        {
            reasonKey = null; a1 = null; a2 = null;

            // Early-outs (do not gate these)
            if (pawn == null || pawn.Dead || pawn.Downed || pawn.IsPrisoner || pawn.RaceProps == null || pawn.RaceProps.Animal || pawn.RaceProps.IsMechanoid)
                return false;

            var settings = SurvivalTools.Settings;
            if (settings == null || (!settings.hardcoreMode && !settings.extraHardcoreMode)) return false; // Normal never blocks here

            // If this WorkGiver's work type is disabled or not active for the pawn and this is not a forced action,
            // do not attempt rescue or gating. Avoids churn for work the pawn wouldn't do anyway.
            if (wg?.workType != null && !forced)
            {
                var ws = pawn.workSettings;
                if (pawn.WorkTypeIsDisabled(wg.workType) || (ws != null && !ws.WorkIsActive(wg.workType)))
                {
                    if (IsDebugLoggingEnabled)
                    {
                        LogDebug($"[JobGate] Skipping gate/rescue for disabled or inactive work type {wg.workType.defName} on {pawn.LabelShort}", $"JobGate_SkipInactive_{pawn.ThingID}|{wg.workType.defName}");
                    }
                    return false;
                }
            }

            // Phase 8+: Exempt pure delivery WorkGivers (resource hauling to blueprints/frames/variants) from gating & rescue.
            if (wg != null && IsPureDeliveryWorkGiver(wg))
                return false;
            // Resolve required stats once
            var requiredStatsPre = ResolveRequiredStats(wg, job);
            // If no stats, do not gate
            if (requiredStatsPre == null || requiredStatsPre.Length == 0) return false;

            // PHASE 6 INTEGRATION: enforce rescue-first flow
            bool hasAllToolsPre = true;
            for (int i = 0; i < requiredStatsPre.Length; i++)
            {
                var stat = requiredStatsPre[i];
                float bestScore;
                var best = Scoring.ToolScoring.GetBestTool(pawn, stat, out bestScore);
                var baseline = SurvivalToolUtility.GetNoToolBaseline(stat);
                if (best == null || bestScore <= baseline + Eps) { hasAllToolsPre = false; break; }
            }

            if (!hasAllToolsPre)
            {
                bool anyQueued = false;
                for (int i = 0; i < requiredStatsPre.Length; i++)
                {
                    var stat = requiredStatsPre[i];
                    anyQueued |= AssignmentSearch.TryUpgradeFor(pawn, stat, 0.001f, 30f, 1000, AssignmentSearch.QueuePriority.Front, $"JobGate({wg?.defName ?? job?.defName}){(forced ? ":forced" : string.Empty)}");
                }

                if (anyQueued)
                {
                    // Allow the job to exist: PreWork hooks will requeue ordered jobs; StartJob hook will requeue AI jobs
                    if (IsDebugLoggingEnabled)
                    {
                        LogDebug($"[JobGate.Phase6] Queued rescue for {pawn.LabelShort} (forced={forced}). Allowing job so requeue hooks can preserve it.", $"JobGate_RescueAllow_{pawn.ThingID}");
                    }
                    return false; // Do not block; preserve the job/menu option
                }
            }

            // Hardcore/Nightmare block only if missing a required tool for ANY required stat
            var req = requiredStatsPre; // Already resolved

            for (int i = 0; i < req.Length; i++)
            {
                var stat = req[i];
                float bestScore;
                var best = Scoring.ToolScoring.GetBestTool(pawn, stat, out bestScore);
                // Compare against toolless baseline from resolver
                var baseline = SurvivalToolUtility.GetNoToolBaseline(stat);
                if (best == null || bestScore <= baseline + Eps)
                {
                    // If we arrive here, either rescue was disabled or we couldn't queue any rescue. Provide standard reason.
                    reasonKey = "ST_Gate_MissingToolStat"; // "Requires a tool for {0} to do {1}."
                    a1 = stat?.label ?? "work";
                    a2 = wg?.label ?? job?.label ?? "this job";
                    return true;
                }
            }
            return false;
        }

        static StatDef[] ResolveRequiredStats(WorkGiverDef wg, JobDef job)
        {
            // prefer WG binding; fall back to Job binding
            if (wg != null)
            {
                StatDef[] arr;
                if (_wgReq.TryGetValue(wg, out arr)) return arr;

                // Use helper directly (CompatAPI forwarder is obsolete)
                var statsList = StatGatingHelper.GetStatsForWorkGiver(wg);
                arr = statsList?.ToArray() ?? new StatDef[0];

                // DEBUG: Log what stats we found for this WorkGiver
                if (IsDebugLoggingEnabled)
                {
                    LogDebug($"[JobGate.ResolveStats] WorkGiver {wg.defName}: found {arr.Length} stats via resolver: {string.Join(", ", arr.Select(s => s.defName))}", $"JobGate_ResolveStats_{wg.defName}");
                }

                _wgReq[wg] = arr;
                return _wgReq[wg];
            }
            if (job != null)
            {
                StatDef[] arr;
                if (_jobReq.TryGetValue(job, out arr)) return arr;

                // PHASE 6 INTEGRATION: Use SurvivalToolUtility for job stats
                var statsList = SurvivalToolUtility.StatsForJob(job);
                arr = statsList?.ToArray() ?? new StatDef[0];

                _jobReq[job] = arr;
                return _jobReq[job];
            }
            return new StatDef[0];
        }

        // Optional: call when resolver rebuilds or settings change
        public static void ClearCaches()
        {
            _wgReq.Clear();
            _jobReq.Clear();
        }

        /// <summary>
        /// Returns true if this WorkGiver is ONLY about delivering construction resources (no actual building work).
        /// Covers vanilla + typo variants + modded prefixes (DeliverResources* / ConstructDeliverResources*).
        /// Safe to call hot path (string checks only).
        /// </summary>
        public static bool IsPureDeliveryWorkGiver(WorkGiverDef wg)
        {
            if (wg == null) return false;
            var dn = wg.defName;
            if (string.IsNullOrEmpty(dn)) return false;
            // Normalize once
            dn = dn.ToLowerInvariant();
            // Common vanilla / mod variants
            // "constructdeliverresources...", including frames / blueprints / typo blueprints
            if (dn.StartsWith("constructdeliverresources")) return true;
            // Generic mod pattern: DeliverResourcesToFrames / DeliverResourcesToBlueprints
            if (dn.StartsWith("deliverresources")) return true;
            // Defensive: explicit contains checks (cheap) for mid-string naming styles
            if (dn.Contains("deliverresources") && (dn.Contains("frame") || dn.Contains("blueprint"))) return true;
            return false;
        }
    }
}

// -------------------------------------------------------------------------
// Strangler Pattern Kill List (Phase 5):
// - Remove now: (no deletes yet; comments only)
// - Old JobTracker reflection gate (remove after WG gates proven stable)
// - Any per-job "missing tool" checks outside this path
// - Patch_Pawn_JobTracker_ExtraHardcore reflection fallback
//
// Rationale:
// - WorkGiver_Scanner patches provide authoritative blocking at the right level
// - Eliminates scattered per-job gating logic in favor of unified JobGate
// -------------------------------------------------------------------------