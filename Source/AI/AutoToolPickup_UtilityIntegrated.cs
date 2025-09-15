// RimWorld 1.6 / C# 7.3
// Source/AI/AutoToolPickup_UtilityIntegrated.cs

using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;
using SurvivalTools.Helpers;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    public static class Patch_JobGiver_Work_TryIssueJobPackage_AutoTool
    {
        private const int SearchRadius = 28;

        #region Entry

        public static void Postfix(Pawn pawn, JobIssueParams jobParams, ref ThinkResult __result)
        {
            try
            {
                if (pawn == null || __result.Job == null) return;

                if (!ShouldAttemptAutoTool(__result.Job, pawn, out var requiredStats))
                {
                    if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"AutoTool_SkipPreconditions_{pawn?.ThingID ?? "null"}"))
                        LogDebug($"[SurvivalTools.AutoTool] Skipping auto-tool for {pawn?.LabelShort ?? "<null pawn>"} — preconditions failed or job ineligible.", $"AutoTool_SkipPreconditions_{pawn?.ThingID ?? "null"}");
                    return;
                }

                bool isDebug = IsDebugLoggingEnabled;
                var jobName = __result.Job.def?.defName ?? "unknown";

                // Already has a suitable tool?
                if (PawnHasHelpfulTool(pawn, requiredStats))
                {
                    if (isDebug && ShouldLogWithCooldown($"AutoTool_HasHelpfulEarly_{pawn.ThingID}"))
                        LogDebug($"[SurvivalTools.AutoTool] {pawn.LabelShort} retains existing tool set for {jobName}; no pickup needed.", $"AutoTool_HasHelpfulEarly_{pawn.ThingID}");
                    return;
                }

                // Hardcore gating: if we can't acquire something, block the job
                if (SurvivalTools.Settings?.hardcoreMode == true)
                {
                    int rsCount = requiredStats?.Count ?? 0;
                    for (int i = 0; i < rsCount; i++)
                    {
                        var stat = requiredStats[i];
                        if (stat == null) continue;
                        if (StatGatingHelper.ShouldBlockJobForStat(stat, SurvivalTools.Settings, pawn) &&
                            !CanAcquireHelpfulToolNow(pawn, requiredStats, isDebug))
                        {
                            __result = ThinkResult.NoJob;
                            return;
                        }
                    }
                }

                // Normal & Hardcore: attempt proactive pickup (Hardcore may have gated already).
                Thing backing;
                var bestTool = SurvivalToolUtility.FindBestToolCandidate(pawn, requiredStats, searchMap: true, out backing);

                if (bestTool == null && backing == null)
                {
                    // Optional expansion: if single stat has no improving tool, broaden to a paired stat set
                    var expanded = ExpandAcquisitionStats(requiredStats);
                    if (expanded != null && expanded.Count > requiredStats.Count)
                    {
                        if (isDebug && ShouldLogWithCooldown($"AutoTool_Expand_{pawn.ThingID}_{jobName}"))
                        {
                            var beforeSb = new System.Text.StringBuilder();
                            for (int i = 0; i < requiredStats.Count; i++)
                            {
                                var rs = requiredStats[i]; if (rs == null) continue; if (beforeSb.Length > 0) beforeSb.Append(", "); beforeSb.Append(rs.defName);
                            }
                            var afterSb = new System.Text.StringBuilder();
                            for (int i = 0; i < expanded.Count; i++)
                            {
                                var rs = expanded[i]; if (rs == null) continue; if (afterSb.Length > 0) afterSb.Append(", "); afterSb.Append(rs.defName);
                            }
                            LogDebug($"[SurvivalTools.AutoTool] Expanding required stats for {pawn.LabelShort} job={jobName} from [{beforeSb}] to [{afterSb}] to find an improving tool.", $"AutoTool_Expand_{pawn.ThingID}_{jobName}");
                        }
                        requiredStats = expanded;
                        bestTool = SurvivalToolUtility.FindBestToolCandidate(pawn, requiredStats, searchMap: true, out backing);
                    }
                }

                if (bestTool != null && backing != null)
                {
                    if (isDebug && ShouldLogWithCooldown($"AutoTool_Pick_{pawn.ThingID}_{bestTool.LabelShort}"))
                    {
                        var statsSb = new System.Text.StringBuilder();
                        for (int i = 0; i < requiredStats.Count; i++) { var rs = requiredStats[i]; if (rs == null) continue; if (statsSb.Length > 0) statsSb.Append(", "); statsSb.Append(rs.defName); }
                        LogDebug($"[SurvivalTools.AutoTool] {pawn.LabelShort} will pick up {bestTool.LabelShort} for job {jobName}; stats=[{statsSb}].", $"AutoTool_Pick_{pawn.ThingID}_{bestTool.LabelShort}");
                    }
                    __result = CreateToolPickupJobs(pawn, __result.Job, bestTool, requiredStats, __result.SourceNode);
                }
                else if (isDebug && ShouldLogWithCooldown($"AutoTool_NoCandidate_{pawn.ThingID}_{jobName}"))
                {
                    var statsSb = new System.Text.StringBuilder();
                    for (int i = 0; i < requiredStats.Count; i++) { var rs = requiredStats[i]; if (rs == null) continue; if (statsSb.Length > 0) statsSb.Append(", "); statsSb.Append(rs.defName); }
                    LogDebug($"[SurvivalTools.AutoTool] No improving tool candidate found for {pawn.LabelShort} job {jobName} (stats=[{statsSb}]).", $"AutoTool_NoCandidate_{pawn.ThingID}_{jobName}");
                }
            }
            catch (Exception ex)
            {
                if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"AutoTool_Exception_{pawn?.ThingID ?? "null"}"))
                    Log.Error($"[SurvivalTools.AutoTool] Exception in AutoTool postfix: {ex}");
            }
        }

        #endregion

        #region Scan preconditions

        private static bool ShouldAttemptAutoTool(Job job, Pawn pawn, out List<StatDef> requiredStats)
        {
            requiredStats = null;

            if (SurvivalTools.Settings?.autoTool != true ||
                job == null || pawn == null || pawn.Map == null ||
                !pawn.CanUseSurvivalTools() || pawn.Drafted || pawn.InMentalState ||
                job.def == JobDefOf.TakeInventory)
                return false;

            // Special-case tree felling
            if (job.def == ST_JobDefOf.FellTree ||
                job.def == ST_JobDefOf.FellTreeDesignated ||
                (job.def == JobDefOf.CutPlant && job.targetA.Thing?.def?.plant?.IsTree == true))
            {
                requiredStats = new List<StatDef> { ST_StatDefOf.TreeFellingSpeed };
                return true;
            }

            requiredStats = SurvivalToolUtility.RelevantStatsFor(job.workGiverDef, job) ?? new List<StatDef>();
            return !requiredStats.NullOrEmpty();
        }

        private static bool PawnHasHelpfulTool(Pawn pawn, List<StatDef> requiredStats)
        {
            if (pawn == null || requiredStats == null || requiredStats.Count == 0) return false;
            var tools = pawn.GetAllUsableSurvivalTools();
            foreach (var thing in tools)
            {
                SurvivalTool st = thing as SurvivalTool;
                if (st == null && thing.def != null && thing.def.IsToolStuff())
                    st = VirtualTool.FromThing(thing);
                if (st != null && SurvivalToolUtility.ToolImprovesAny(st, requiredStats))
                {
                    if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"AutoTool_HasHelpful_{pawn.ThingID}"))
                    {
                        var names = new System.Text.StringBuilder();
                        for (int i = 0; i < requiredStats.Count; i++)
                        {
                            var rs = requiredStats[i];
                            if (rs == null) continue;
                            float toolFactor = SurvivalToolUtility.GetToolProvidedFactor(st, rs);
                            float baseline = SurvivalToolUtility.GetNoToolBaseline(rs);
                            if (toolFactor > baseline + 0.001f)
                            {
                                if (names.Length > 0) names.Append(", ");
                                names.Append(rs.defName).Append("(baseline=").Append(baseline.ToString("F2")).Append(" -> ").Append(toolFactor.ToString("F2")).Append(")");
                            }
                        }
                        LogDebug($"[SurvivalTools.AutoTool] {pawn.LabelShort} already has helpful tool {st.LabelShort}: improves [{names}]", $"AutoTool_HasHelpful_{pawn.ThingID}");
                    }
                    return true;
                }
            }
            return false;
        }

        // Broadens a narrow single-stat requirement to a paired set when no direct improvement tool exists.
        // Helps Normal Mode pick up multi-purpose tools that indirectly aid related tasks.
        private static List<StatDef> ExpandAcquisitionStats(List<StatDef> current)
        {
            if (current == null || current.Count != 1) return current;
            var only = current[0];
            if (only == null) return current;
            // Pair sow/harvest stats bidirectionally
            if (only == ST_StatDefOf.SowingSpeed)
            {
                var list = new List<StatDef>(2) { only };
                if (!list.Contains(ST_StatDefOf.PlantHarvestingSpeed)) list.Add(ST_StatDefOf.PlantHarvestingSpeed);
                return list;
            }
            if (only == ST_StatDefOf.PlantHarvestingSpeed)
            {
                var list = new List<StatDef>(2) { only };
                if (!list.Contains(ST_StatDefOf.SowingSpeed)) list.Add(ST_StatDefOf.SowingSpeed);
                return list;
            }
            return current; // unchanged
        }

        #endregion

        #region Acquire check

        private static bool CanAcquireHelpfulToolNow(Pawn pawn, List<StatDef> requiredStats, bool isDebug)
        {
            Thing backing;
            var best = SurvivalToolUtility.FindBestToolCandidate(pawn, requiredStats, searchMap: true, out backing);
            if (best == null || backing == null) return false;
            if (!backing.Spawned) return false;
            return pawn.CanReserveAndReach(backing, PathEndMode.OnCell, pawn.NormalMaxDanger());
        }

        #endregion

        #region Pickup job creation

        private static ThinkResult CreateToolPickupJobs(
            Pawn pawn,
            Job originalJob,
            SurvivalTool toolToGet,
            List<StatDef> requiredStats,
            ThinkNode sourceNode)
        {
            if (pawn == null || originalJob == null || toolToGet == null)
                return new ThinkResult(originalJob, sourceNode, JobTag.Misc, false);

            var spawnTarget = SurvivalToolUtility.BackingThing(toolToGet, pawn) ?? (Thing)toolToGet;
            if (spawnTarget == null || !spawnTarget.Spawned || spawnTarget.Map != pawn.Map ||
                spawnTarget.IsForbidden(pawn) ||
                !pawn.CanReserveAndReach(spawnTarget, PathEndMode.OnCell, pawn.NormalMaxDanger()))
            {
                return new ThinkResult(originalJob, sourceNode, JobTag.Misc, false);
            }

            var pickupJob = JobMaker.MakeJob(JobDefOf.TakeInventory, spawnTarget);
            pickupJob.count = 1;

            // Simple: queue pickup, then resume original job
            if (originalJob.def != JobDefOf.TakeInventory)
            {
                // Avoid cloning DoBill jobs because other mods (e.g. CommonSense) patch internal bill state.
                if (originalJob.def.defName != null && originalJob.def.defName.IndexOf("DoBill", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"AutoTool_RequeueOriginal_{pawn.ThingID}"))
                        LogDebug($"[SurvivalTools.AutoTool] Re-queuing original DoBill job (no clone) after pickup for {pawn.LabelShort}.", $"AutoTool_RequeueOriginal_{pawn.ThingID}");
                    pawn.jobs.jobQueue.EnqueueFirst(originalJob);
                }
                else
                {
                    var cloned = CloneJobForQueue(originalJob);
                    pawn.jobs.jobQueue.EnqueueFirst(cloned ?? originalJob);
                }
            }

            return new ThinkResult(pickupJob, sourceNode, JobTag.Misc, false);
        }

        private static Job CloneJobForQueue(Job j)
        {
            if (j == null) return null;

            // Make a new job with the same def/targets
            var copy = JobMaker.MakeJob(j.def, j.targetA, j.targetB, j.targetC);

            // Copy fields manually (Job fields are public, not properties in RW 1.6)
            copy.count = j.count;
            copy.playerForced = j.playerForced;
            copy.expiryInterval = j.expiryInterval;
            copy.checkOverrideOnExpire = j.checkOverrideOnExpire;
            copy.locomotionUrgency = j.locomotionUrgency;
            copy.overeat = j.overeat;
            copy.killIncappedTarget = j.killIncappedTarget;
            copy.ignoreForbidden = j.ignoreForbidden;
            copy.ignoreDesignations = j.ignoreDesignations;
            copy.ignoreJoyTimeAssignment = j.ignoreJoyTimeAssignment;

            // Extra commonly used fields in DoBill & hauling contexts (defensive null checks)
            try { copy.haulOpportunisticDuplicates = j.haulOpportunisticDuplicates; } catch { }
            try { copy.bill = j.bill; } catch { }
            try { copy.placedThings = j.placedThings != null ? new List<ThingCountClass>(j.placedThings) : null; } catch { }
            try { copy.startTick = j.startTick; } catch { }

            // Copy queues safely
            if (j.targetQueueA != null)
                copy.targetQueueA = new List<LocalTargetInfo>(j.targetQueueA);
            if (j.targetQueueB != null)
                copy.targetQueueB = new List<LocalTargetInfo>(j.targetQueueB);
            if (j.countQueue != null)
                copy.countQueue = new List<int>(j.countQueue);

            return copy;
        }


        #endregion

        // Legacy scanner & scoring removed: unified logic lives in SurvivalToolUtility.FindBestToolCandidate / ScoreToolForStats / ToolImprovesAny
    }
}
