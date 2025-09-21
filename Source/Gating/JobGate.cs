// RimWorld 1.6 / C# 7.3
// Source/Gating/JobGate.cs
using System;
using System.Collections.Generic;
using Verse;
using RimWorld;

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

            // Hardcore/Nightmare block only if missing a required tool for ANY required stat
            var req = ResolveRequiredStats(wg, job);
            if (req == null || req.Length == 0) return false; // nothing to gate

            for (int i = 0; i < req.Length; i++)
            {
                var stat = req[i];
                float bestScore;
                var best = Scoring.ToolScoring.GetBestTool(pawn, stat, out bestScore);
                // Compare against toolless baseline from resolver
                var baseline = SurvivalToolUtility.GetNoToolBaseline(stat);
                if (best == null || bestScore <= baseline + Eps)
                {
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
                arr = Compat.CompatAPI.GetRequiredStatsFor(wg); // add this tiny getter if you don't have it: returns StatDef[] or empty
                _wgReq[wg] = arr ?? new StatDef[0];
                return _wgReq[wg];
            }
            if (job != null)
            {
                StatDef[] arr;
                if (_jobReq.TryGetValue(job, out arr)) return arr;
                arr = Compat.CompatAPI.GetRequiredStatsFor(job);
                _jobReq[job] = arr ?? new StatDef[0];
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