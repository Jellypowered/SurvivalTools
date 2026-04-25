// RimWorld 1.6 / C# 7.3
// Source/Gating/JobGate.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
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

        // Helper to format WG/Job context
        static string Ctx(WorkGiverDef wg, JobDef job)
        {
            return wg != null ? $"WG:{wg.defName}" : (job != null ? $"Job:{job.defName}" : "<none>");
        }

        // One-line decision logger to make outcomes obvious in logs
        static void LogDecisionLine(Pawn pawn, WorkGiverDef wg, JobDef job, bool forced, bool blocked, string reason, StatDef statForDetail = null, float bestScore = -1f, float baseline = -1f)
        {
            if (!IsGatingLoggingEnabled) return;
            string outcome = blocked ? "BLOCK" : "ALLOW";
            string ctx = Ctx(wg, job);
            string pawnTag = pawn?.LabelShort ?? "<null>";
            string extra = string.Empty;
            if (statForDetail != null && bestScore >= 0f && baseline >= 0f)
            {
                extra = $" | stat={statForDetail.defName} best={bestScore:0.###} baseline={baseline:0.###}";
            }
            LogDebug($"[JobGate] Decision: {outcome} | pawn={pawnTag} | ctx={ctx} | forced={forced} | reason={reason}{extra}", $"JobGate.Decision|{pawn?.ThingID}|{wg?.defName ?? job?.defName}|{outcome}|{reason}");
        }

        // hot path: LINQ-free
        // Overload: accepts Job instance for target-aware stat resolution (trees vs plants)
        // queryOnly=true: skip TryUpgradeFor side effects (use when building UI/menus, not executing jobs)
        public static bool ShouldBlock(Pawn pawn, WorkGiverDef wg, Job jobInstance, bool forced, out string reasonKey, out string a1, out string a2, bool queryOnly = false)
        {
            return ShouldBlockInternal(pawn, wg, jobInstance?.def, jobInstance, forced, out reasonKey, out a1, out a2, queryOnly);
        }

        // Original: accepts JobDef for backward compatibility
        public static bool ShouldBlock(Pawn pawn, WorkGiverDef wg, JobDef job, bool forced, out string reasonKey, out string a1, out string a2, bool queryOnly = false)
        {
            return ShouldBlockInternal(pawn, wg, job, null, forced, out reasonKey, out a1, out a2, queryOnly);
        }

        private static bool ShouldBlockInternal(Pawn pawn, WorkGiverDef wg, JobDef job, Job jobInstance, bool forced, out string reasonKey, out string a1, out string a2, bool queryOnly = false)
        {
            reasonKey = null; a1 = null; a2 = null;

            // Trace entry to make every ShouldBlock invocation observable
            if (IsGatingLoggingEnabled)
            {
                LogDebug($"[JobGate.Enter] pawn={pawn?.LabelShort ?? "<null>"} ctx={Ctx(wg, job)} forced={forced} queryOnly={queryOnly}", $"JobGate.Enter|{pawn?.ThingID}|{wg?.defName ?? job?.defName}");
            }

            // Early-outs (do not gate these)
            if (pawn == null || pawn.Dead || pawn.Downed || pawn.IsPrisoner || pawn.RaceProps == null || pawn.RaceProps.Animal || PawnToolValidator.IsMechanoidOrInherited(pawn))
            {
                LogDecisionLine(pawn, wg, job, forced, blocked: false, reason: "PawnNotEligible_Early");
                return false;
            }

            // Hard scope: only player-controlled humanlikes & tool-using jobs.
            if (!SurvivalTools.Helpers.PawnEligibility.IsEligibleColonistHuman(pawn))
            {
                LogDecisionLine(pawn, wg, job, forced, blocked: false, reason: "NotEligibleColonistHuman");
                return false;
            }
            if (job == JobDefOf.Ingest)
            {
                LogDecisionLine(pawn, wg, job, forced, blocked: false, reason: "IngestJob");
                return false;
            }
            if (!JobLikelyUsesTools(wg, job))
            {
                LogDecisionLine(pawn, wg, job, forced, blocked: false, reason: "JobLikelyUsesTools=false");
                return false;
            }

            var settings = SurvivalToolsMod.Settings;
            if (settings == null || (!settings.hardcoreMode && !settings.extraHardcoreMode))
            {
                LogDecisionLine(pawn, wg, job, forced, blocked: false, reason: "NotHardcoreMode");
                return false; // Normal never blocks here
            }

            // If this WorkGiver's work type is disabled or not active for the pawn and this is not a forced action,
            // do not attempt rescue or gating. Avoids churn for work the pawn wouldn't do anyway.
            if (wg?.workType != null && !forced)
            {
                var ws = pawn.workSettings;
                if (pawn.WorkTypeIsDisabled(wg.workType) || (ws != null && !ws.WorkIsActive(wg.workType)))
                {
                    LogDecisionLine(pawn, wg, job, forced, blocked: false, reason: $"WorkTypeInactiveOrDisabled:{wg.workType.defName}");
                    return false;
                }
            }

            // Phase 8+: Exempt pure delivery WorkGivers (resource hauling to blueprints/frames/variants) from gating & rescue.
            if (wg != null && IsPureDeliveryWorkGiver(wg))
            {
                LogDecisionLine(pawn, wg, job, forced, blocked: false, reason: "PureDeliveryWorkGiver");
                return false;
            }
            // Resolve declared stats once (may include optional ones like MiningYieldDigging)
            // Pass jobInstance for target-aware resolution (trees vs plants)
            var declaredStats = ResolveRequiredStats(wg, job, jobInstance);
            // If no stats, do not gate
            if (declaredStats == null || declaredStats.Length == 0)
            {
                LogDecisionLine(pawn, wg, job, forced, blocked: false, reason: "NoDeclaredStats");
                return false;
            }
            if (IsGatingLoggingEnabled)
            {
                LogDebug($"[JobGate.Stats] pawn={pawn.LabelShort} ctx={Ctx(wg, job)} declared=[{string.Join(",", declaredStats.Select(s => s.defName))}]", $"JobGate.Stats|{pawn.ThingID}|{wg?.defName ?? job?.defName}");
            }

            // Filter to only the stats that should HARD-block under current settings (skip optional stats)
            // This ensures MiningYieldDigging and similar bonus stats don't prevent work from starting.
            var requiredStatsList = new List<StatDef>(declaredStats.Length);
            for (int i = 0; i < declaredStats.Length; i++)
            {
                var s = declaredStats[i];
                if (StatGatingHelper.ShouldBlockJobForStat(s, settings, pawn))
                    requiredStatsList.Add(s);
            }
            var requiredStatsPre = requiredStatsList.ToArray();
            if (requiredStatsPre.Length == 0)
            {
                if (IsGatingLoggingEnabled)
                {
                    LogDebug($"[JobGate.Filter] pawn={pawn.LabelShort} ctx={Ctx(wg, job)} declared=[{string.Join(",", declaredStats.Select(s => s.defName))}] all filtered out by ShouldBlockJobForStat", $"JobGate.FilterEmpty|{pawn.ThingID}|{wg?.defName ?? job?.defName}");
                }
                LogDecisionLine(pawn, wg, job, forced, blocked: false, reason: "NoRequiredStats_AfterFilter");
                return false;
            }
            if (IsGatingLoggingEnabled)
            {
                LogDebug($"[JobGate.Required] pawn={pawn.LabelShort} ctx={Ctx(wg, job)} required=[{string.Join(",", requiredStatsPre.Select(s => s.defName))}]", $"JobGate.Required|{pawn.ThingID}|{wg?.defName ?? job?.defName}");
            }

            // PHASE 6 INTEGRATION: enforce rescue-first flow
            bool hasAllToolsPre = true;
            for (int i = 0; i < requiredStatsPre.Length; i++)
            {
                var stat = requiredStatsPre[i];
                float bestScore;
                var best = Scoring.ToolScoring.GetBestTool(pawn, stat, out bestScore);
                var baseline = SurvivalToolUtility.GetToolValidationBaseline(stat);
                if (IsGatingLoggingEnabled)
                {
                    LogDebug($"[JobGate.PreCheck] pawn={pawn.LabelShort} ctx={Ctx(wg, job)} stat={stat.defName} best={(best?.LabelShort ?? "<null>")} score={bestScore:0.###} baseline={baseline:0.###}", $"JobGate.PreCheck|{pawn.ThingID}|{wg?.defName ?? job?.defName}|{stat.defName}");
                }
                if (best == null || bestScore <= baseline + Eps) { hasAllToolsPre = false; break; }
            }
            if (IsGatingLoggingEnabled)
            {
                LogDebug($"[JobGate.PreSummary] pawn={pawn.LabelShort} ctx={Ctx(wg, job)} hasAllToolsPre={hasAllToolsPre}", $"JobGate.PreSummary|{pawn.ThingID}|{wg?.defName ?? job?.defName}");
            }

            if (!hasAllToolsPre)
            {
                // NIGHTMARE STRICT: Do not allow rescue bypass if still over carry limit
                try
                {
                    if (SurvivalToolsMod.Settings?.extraHardcoreMode == true)
                    {
                        int allowedNm = AssignmentSearch.GetEffectiveCarryLimit(pawn, SurvivalToolsMod.Settings);
                        if (!NightmareCarryEnforcer.IsCompliant(pawn, null, allowedNm))
                        {
                            reasonKey = "ST_Gate_MissingToolStat"; // reuse generic gating key
                            a1 = requiredStatsPre[0]?.label ?? "work";
                            a2 = wg?.label ?? job?.label ?? "this job";
                            LogDecisionLine(pawn, wg, job, forced, blocked: true, reason: "NightmareCarryExceeded");
                            return true;
                        }
                    }
                }
                catch { }
                // If acquisition is already pending/queued, keep blocking gated work until equip/take starts.
                // Allowing work here can let pawns continue the gated job with no tool actually in hand.
                if (AssignmentSearch.HasAcquisitionPendingOrQueued(pawn))
                {
                    reasonKey = "ST_Gate_MissingToolStat";
                    a1 = requiredStatsPre[0]?.label ?? "work";
                    a2 = wg?.label ?? job?.label ?? "this job";
                    LogDecisionLine(pawn, wg, job, forced, blocked: true, reason: "RescueQueued_WaitingForAcquisition");
                    return true;
                }
                // queryOnly: skip TryUpgradeFor when building menus/UI — it does a map-wide
                // pathfinding search and queues jobs, causing ~500-1000ms freezes on every right-click.
                // Right-click rescue uses queryOnly=true and calls TryUpgradeFor only when the
                // user actually clicks the rescue option (in ExecuteRescue).
                if (!queryOnly)
                {
                bool anyQueued = false;
                for (int i = 0; i < requiredStatsPre.Length; i++)
                {
                    var stat = requiredStatsPre[i];
                    anyQueued |= AssignmentSearch.TryUpgradeFor(pawn, stat, 0.001f, 30f, 1000, AssignmentSearch.QueuePriority.Front, $"JobGate({wg?.defName ?? job?.defName}){(forced ? ":forced" : string.Empty)}");
                }

                if (anyQueued)
                {
                    // Keep blocking until the pawn actually starts acquisition/equip.
                    // This prevents gated jobs from running toolless while a rescue sits in queue.
                    if (AssignmentSearch.HasAcquisitionPendingOrQueued(pawn))
                    {
                        if (SurvivalToolsMod.Settings?.extraHardcoreMode == true)
                        {
                            // Nightmare: still require compliance, do not allow just because acquisition is queued
                            int allowedNm2 = AssignmentSearch.GetEffectiveCarryLimit(pawn, SurvivalToolsMod.Settings);
                            if (!NightmareCarryEnforcer.IsCompliant(pawn, null, allowedNm2))
                            {
                                reasonKey = "ST_Gate_MissingToolStat";
                                a1 = requiredStatsPre[0]?.label ?? "work";
                                a2 = wg?.label ?? job?.label ?? "this job";
                                LogDecisionLine(pawn, wg, job, forced, blocked: true, reason: "NightmareCarryExceeded_PostQueue");
                                return true;
                            }
                        }

                        reasonKey = "ST_Gate_MissingToolStat";
                        a1 = requiredStatsPre[0]?.label ?? "work";
                        a2 = wg?.label ?? job?.label ?? "this job";
                        LogDecisionLine(pawn, wg, job, forced, blocked: true, reason: "RescueQueued_WaitingForAcquisition");
                        return true;
                    }

                    // Block now to ensure we don't start the gated job before we’re ready
                    if (IsGatingLoggingEnabled)
                    {
                        LogDebug($"[JobGate.Phase6] Rescue queued but only drop/dequeue pending for {pawn.LabelShort}; blocking job until acquisition starts.", $"JobGate_RescueBlock_{pawn.ThingID}");
                        LogJobQueueSummary(pawn, "JobGate.RescueBlock");
                    }
                    reasonKey = "ST_Gate_MissingToolStat";
                    a1 = requiredStatsPre[0]?.label ?? "work";
                    a2 = wg?.label ?? job?.label ?? "this job";
                    LogDecisionLine(pawn, wg, job, forced, blocked: true, reason: "RescueQueued_WaitingForAcquisition");
                    return true;
                }
                } // end !queryOnly
            }

            // Hardcore/Nightmare block only if missing a required tool for ANY required stat
            var req = requiredStatsPre; // Already resolved

            for (int i = 0; i < req.Length; i++)
            {
                var stat = req[i];
                float bestScore;
                var best = Scoring.ToolScoring.GetBestTool(pawn, stat, out bestScore);
                // Compare against toolless baseline from resolver
                var baseline = SurvivalToolUtility.GetToolValidationBaseline(stat);
                if (IsGatingLoggingEnabled)
                {
                    LogDebug($"[JobGate.FinalCheck] pawn={pawn.LabelShort} ctx={Ctx(wg, job)} stat={stat.defName} best={(best?.LabelShort ?? "<null>")} score={bestScore:0.###} baseline={baseline:0.###} willBlock={(best == null || bestScore <= baseline + Eps)}", $"JobGate.FinalCheck|{pawn.ThingID}|{wg?.defName ?? job?.defName}|{stat.defName}");
                }
                if (best == null || bestScore <= baseline + Eps)
                {
                    // If we arrive here, either rescue was disabled or we couldn't queue any rescue. Provide standard reason.
                    reasonKey = "ST_Gate_MissingToolStat"; // "Requires a tool for {0} to do {1}."
                    a1 = stat?.label ?? "work";
                    a2 = wg?.label ?? job?.label ?? "this job";
                    if (IsGatingLoggingEnabled)
                    {
                        LogDebug($"[JobGate] Blocking: missing tool for {stat?.defName} on {pawn.LabelShort} for {wg?.defName ?? job?.defName}", $"JobGate.Block|{pawn?.ThingID}|{wg?.defName ?? job?.defName}|{stat?.defName}");
                        LogJobQueueSummary(pawn, "JobGate.Block.Final");
                    }
                    LogDecisionLine(pawn, wg, job, forced, blocked: true, reason: "MissingRequiredTool", statForDetail: stat, bestScore: bestScore, baseline: baseline);
                    return true;
                }
            }
            LogDecisionLine(pawn, wg, job, forced, blocked: false, reason: "AllToolsPresent");
            return false;
        }

        static StatDef[] ResolveRequiredStats(WorkGiverDef wg, JobDef job, Job jobInstance)
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
                if (IsGatingLoggingEnabled)
                {
                    // Annotate smoothing optional bonus if present (ConstructionSpeed + SmoothingSpeed)
                    string list = string.Join(", ", arr.Select(s => s.defName));
                    if (arr.Length > 1 && arr.Any(s => s.defName.IndexOf("smooth", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        // TODO[SMOOTHING_TOOL_PURPOSE]: if future smoothing tool exists, treat SmoothingSpeed as weighted tie-breaker
                        list += " (optional SmoothingSpeed)";
                    }
                    LogDebug($"[JobGate.ResolveStats] WorkGiver {wg.defName}: found {arr.Length} stats via resolver: {list}", $"JobGate_ResolveStats_{wg.defName}");
                }

                _wgReq[wg] = arr;
                return _wgReq[wg];
            }
            if (job != null)
            {
                // Use Job instance for target-aware resolution if available (trees vs plants)
                // Don't cache when using jobInstance since results vary by target
                if (jobInstance != null)
                {
                    var statsList = SurvivalToolUtility.StatsForJob(jobInstance);
                    return statsList?.ToArray() ?? new StatDef[0];
                }

                // Fallback to JobDef-based resolution (cacheable)
                StatDef[] arr;
                if (_jobReq.TryGetValue(job, out arr)) return arr;

                // PHASE 6 INTEGRATION: Use SurvivalToolUtility for job stats
                var statsListDef = SurvivalToolUtility.StatsForJob(job);
                arr = statsListDef?.ToArray() ?? new StatDef[0];

                _jobReq[job] = arr;
                return _jobReq[job];
            }
            return new StatDef[0];
        }

        // Shared heuristic with PreWork/GatingEnforcer for quick tool job detection.
        private static bool JobLikelyUsesTools(WorkGiverDef wg, JobDef job)
        {
            try
            {
                if (job != null)
                {
                    var jd = job;
                    if (jd == JobDefOf.Ingest || jd == JobDefOf.LayDown || jd == JobDefOf.Wait || jd == JobDefOf.Wait_MaintainPosture || jd == JobDefOf.Goto || jd == JobDefOf.GotoWander)
                    {
                        if (IsGatingLoggingEnabled) LogDebug($"[JobGate.JLUT] hard-skip {jd.defName}", $"JobGate.JLUT.HardSkip|{jd.defName}");
                        return false;
                    }
                }
                var statsFromWG = wg != null ? SurvivalToolUtility.RelevantStatsFor(wg, job) : null;
                if (statsFromWG != null && statsFromWG.Count > 0)
                {
                    if (IsGatingLoggingEnabled) LogDebug($"[JobGate.JLUT] true via RelevantStatsFor(wg={wg?.defName},job={job?.defName}) count={statsFromWG.Count}", $"JobGate.JLUT.WG|{wg?.defName}|{job?.defName}");
                    return true;
                }
                // When job is null (e.g. called from HasJobOnThing prefix before a job is created),
                // RelevantStatsFor can't determine stats from the job. Check the WorkGiver directly.
                if (wg != null && (job == null || statsFromWG == null || statsFromWG.Count == 0))
                {
                    var wgStatsDirect = StatGatingHelper.GetStatsForWorkGiver(wg);
                    if (wgStatsDirect != null && wgStatsDirect.Count > 0)
                    {
                        if (IsGatingLoggingEnabled) LogDebug($"[JobGate.JLUT] true via GetStatsForWorkGiver({wg.defName}) count={wgStatsDirect.Count}", $"JobGate.JLUT.WGDirect|{wg.defName}");
                        return true;
                    }
                }
                var statsFromJob = SurvivalToolUtility.RelevantStatsFor(wg, job); // job instance path
                if (statsFromJob != null && statsFromJob.Count > 0)
                {
                    if (IsGatingLoggingEnabled) LogDebug($"[JobGate.JLUT] true via RelevantStatsFor 2nd-pass count={statsFromJob.Count}", $"JobGate.JLUT.WG2|{wg?.defName}|{job?.defName}");
                    return true;
                }
                if (job != null)
                {
                    var wg2 = Helpers.JobDefToWorkGiverDefHelper.GetWorkGiverDefForJob(job);
                    var statsFromJobDef = SurvivalToolUtility.RelevantStatsFor(wg2, job);
                    if (statsFromJobDef != null && statsFromJobDef.Count > 0)
                    {
                        if (IsGatingLoggingEnabled) LogDebug($"[JobGate.JLUT] true via JobDefToWG({job.defName})->{wg2?.defName} count={statsFromJobDef.Count}", $"JobGate.JLUT.JobDef|{job.defName}");
                        return true;
                    }
                }
            }
            catch (Exception ex) { if (IsGatingLoggingEnabled) LogDebug($"[JobGate.JLUT] exception: {ex.Message} -> fail-open true", $"JobGate.JLUT.Ex|{wg?.defName}|{job?.defName}"); return true; }
            if (IsGatingLoggingEnabled) LogDebug($"[JobGate.JLUT] FALSE wg={wg?.defName} job={job?.defName}", $"JobGate.JLUT.False|{wg?.defName}|{job?.defName}");
            return false;
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
            // Explicit allowlist additions (Phase 10 modular exemptions)
            if (_pureDeliveryExplicit != null && _pureDeliveryExplicit.Contains(wg)) return true;
            return false;
        }

        // Phase 10: explicit exemption list populated by CompatAPI.ExemptPureDelivery_ByDerivationOrAlias
        private static readonly HashSet<WorkGiverDef> _pureDeliveryExplicit = new HashSet<WorkGiverDef>();
        internal static void MarkPureDelivery(WorkGiverDef wg)
        {
            if (wg == null) return; _pureDeliveryExplicit.Add(wg);
        }

        // Read-only exposure for diagnostics
        internal static IEnumerable<WorkGiverDef> GetExplicitPureDeliveryWorkGivers() => _pureDeliveryExplicit;
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