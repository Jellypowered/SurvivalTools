
// RimWorld 1.6 / C# 7.3
// Source/Assign/AssignmentSearch.cs
//
// Phase 6: Pre-work auto-equip without ping-pong
// - Searches for better tools before starting work
// - Respects carry limits by difficulty
// - Implements hysteresis to prevent re-upgrading
// - LINQ-free, pooled buffers for performance

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using SurvivalTools.Scoring;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;



namespace SurvivalTools.Assign
{
    public static class AssignmentSearch
    {
        // Controls where to place queued acquisition jobs
        public enum QueuePriority
        {
            Append, // Add to back of the queue (AI/WorkGiver paths)
            Front   // Push to front of the queue (player-ordered rescue path)
        }
        // Pooled collections to avoid allocations
        private static readonly List<Thing> _candidateBuffer = new List<Thing>(64);
        private static readonly List<Thing> _inventoryBuffer = new List<Thing>(16);
        private static readonly List<Thing> _stockpileBuffer = new List<Thing>(32);

        // Hysteresis tracking: pawnID -> (lastUpgradeTick, lastEquippedDefName)
        private static readonly Dictionary<int, HysteresisData> _hysteresisData = new Dictionary<int, HysteresisData>();

        // Anti-recursion: pawnID -> processing flag
        private static readonly Dictionary<int, bool> _processingPawns = new Dictionary<int, bool>();

        private const int HysteresisTicksNormal = 5000;
        private const float HysteresisExtraGainPct = 0.05f; // +5% for re-upgrade
        private const float GatingEpsilon = 0.001f;
    private const int FocusTicksWindow = 600; // 10 seconds: prefer current work stat, avoid thrash

        // Cooldown for repeatedly failing candidates (forbidden, unreachable, reserved by others)
        // Key: ThingIDNumber -> nextAllowedTick
        private static readonly Dictionary<int, int> _candidateCooldownTicks = new Dictionary<int, int>(128);
        private const int CandidateCooldownTicks = 600; // 10 seconds (at 60 TPS)

        // Short-lived focus window to avoid cross-stat thrashing
        // pawnID -> (untilTick, statDefName)
        private static readonly Dictionary<int, FocusData> _statFocus = new Dictionary<int, FocusData>(64);

    // Recently acquired protection: pawnID -> (untilTick, lastAcquiredThingID)
    private static readonly Dictionary<int, RecentAcqData> _recentAcquisitions = new Dictionary<int, RecentAcqData>(64);

        private struct FocusData
        {
            public int untilTick;
            public string statDefName;
        }

        private struct RecentAcqData
        {
            public int untilTick;
            public int thingID;
        }

        private struct HysteresisData
        {
            public int lastUpgradeTick;
            public string lastEquippedDefName;
        }

        /// <summary>
        /// Public helper for other systems (e.g., inventory auto-trim) to respect the
        /// recent-acquisition protection window and avoid immediately dropping the
        /// tool we just targeted/acquired.
        /// Accepts a physical thing (real tool or tool-stuff stack). If a virtual
        /// SurvivalTool wrapper is passed, we resolve its backing physical thing
        /// before comparing IDs.
        /// </summary>
        public static bool IsRecentlyAcquired(Pawn pawn, Thing toolOrStack)
        {
            try
            {
                if (pawn == null || toolOrStack == null) return false;
                int now = Find.TickManager?.TicksGame ?? 0;
                if (!_recentAcquisitions.TryGetValue(pawn.thingIDNumber, out var acq) || now >= acq.untilTick)
                    return false;

                // Direct match
                if (toolOrStack.thingIDNumber == acq.thingID)
                    return true;

                // If we got a SurvivalTool (real or virtual), try to resolve to a backing physical thing
                var st = toolOrStack as SurvivalTool;
                if (st != null)
                {
                    var back = SurvivalToolUtility.BackingThing(st, pawn);
                    if (back != null && back.thingIDNumber == acq.thingID)
                        return true;
                }
            }
            catch { /* best-effort */ }
            return false;
        }

        private struct ToolCandidate
        {
            public Thing tool;
            public float score;
            public float gainPct;
            public int pathCost;
            public ToolLocation location;
        }

        private enum ToolLocation
        {
            Inventory,
            Equipment,
            SameCell,
            Stockpile,
            HomeArea,
            Nearby
        }

        /// <summary>
        /// Returns true if we queued an equip/haul job; original job should be retried afterward.
        /// </summary>
        // Backward-compatible overload (public API): defaults to Append priority
        public static bool TryUpgradeFor(Pawn pawn, StatDef workStat, float minGainPct, float radius, int pathCostBudget)
            => TryUpgradeForInternal(pawn, workStat, minGainPct, radius, pathCostBudget, QueuePriority.Append, null);

        // New overload with explicit queue priority control
        public static bool TryUpgradeFor(Pawn pawn, StatDef workStat, float minGainPct, float radius, int pathCostBudget, QueuePriority priority)
            => TryUpgradeForInternal(pawn, workStat, minGainPct, radius, pathCostBudget, priority, null);

        // New overload including caller context for diagnostics
        public static bool TryUpgradeFor(Pawn pawn, StatDef workStat, float minGainPct, float radius, int pathCostBudget, QueuePriority priority, string caller)
        {
            // Log with cooldown via ST_Logging to avoid spam
            string cooldownKey = $"TryUpgradeFor_{pawn?.ThingID}_{workStat?.defName}|{caller}";
            if (ShouldLogWithCooldown(cooldownKey))
            {
                LogDebug($"[SurvivalTools.Assignment] TryUpgradeFor CALLED for {pawn?.LabelShort} with {workStat?.defName} (priority={priority}, caller={caller ?? "(none)"})", cooldownKey);
            }
            return TryUpgradeForInternal(pawn, workStat, minGainPct, radius, pathCostBudget, priority, caller);
        }

        private static bool TryUpgradeForInternal(Pawn pawn, StatDef workStat, float minGainPct, float radius, int pathCostBudget, QueuePriority priority, string caller)
        {
            LogDebug($"TryUpgradeFor called: pawn={pawn?.LabelShort}, workStat={workStat?.defName}, minGainPct={minGainPct:P1}, radius={radius}, pathCostBudget={pathCostBudget}, caller={caller ?? "(none)"}", "AssignmentSearch.TryUpgradeFor");
            LogCurrentJobState(pawn, caller != null ? $"TryUpgradeFor:start:{caller}" : "TryUpgradeFor:start");
            LogJobQueue(pawn, caller != null ? $"TryUpgradeFor:start:{caller}" : "TryUpgradeFor:start");

            // Early-out blacklist
            if (!CanPawnUpgrade(pawn))
            {
                LogDebug($"CanPawnUpgrade failed for {pawn?.LabelShort}", "AssignmentSearch.CanPawnUpgrade");
                return false;
            }

            // If we recently focused a different work stat, skip to avoid tool thrashing
            if (IsBlockedByFocus(pawn.thingIDNumber, workStat?.defName))
            {
                LogDebug($"Blocked by focus window: {pawn.LabelShort} focus is different than {workStat?.defName}", "AssignmentSearch.FocusBlock");
                LogJobQueue(pawn, caller != null ? $"TryUpgradeFor:blockedByFocus:{caller}" : "TryUpgradeFor:blockedByFocus");
                return false;
            }

            // Anti-recursion check
            int pawnID = pawn.thingIDNumber;
            if (_processingPawns.TryGetValue(pawnID, out bool processing) && processing)
            {
                LogDebug($"Anti-recursion triggered for {pawn.LabelShort}", "AssignmentSearch.AntiRecursion");
                return false;
            }

            try
            {
                _processingPawns[pawnID] = true;
            if (workStat == null)
            {
                LogDebug($"workStat is null for {pawn?.LabelShort}", "AssignmentSearch.NullWorkStat");
                return false;
            }

            // Get current score and tool
            var currentTool = ToolScoring.GetBestTool(pawn, workStat, out float currentScore);
            string currentDefName = currentTool?.def?.defName ?? "none";
            LogDebug($"Current tool for {pawn.LabelShort} / {workStat.defName}: {currentDefName} (score: {currentScore:F3})", "AssignmentSearch.CurrentTool");

            // If a drop or acquisition is already being performed as the CURRENT job, defer any new work this pass
            // Note: we intentionally IGNORE queued items here because queued ordered jobs do not auto-start.
            bool pendingDrop = HasPendingDropJob(pawn);
            bool pendingAcquire = HasPendingAcquisitionJob(pawn);
            if (pendingDrop || pendingAcquire)
            {
                LogDebug($"Pending tool-management job detected for {pawn.LabelShort} — deferring (drop={pendingDrop}, acquire={pendingAcquire})", "AssignmentSearch.DeferForPendingQueue");
                LogJobQueue(pawn, "TryUpgradeFor:pendingQueue");
                return true; // signal that we're handling tool management
            }

            // Find best candidate
            var candidate = FindBestCandidate(pawn, workStat, currentScore, minGainPct, radius, pathCostBudget);
            if (candidate.tool == null)
            {
                LogDebug($"No candidate tool found for {pawn.LabelShort}", "AssignmentSearch.NoCandidate");
                return false;
            }

            LogDebug($"Found candidate tool for {pawn.LabelShort}: {candidate.tool.LabelShort} (score: {candidate.score:F3}, gain: {candidate.gainPct:P1}, location: {candidate.location})", "AssignmentSearch.FoundCandidate");
            LogJobQueue(pawn, caller != null ? $"TryUpgradeFor:preQueueCandidate:{caller}" : "TryUpgradeFor:preQueueCandidate");

            // Apply hysteresis AFTER selecting a concrete candidate so we can use its gain and def
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            var candDefName = candidate.tool.def?.defName ?? string.Empty;
            if (IsInHysteresis(pawn.thingIDNumber, currentTick, candDefName, candidate.gainPct, minGainPct))
            {
                LogDebug($"Hysteresis check failed for {pawn.LabelShort}", "AssignmentSearch.Hysteresis");
                return false;
            }

            // Queue the job to acquire/equip the tool
            bool acquisitionEnqueued;
            if (QueueAcquisitionJob(pawn, candidate, workStat, priority, out acquisitionEnqueued))
            {
                if (acquisitionEnqueued)
                {
                    LogDebug($"Successfully queued acquisition job for {pawn.LabelShort}: {candidate.tool.LabelShort}", "AssignmentSearch.QueueSuccess");

                    // Update hysteresis only when we actually enqueued an acquisition
                    _hysteresisData[pawn.thingIDNumber] = new HysteresisData
                    {
                        lastUpgradeTick = currentTick,
                        lastEquippedDefName = candidate.tool.def.defName
                    };

                    // Set a short focus window on this stat to prevent other stats from thrashing
                    SetFocus(pawn, workStat);

                    // Notify score cache
                    ScoreCache.NotifyInventoryChanged(pawn);
                    if (candidate.tool != null)
                        ScoreCache.NotifyToolChanged(candidate.tool);
                    LogJobQueue(pawn, caller != null ? $"TryUpgradeFor:afterEnqueueAcquisition:{caller}" : "TryUpgradeFor:afterEnqueueAcquisition");

                    return true;
                }
                else
                {
                    // We queued a drop and deferred acquisition; report handled but don't update hysteresis yet
                    LogDebug($"Queued drop for {pawn.LabelShort} and deferred acquisition of {candidate.tool.LabelShort}", "AssignmentSearch.DeferredAfterDrop");
                    // Set a short focus window to prioritize this stat; this avoids cross-stat thrashing.
                    SetFocus(pawn, workStat);
                    LogJobQueue(pawn, caller != null ? $"TryUpgradeFor:afterEnqueueDrop:{caller}" : "TryUpgradeFor:afterEnqueueDrop");
                    return true;
                }
            }
            else
            {
                LogDebug($"Failed to queue acquisition job for {pawn.LabelShort}: {candidate.tool.LabelShort}", "AssignmentSearch.QueueFailed");
            }

            return false;
            }
            finally
            {
                _processingPawns[pawnID] = false;
                // Deduped debug path to prevent spam
                LogDebug($"[SurvivalTools.Assignment] TryUpgradeFor result for {pawn.LabelShort} (caller={caller ?? "(none)"}) completed", $"TryUpgradeForResult|{pawn.ThingID}|{workStat?.defName}|{caller}");
            }
        }

        private static void TrySetFocusForGating(Pawn pawn, StatDef workStat, float currentScore)
        {
            try
            {
                if (pawn == null || workStat == null) return;
                float baseline = SurvivalToolUtility.GetNoToolBaseline(workStat);
                if (currentScore <= baseline + GatingEpsilon)
                {
                    SetFocus(pawn, workStat);
                }
            }
            catch { /* best-effort */ }
        }

        private static void SetFocus(Pawn pawn, StatDef workStat)
        {
            try
            {
                if (pawn == null || workStat == null) return;
                int now = Find.TickManager?.TicksGame ?? 0;
                _statFocus[pawn.thingIDNumber] = new FocusData
                {
                    untilTick = now + FocusTicksWindow,
                    statDefName = workStat.defName
                };
                LogDebug($"Set focus for {pawn.LabelShort} on {workStat.defName} for {FocusTicksWindow} ticks", "AssignmentSearch.SetFocus");
            }
            catch { /* best-effort */ }
        }

        private static bool IsBlockedByFocus(int pawnID, string statDefName)
        {
            try
            {
                if (string.IsNullOrEmpty(statDefName)) return false;
                if (!_statFocus.TryGetValue(pawnID, out var focus)) return false;
                int now = Find.TickManager?.TicksGame ?? 0;
                if (now > focus.untilTick) return false;
                // Block if another stat is currently focused
                return !string.Equals(focus.statDefName, statDefName);
            }
            catch { return false; }
        }

        // DIAGNOSTIC LOGGING ------------------------------------------------
        private static void LogCurrentJobState(Pawn pawn, string tag)
        {
            try
            {
                if (pawn?.jobs == null)
                {
                    LogDebug($"[SurvivalTools.Assignment][{tag}] pawn or jobs null", $"Assign.JobState.{pawn?.ThingID}|{tag}");
                    return;
                }
                var cur = pawn.jobs.curJob;
                var curDef = cur?.def?.defName ?? "(none)";
                var curTarget = cur != null ? (cur.targetA.HasThing ? cur.targetA.Thing.LabelShort : (cur.targetA.Cell.IsValid ? cur.targetA.Cell.ToString() : "(n/a)")) : "(n/a)";
                LogDebug($"[SurvivalTools.Assignment][{tag}] curJob={curDef} target={curTarget} interruptible={pawn.jobs.IsCurrentJobPlayerInterruptible()} queueCount={pawn.jobs.jobQueue?.Count ?? 0}", $"Assign.JobState.{pawn.ThingID}|{tag}");
            }
            catch (Exception ex)
            {
                LogError($"[SurvivalTools.Assignment][{tag}] Exception in LogCurrentJobState: {ex}");
            }
        }

        private static void LogJobQueue(Pawn pawn, string tag)
        {
            try
            {
                var jq = pawn?.jobs?.jobQueue;
                if (jq == null)
                {
                    LogDebug($"[SurvivalTools.Assignment][{tag}] jobQueue=null", $"Assign.JobQueue.{pawn?.ThingID}|{tag}");
                    return;
                }
                int count = jq.Count;
                // Build a compact single-line queue summary to avoid multi-line spam
                if (count == 0)
                {
                    LogDebug($"[SurvivalTools.Assignment][{tag}] jobQueue count=0", $"Assign.JobQueue.{pawn.ThingID}|{tag}");
                    return;
                }
                System.Text.StringBuilder sb = new System.Text.StringBuilder(64 + count * 24);
                sb.Append($"[SurvivalTools.Assignment][{tag}] jobQueue count={count} :: ");
                for (int i = 0; i < count; i++)
                {
                    var item = jq[i];
                    var def = item?.job?.def?.defName ?? "(null)";
                    var j = item?.job;
                    string target = "(n/a)";
                    if (j != null)
                    {
                        if (j.targetA.HasThing)
                        {
                            var t = j.targetA.Thing;
                            target = $"{t.LabelShort}#{t.thingIDNumber}";
                        }
                        else if (j.targetA.Cell.IsValid)
                        {
                            target = j.targetA.Cell.ToString();
                        }
                    }
                    if (i > 0) sb.Append(" | ");
                    sb.Append('[').Append(i).Append("] ").Append(def).Append(" -> ").Append(target);
                }
                LogDebug(sb.ToString(), $"Assign.JobQueue.{pawn.ThingID}|{tag}");
            }
            catch (Exception ex)
            {
                LogError($"[SurvivalTools.Assignment][{tag}] Exception in LogJobQueue: {ex}");
            }
        }

        /// <summary>
        /// Enhanced validation for tool state before acquisition.
        /// Checks for real-time issues that could cause job failures.
        /// </summary>
        private static bool ValidateToolStateForAcquisition(Pawn pawn, Thing tool)
        {
            if (pawn == null || tool == null)
            {
                LogDebug($"ValidateToolStateForAcquisition: null pawn or tool", "AssignmentSearch.ValidationNull");
                return false;
            }

            // Check if tool still exists and is not destroyed
            if (tool.Destroyed)
            {
                LogDebug($"ValidateToolStateForAcquisition: {tool.LabelShort} is destroyed", "AssignmentSearch.ValidationDestroyed");
                return false;
            }

            // Check if tool is on same map
            if (tool.Map != pawn.Map)
            {
                LogDebug($"ValidateToolStateForAcquisition: {tool.LabelShort} is on different map", "AssignmentSearch.ValidationDifferentMap");
                SetCandidateCooldown(tool);
                return false;
            }

            // Check if tool is held by another pawn (not stockpiles/ground)
            // Allow tools in stockpiles, on ground, or in our own inventory/equipment
            var parentHolder = tool.ParentHolder;
            if (parentHolder is Pawn_InventoryTracker inventoryTracker &&
                inventoryTracker.pawn != pawn)
            {
                LogDebug($"ValidateToolStateForAcquisition: {tool.LabelShort} is held by another pawn ({inventoryTracker.pawn.LabelShort})", "AssignmentSearch.ValidationHeldByPawn");
                SetCandidateCooldown(tool);
                return false;
            }

            // Check if tool is equipped by another pawn
            if (parentHolder is Pawn_EquipmentTracker equipmentTracker &&
                equipmentTracker.pawn != pawn)
            {
                LogDebug($"ValidateToolStateForAcquisition: {tool.LabelShort} is equipped by another pawn ({equipmentTracker.pawn.LabelShort})", "AssignmentSearch.ValidationEquippedByPawn");
                SetCandidateCooldown(tool);
                return false;
            }

            // Check if pawn can reserve and reach the tool (legacy approach)
            if (!pawn.CanReserveAndReach(tool, PathEndMode.OnCell, pawn.NormalMaxDanger()))
            {
                LogDebug($"ValidateToolStateForAcquisition: {pawn.LabelShort} cannot reserve and reach {tool.LabelShort}", "AssignmentSearch.ValidationCannotReserveAndReach");
                SetCandidateCooldown(tool);
                return false;
            }

            // Check if tool is forbidden
            if (tool.IsForbidden(pawn))
            {
                LogDebug($"ValidateToolStateForAcquisition: {tool.LabelShort} is forbidden for {pawn.LabelShort}", "AssignmentSearch.ValidationForbidden");
                SetCandidateCooldown(tool);
                return false;
            }

            LogDebug($"[SurvivalTools.Assignment] Tool validation PASSED for {pawn.LabelShort} acquiring {tool.LabelShort}", $"Assign.ToolValidation.Acq|{pawn.ThingID}|{tool.thingIDNumber}");
            return true;
        }

        /// <summary>
        /// Enhanced validation for tool state before dropping.
        /// Ensures the tool is actually carried by the pawn and can be dropped.
        /// </summary>
        private static bool ValidateToolStateForDrop(Pawn pawn, Thing tool)
        {
            if (pawn == null || tool == null)
            {
                LogDebug($"ValidateToolStateForDrop: null pawn or tool", "AssignmentSearch.DropValidationNull");
                return false;
            }

            // Check if tool still exists and is not destroyed
            if (tool.Destroyed)
            {
                LogDebug($"ValidateToolStateForDrop: {tool.LabelShort} is destroyed", "AssignmentSearch.DropValidationDestroyed");
                return false;
            }

            // Check if tool is on same map (allow null for carried items)
            if (tool.Map != null && tool.Map != pawn.Map)
            {
                LogDebug($"ValidateToolStateForDrop: {tool.LabelShort} is on different map", "AssignmentSearch.DropValidationDifferentMap");
                return false;
            }

            // Check if tool is actually carried by this pawn
            bool isInInventory = pawn.inventory?.innerContainer?.Contains(tool) == true;
            bool isEquipped = pawn.equipment?.AllEquipmentListForReading?.Contains(tool) == true;

            if (!isInInventory && !isEquipped)
            {
                LogDebug($"ValidateToolStateForDrop: {tool.LabelShort} is not carried by {pawn.LabelShort}", "AssignmentSearch.DropValidationNotCarried");
                return false;
            }

            // Check if pawn is incapacitated (can't drop if downed)
            if (pawn.Downed || pawn.Dead)
            {
                LogDebug($"ValidateToolStateForDrop: {pawn.LabelShort} is downed or dead", "AssignmentSearch.DropValidationIncapacitated");
                return false;
            }

            LogDebug($"[SurvivalTools.Assignment] Drop validation PASSED for {pawn.LabelShort} dropping {tool.LabelShort}", $"Assign.ToolValidation.Drop|{pawn.ThingID}|{tool.thingIDNumber}");
            return true;
        }

        // QUEUE DEDUP/CONSUMPTION -------------------------------------------
        private static Thing GetBackingThing(Pawn pawn, Thing tool)
        {
            try
            {
                var asTool = tool as SurvivalTool;
                var back = SurvivalToolUtility.BackingThing(asTool, pawn);
                return back ?? tool;
            }
            catch { return tool; }
        }

        private static bool TargetsSameTool(Pawn pawn, Thing a, Thing b)
        {
            if (a == null || b == null) return false;
            if (a.thingIDNumber == b.thingIDNumber) return true;
            var ba = GetBackingThing(pawn, a);
            var bb = GetBackingThing(pawn, b);
            if (ba != null && bb != null && ba.thingIDNumber == bb.thingIDNumber) return true;
            // Fallback: same def (last resort)
            return a.def == b.def;
        }

        private static bool TryStartQueuedToolJobFor(Pawn pawn, Thing tool, params JobDef[] jobDefs)
        {
            try
            {
                if (pawn?.jobs?.jobQueue == null || tool == null) return false;
                if (pawn.jobs.curJob != null) return false; // only start when idle

                var jq = pawn.jobs.jobQueue;
                for (int i = 0; i < jq.Count; i++)
                {
                    var q = jq[i];
                    var j = q?.job;
                    if (j == null || j.def == null) continue;
                    bool defMatch = false;
                    for (int d = 0; d < jobDefs.Length; d++)
                    {
                        if (j.def == jobDefs[d]) { defMatch = true; break; }
                    }
                    if (!defMatch) continue;
                    var targetThing = j.targetA.Thing;
                    if (targetThing != null && TargetsSameTool(pawn, targetThing, tool))
                    {
                        // Start a cloned job to avoid state attached to the queued instance
                        var cloned = JobUtils.CloneJobForQueue(j);
                        if (cloned == null || cloned.def == null)
                            return false;
                        cloned.playerForced = true;
                        bool started = false;
                        try
                        {
                            started = pawn.jobs.TryTakeOrderedJob(cloned);
                        }
                        catch (Exception ex)
                        {
                            LogError($"[SurvivalTools.Assignment] Exception starting cloned queued job: {ex}");
                            started = false;
                        }
                        if (started)
                        {
                            LogDebug($"[SurvivalTools.Assignment] Started queued {cloned.def.defName} immediately for {pawn.LabelShort} targeting {tool.LabelShort}", $"Assign.StartQueued|{pawn.ThingID}|{tool.thingIDNumber}|{cloned.def?.defName}");
                            // Safely remove the original queued entry by index if still present
                            var queueList = jq as System.Collections.IList;
                            if (queueList != null && i >= 0 && i < jq.Count && jq[i]?.job == j)
                            {
                                queueList.RemoveAt(i);
                            }
                            return true;
                        }
                        else
                        {
                            // Could not start; leave original queued job in place
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"[SurvivalTools.Assignment] Exception in TryStartQueuedToolJobFor: {ex}");
            }
            return false;
        }

        private static void RemoveQueuedToolJobsFor(Pawn pawn, Thing tool, params JobDef[] jobDefs)
        {
            try
            {
                var jq = pawn?.jobs?.jobQueue;
                if (jq == null || tool == null) return;
                for (int i = jq.Count - 1; i >= 0; i--)
                {
                    var j = jq[i]?.job;
                    if (j?.def == null) continue;
                    bool defMatch = false;
                    for (int d = 0; d < jobDefs.Length; d++)
                    {
                        if (j.def == jobDefs[d]) { defMatch = true; break; }
                    }
                    if (!defMatch) continue;
                    var targetThing = j.targetA.Thing;
                    if (targetThing != null && TargetsSameTool(pawn, targetThing, tool))
                    {
                        var queueList = jq as System.Collections.IList;
                        if (queueList != null) queueList.RemoveAt(i);
                        LogDebug($"Removed duplicate queued {j.def.defName} for {pawn?.LabelShort} targeting {tool.LabelShort}", "AssignmentSearch.DedupeRemove");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"[SurvivalTools.Assignment] Exception in RemoveQueuedToolJobsFor: {ex}");
            }
        }

        private static bool CanPawnUpgrade(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed)
                return false;

            if (pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                return false;

            if (pawn.guest != null && !pawn.IsColonist)
                return false;

            if (!pawn.Awake())
                return false;

            // Check essential capacities
            if (pawn.health?.capacities == null)
                return false;

            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                return false;

            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
                return false;

            return true;
        }

        private static bool IsInHysteresis(int pawnID, int currentTick, string currentDefName, float minGainPct)
        {
            if (!_hysteresisData.TryGetValue(pawnID, out var data))
                return false;

            int ticksSinceUpgrade = currentTick - data.lastUpgradeTick;
            if (ticksSinceUpgrade < HysteresisTicksNormal)
            {
                // Still in hysteresis period - require extra gain to re-upgrade
                if (data.lastEquippedDefName == currentDefName)
                {
                    // Same tool, need extra gain
                    return minGainPct < (minGainPct + HysteresisExtraGainPct);
                }
            }

            return false;
        }

        private static ToolCandidate FindBestCandidate(Pawn pawn, StatDef workStat, float currentScore, float minGainPct, float radius, int pathCostBudget)
        {
            var bestCandidate = new ToolCandidate();
            float baseline = SurvivalToolUtility.GetNoToolBaseline(workStat);
            bool needsGatingRescue = currentScore <= baseline + GatingEpsilon;

            // Search order: inventory → equipment → same cell → stockpiles → home area → nearby
            SearchInventory(pawn, workStat, currentScore, minGainPct, needsGatingRescue, ref bestCandidate);
            if (bestCandidate.tool != null && bestCandidate.location == ToolLocation.Inventory)
                return bestCandidate; // Already in inventory, best case

            SearchEquipment(pawn, workStat, currentScore, minGainPct, needsGatingRescue, ref bestCandidate);
            if (bestCandidate.tool != null && bestCandidate.location == ToolLocation.Equipment)
                return bestCandidate; // On belt/pack, very good

            SearchSameCell(pawn, workStat, currentScore, minGainPct, needsGatingRescue, ref bestCandidate);
            if (bestCandidate.tool != null && bestCandidate.location == ToolLocation.SameCell)
                return bestCandidate; // Same cell, excellent

            SearchStockpiles(pawn, workStat, currentScore, minGainPct, needsGatingRescue, radius, pathCostBudget, ref bestCandidate);
            SearchHomeArea(pawn, workStat, currentScore, minGainPct, needsGatingRescue, radius, pathCostBudget, ref bestCandidate);
            SearchNearby(pawn, workStat, currentScore, minGainPct, needsGatingRescue, radius, pathCostBudget, ref bestCandidate);

            return bestCandidate;
        }

        private static void SearchInventory(Pawn pawn, StatDef workStat, float currentScore, float minGainPct, bool needsGatingRescue, ref ToolCandidate bestCandidate)
        {
            var inventory = pawn.inventory?.innerContainer;
            if (inventory == null)
                return;

            _inventoryBuffer.Clear();
            for (int i = 0; i < inventory.Count; i++)
            {
                var thing = inventory[i];
                if (thing != null && IsValidTool(thing, workStat))
                    _inventoryBuffer.Add(thing);
            }

            EvaluateCandidates(_inventoryBuffer, pawn, workStat, currentScore, minGainPct, needsGatingRescue,
                               ToolLocation.Inventory, 0, ref bestCandidate);
        }

        private static void SearchEquipment(Pawn pawn, StatDef workStat, float currentScore, float minGainPct, bool needsGatingRescue, ref ToolCandidate bestCandidate)
        {
            var equipment = pawn.equipment?.AllEquipmentListForReading;
            if (equipment == null)
                return;

            _candidateBuffer.Clear();
            for (int i = 0; i < equipment.Count; i++)
            {
                var thing = equipment[i];
                if (thing != null && IsValidTool(thing, workStat))
                    _candidateBuffer.Add(thing);
            }

            EvaluateCandidates(_candidateBuffer, pawn, workStat, currentScore, minGainPct, needsGatingRescue,
                               ToolLocation.Equipment, 0, ref bestCandidate);
        }

        private static void SearchSameCell(Pawn pawn, StatDef workStat, float currentScore, float minGainPct, bool needsGatingRescue, ref ToolCandidate bestCandidate)
        {
            var cell = pawn.Position;
            if (!cell.IsValid || pawn.Map == null)
                return;

            var thingsAtCell = pawn.Map.thingGrid.ThingsAt(cell);
            if (thingsAtCell == null)
                return;

            _candidateBuffer.Clear();
            foreach (var thing in thingsAtCell)
            {
                // Skip forbidden items up front to avoid wasted evaluation
                if (thing != null && thing != pawn && !thing.IsForbidden(pawn) && IsValidTool(thing, workStat))
                    _candidateBuffer.Add(thing);
            }

            EvaluateCandidates(_candidateBuffer, pawn, workStat, currentScore, minGainPct, needsGatingRescue,
                               ToolLocation.SameCell, 1, ref bestCandidate);
        }

        private static void SearchStockpiles(Pawn pawn, StatDef workStat, float currentScore, float minGainPct, bool needsGatingRescue, float radius, int pathCostBudget, ref ToolCandidate bestCandidate)
        {
            if (pawn.Map?.zoneManager?.AllZones == null)
                return;

            _stockpileBuffer.Clear();
            var zones = pawn.Map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                var zone = zones[i] as Zone_Stockpile;
                if (zone?.AllContainedThings == null)
                    continue;

                foreach (var thing in zone.AllContainedThings)
                {
                    // Skip forbidden items up front to avoid wasted evaluation
                    if (thing != null && !thing.IsForbidden(pawn) && IsValidTool(thing, workStat) &&
                        IsWithinRadius(pawn.Position, thing.Position, radius))
                    {
                        _stockpileBuffer.Add(thing);
                    }
                }
            }

            EvaluateCandidatesWithPathCost(_stockpileBuffer, pawn, workStat, currentScore, minGainPct, needsGatingRescue,
                                          ToolLocation.Stockpile, pathCostBudget, ref bestCandidate);
        }

        private static void SearchHomeArea(Pawn pawn, StatDef workStat, float currentScore, float minGainPct, bool needsGatingRescue, float radius, int pathCostBudget, ref ToolCandidate bestCandidate)
        {
            if (pawn.Map?.areaManager?.Home == null)
                return;

            var homeArea = pawn.Map.areaManager.Home;
            var listerThings = pawn.Map.listerThings;

            // Search tools in home area
            var toolDefs = GetRelevantToolDefs(workStat);
            _candidateBuffer.Clear();

            for (int i = 0; i < toolDefs.Count; i++)
            {
                var toolDef = toolDefs[i];
                var thingsOfDef = listerThings.ThingsOfDef(toolDef);

                for (int j = 0; j < thingsOfDef.Count; j++)
                {
                    var thing = thingsOfDef[j];
                    // Skip forbidden items up front to avoid wasted evaluation
                    if (thing != null && !thing.IsForbidden(pawn) && homeArea[thing.Position] &&
                        IsWithinRadius(pawn.Position, thing.Position, radius) &&
                        IsValidTool(thing, workStat))
                    {
                        _candidateBuffer.Add(thing);
                    }
                }
            }

            EvaluateCandidatesWithPathCost(_candidateBuffer, pawn, workStat, currentScore, minGainPct, needsGatingRescue,
                                          ToolLocation.HomeArea, pathCostBudget, ref bestCandidate);
        }

        private static void SearchNearby(Pawn pawn, StatDef workStat, float currentScore, float minGainPct, bool needsGatingRescue, float radius, int pathCostBudget, ref ToolCandidate bestCandidate)
        {
            if (pawn.Map?.listerThings == null)
                return;

            var listerThings = pawn.Map.listerThings;
            var toolDefs = GetRelevantToolDefs(workStat);

            _candidateBuffer.Clear();
            for (int i = 0; i < toolDefs.Count; i++)
            {
                var toolDef = toolDefs[i];
                var thingsOfDef = listerThings.ThingsOfDef(toolDef);

                for (int j = 0; j < thingsOfDef.Count; j++)
                {
                    var thing = thingsOfDef[j];
                    // Skip forbidden items up front to avoid wasted evaluation
                    if (thing != null && !thing.IsForbidden(pawn) && IsWithinRadius(pawn.Position, thing.Position, radius) &&
                        IsValidTool(thing, workStat))
                    {
                        _candidateBuffer.Add(thing);
                    }
                }
            }

            EvaluateCandidatesWithPathCost(_candidateBuffer, pawn, workStat, currentScore, minGainPct, needsGatingRescue,
                                          ToolLocation.Nearby, pathCostBudget, ref bestCandidate);
        }

        private static void EvaluateCandidates(List<Thing> candidates, Pawn pawn, StatDef workStat, float currentScore,
                                             float minGainPct, bool needsGatingRescue, ToolLocation location, int pathCost,
                                             ref ToolCandidate bestCandidate)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                var tool = candidates[i];
                if (ShouldSkipCandidate(pawn, tool))
                    continue;
                if (!CanPawnReserveAndReach(pawn, tool))
                    continue;

                var score = ToolScoring.Score(tool, pawn, workStat);
                var gainPct = (score - currentScore) / Math.Max(currentScore, 0.001f); if (ShouldConsiderTool(score, currentScore, gainPct, minGainPct, needsGatingRescue))
                {
                    if (bestCandidate.tool == null || score > bestCandidate.score ||
                        (Math.Abs(score - bestCandidate.score) < 0.001f && location < bestCandidate.location))
                    {
                        bestCandidate = new ToolCandidate
                        {
                            tool = tool,
                            score = score,
                            gainPct = gainPct,
                            pathCost = pathCost,
                            location = location
                        };
                    }
                }
            }
        }

        private static void EvaluateCandidatesWithPathCost(List<Thing> candidates, Pawn pawn, StatDef workStat, float currentScore,
                                                          float minGainPct, bool needsGatingRescue, ToolLocation location, int pathCostBudget,
                                                          ref ToolCandidate bestCandidate)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                var tool = candidates[i];
                if (ShouldSkipCandidate(pawn, tool))
                    continue;
                if (!CanPawnReserveAndReach(pawn, tool))
                    continue;

                int pathCost = GetPathCost(pawn, tool);
                if (pathCost > pathCostBudget)
                    continue;

                var score = ToolScoring.Score(tool, pawn, workStat);
                var gainPct = (score - currentScore) / Math.Max(currentScore, 0.001f); if (ShouldConsiderTool(score, currentScore, gainPct, minGainPct, needsGatingRescue))
                {
                    if (bestCandidate.tool == null || score > bestCandidate.score ||
                        (Math.Abs(score - bestCandidate.score) < 0.001f && pathCost < bestCandidate.pathCost))
                    {
                        bestCandidate = new ToolCandidate
                        {
                            tool = tool,
                            score = score,
                            gainPct = gainPct,
                            pathCost = pathCost,
                            location = location
                        };
                    }
                }
            }
        }

        private static bool ShouldConsiderTool(float toolScore, float currentScore, float gainPct, float minGainPct, bool needsGatingRescue)
        {
            if (needsGatingRescue)
            {
                // For gating rescue, any improvement is good
                return toolScore > currentScore + GatingEpsilon;
            }

            // Normal case: require minimum gain percentage
            return gainPct >= minGainPct;
        }

        private static bool QueueAcquisitionJob(Pawn pawn, ToolCandidate candidate, StatDef requestedStat, QueuePriority priority, out bool acquisitionEnqueued)
        {
            acquisitionEnqueued = false;
            if (candidate.tool == null)
            {
                LogDebug($"QueueAcquisitionJob: candidate.tool is null for {pawn?.LabelShort}", "AssignmentSearch.NullTool");
                return false;
            }

            LogDebug($"QueueAcquisitionJob: attempting to queue job for {pawn.LabelShort} to acquire {candidate.tool.LabelShort} from {candidate.location} (priority={priority})", "AssignmentSearch.QueueAttempt");

            // If an equivalent queued acquisition job exists and pawn is idle, start it instead of creating a new one
            if (TryStartQueuedToolJobFor(pawn, candidate.tool, JobDefOf.Equip, JobDefOf.TakeInventory))
            {
                acquisitionEnqueued = true;
                // Purge any other duplicates for the same target
                RemoveQueuedToolJobsFor(pawn, candidate.tool, JobDefOf.Equip, JobDefOf.TakeInventory);
                return true;
            }

            // IMPORTANT: For Append (AI/opportunistic) path, do NOT enqueue while pawn is busy.
            // Ordered job queue doesn't auto-run; we'll retry later when pawn is idle.
            if (priority == QueuePriority.Append && pawn?.jobs?.curJob != null)
            {
                LogDebug($"Pawn {pawn.LabelShort} is busy with {pawn.jobs.curJob.def.defName}; skipping enqueue (Append). Will retry when idle.", "AssignmentSearch.SkipEnqueueWhenBusy");
                acquisitionEnqueued = false;
                return false;
            }

            // If a drop/acquisition is already pending in the queue, do not enqueue acquisition this pass
            if (HasPendingDropJob(pawn) || HasPendingAcquisitionJob(pawn))
            {
                LogDebug($"QueueAcquisitionJob: tool-management pending for {pawn.LabelShort} — deferring acquisition", "AssignmentSearch.DeferAcquireForPendingQueue");
                // handled this pass by deferring; no acquisition enqueued
                acquisitionEnqueued = false;
                return true;
            }

            // ENHANCED VALIDATION: Real-time tool state check before any job creation
            if (!ValidateToolStateForAcquisition(pawn, candidate.tool))
            {
                LogDebug($"Tool validation failed for {candidate.tool.LabelShort} and {pawn.LabelShort}", "AssignmentSearch.ToolValidationFailed");
                return false;
            }

            // Check carry limits BEFORE creating jobs and try to make room
            if (!CanCarryAdditionalTool(pawn))
            {
                LogDebug($"Carry limit reached for {pawn.LabelShort}, need to drop worst tool first", "AssignmentSearch.CarryLimit");

                // Try to drop worst tool first - this must complete before acquisition
                Thing worstTool = null;
                try
                {
                    // If carry-limit is effectively 1, protect the best tool for the requested stat to avoid ping-pong
                    var settings = SurvivalTools.Settings;
                    int carryLimit = GetCarryLimit(settings);
                    if (HasToolbelt(pawn)) carryLimit = Math.Max(carryLimit, 3);
                    if (carryLimit <= 1 && requestedStat != null)
                    {
                        worstTool = FindWorstCarriedToolRespectingFocus(pawn, requestedStat);
                    }
                    else
                    {
                        worstTool = FindWorstCarriedTool(pawn);
                    }
                }
                catch { worstTool = FindWorstCarriedTool(pawn); }
                if (worstTool == null)
                {
                    LogDebug($"No worst tool found to drop for {pawn.LabelShort}", "AssignmentSearch.NoWorstTool");
                    return false;
                }

                LogDebug($"Found worst tool to drop: {worstTool.LabelShort} for {pawn.LabelShort}", "AssignmentSearch.FoundWorstTool");

                // Validate worst tool before trying to drop it
                if (!ValidateToolStateForDrop(pawn, worstTool))
                {
                    LogDebug($"Worst tool validation failed for {worstTool.LabelShort} and {pawn.LabelShort}", "AssignmentSearch.WorstToolValidationFailed");
                    return false;
                }

                // For Append path while busy, skip queuing drop and retry later when idle
                if (priority == QueuePriority.Append && pawn?.jobs?.curJob != null)
                {
                    LogDebug($"Busy with {pawn.jobs.curJob.def.defName}; skipping drop enqueue (Append).", "AssignmentSearch.SkipDropWhenBusy");
                    acquisitionEnqueued = false;
                    return false;
                }

                // Queue drop job ONLY – acquisition will be re-attempted on next tick/job pass
                if (!QueueDropJob(pawn, worstTool))
                {
                    LogDebug($"Failed to queue drop job for {worstTool.LabelShort} by {pawn.LabelShort}", "AssignmentSearch.DropJobFailed");
                    return false;
                }

                LogDebug($"Successfully queued drop job for {worstTool.LabelShort} by {pawn.LabelShort}", "AssignmentSearch.DropJobSuccess");
                // Defer acquisition until after drop completes to avoid queue ordering issues
                acquisitionEnqueued = false;
                return true;
            }

            Job job = null;

            LogDebug($"[SurvivalTools.Assignment] Creating acquisition job for {candidate.tool.LabelShort} at {candidate.location}", $"Assign.CreateJob.Acq|{pawn.ThingID}|{candidate.tool.thingIDNumber}");

            switch (candidate.location)
            {
                case ToolLocation.Inventory:
                    // Already in inventory, just equip
                    job = JobMaker.MakeJob(JobDefOf.Equip, candidate.tool);
                    LogDebug($"Created Equip job for {pawn.LabelShort} (tool in inventory)", "AssignmentSearch.EquipInventory");
                    break;

                case ToolLocation.Equipment:
                case ToolLocation.SameCell:
                    // Pick up and equip
                    job = JobMaker.MakeJob(JobDefOf.Equip, candidate.tool);
                    LogDebug($"Created Equip job for {pawn.LabelShort} (tool at {candidate.location})", "AssignmentSearch.EquipLocation");
                    break;

                default:
                    // Haul to inventory first
                    job = JobMaker.MakeJob(JobDefOf.TakeInventory, candidate.tool);
                    job.count = 1;
                    LogDebug($"Created TakeInventory job for {pawn.LabelShort} (tool at {candidate.location})", "AssignmentSearch.TakeInventory");
                    break;
            }

            if (job == null)
            {
                LogDebug($"Failed to create job for {pawn.LabelShort}", "AssignmentSearch.JobCreationFailed");
                return false;
            }

            // ENHANCED VALIDATION: Use JobUtils to validate job before queuing
            if (!JobUtils.IsJobStillValid(job, pawn))
            {
                LogDebug($"Acquisition job {job.def.defName} is invalid for {pawn.LabelShort}, not queuing", "AssignmentSearch.AcquisitionJobInvalid");
                return false;
            }

            try
            {
                // SAFETY: Clone job for queue to prevent reference issues
                var clonedJob = JobUtils.CloneJobForQueue(job);
                // Make this behave like a player-forced ordered job so it starts immediately when idle
                clonedJob.playerForced = true;

                // ENHANCED VALIDATION: Double-check tool state right before reservation
                if (!ValidateToolStateForAcquisition(pawn, candidate.tool))
                {
                    LogDebug($"Tool validation failed right before reservation for {candidate.tool.LabelShort} and {pawn.LabelShort}", "AssignmentSearch.PreReservationValidationFailed");
                    return false;
                }

                // Reserve using the cloned job instance that will actually run
                if (!pawn.Reserve(candidate.tool, clonedJob, 1, -1, null, true))
                {
                    LogDebug($"Failed to reserve {candidate.tool.LabelShort} for {pawn.LabelShort}", "AssignmentSearch.ReserveFailed");
                    return false;
                }

                // If pawn is idle, start immediately to avoid queue starvation
                if (pawn.jobs?.curJob == null)
                {
                    bool taken = pawn.jobs.TryTakeOrderedJob(clonedJob);
                    if (taken)
                    {
                        LogDebug($"[SurvivalTools.Assignment] Started {clonedJob.def.defName} immediately for {pawn.LabelShort} targeting {candidate.tool.LabelShort}", $"Assign.StartImmediate|{pawn.ThingID}|{candidate.tool.thingIDNumber}|{clonedJob.def?.defName}");
                        // Remove any duplicate queued acquisition jobs for this target
                        RemoveQueuedToolJobsFor(pawn, candidate.tool, JobDefOf.Equip, JobDefOf.TakeInventory);
                        // Protect the just-acquired tool from being dropped immediately
                        try
                        {
                            int nowTick = Find.TickManager?.TicksGame ?? 0;
                            _recentAcquisitions[pawn.thingIDNumber] = new RecentAcqData { untilTick = nowTick + FocusTicksWindow, thingID = candidate.tool.thingIDNumber };
                            LogDebug($"Set recent-acquisition protect for {pawn.LabelShort} on {candidate.tool.LabelShort} for {FocusTicksWindow} ticks", "AssignmentSearch.SetRecentAcq");
                        }
                        catch { /* best-effort */ }
                        acquisitionEnqueued = true;
                        return true;
                    }
                    // If failed to start now, fall back to enqueue with priority
                }

                // Enqueue according to requested priority to avoid preempting AI-selected jobs
                if (priority == QueuePriority.Front)
                {
                    // Avoid enqueueing duplicates
                    if (!TryStartQueuedToolJobFor(pawn, candidate.tool, JobDefOf.Equip, JobDefOf.TakeInventory))
                        pawn.jobs?.jobQueue?.EnqueueFirst(clonedJob, JobTag.Misc);
                }
                else
                {
                    // For Append and idle=false case we should never reach here due to early return.
                    // Still avoid enqueueing duplicates in case of future changes
                    pawn.jobs?.jobQueue?.EnqueueLast(clonedJob, JobTag.Misc);
                }
                LogDebug($"[SurvivalTools.Assignment] Successfully enqueued {clonedJob.def.defName} job for {pawn.LabelShort} targeting {candidate.tool.LabelShort}", $"Assign.Enqueued|{pawn.ThingID}|{candidate.tool.thingIDNumber}|{clonedJob.def?.defName}");
                // Protect the just-enqueued acquisition target as "recently acquired" to avoid immediate drop
                try
                {
                    int nowTick2 = Find.TickManager?.TicksGame ?? 0;
                    _recentAcquisitions[pawn.thingIDNumber] = new RecentAcqData { untilTick = nowTick2 + FocusTicksWindow, thingID = candidate.tool.thingIDNumber };
                    LogDebug($"Set recent-acquisition protect (enqueued) for {pawn.LabelShort} on {candidate.tool.LabelShort} for {FocusTicksWindow} ticks", "AssignmentSearch.SetRecentAcq.Enqueued");
                }
                catch { /* best-effort */ }
                acquisitionEnqueued = true;
                return true;
            }
            catch (Exception ex)
            {
                LogError($"[SurvivalTools.Assignment] Exception enqueueing job for {pawn.LabelShort}: {ex}");

                // Clean up reservation on failure
                try
                {
                    pawn.Map?.reservationManager?.Release(candidate.tool, pawn, job);
                }
                catch (Exception releaseEx)
                {
                    LogError($"[SurvivalTools.Assignment] Exception releasing reservation: {releaseEx}");
                }

                // FALLBACK: Try direct tool swapping if job queuing fails
                LogWarning($"[SurvivalTools.Assignment] Job queuing failed, attempting direct tool swap fallback");
                var swap = TryDirectToolSwap(pawn, candidate);
                acquisitionEnqueued = swap;
                return swap;
            }
        }

        private static bool CanCarryAdditionalTool(Pawn pawn)
        {
            var settings = SurvivalTools.Settings;
            if (settings == null)
                return true;

            int carryLimit = GetCarryLimit(settings);
            if (HasToolbelt(pawn))
                carryLimit = Math.Max(carryLimit, 3); // Toolbelt exception

            int currentTools = CountCarriedTools(pawn);

            LogDebug($"CanCarryAdditionalTool for {pawn.LabelShort}: current={currentTools}, limit={carryLimit}, canCarry={currentTools < carryLimit}", "AssignmentSearch.CarryCheck");

            return currentTools < carryLimit;
        }

        private static int GetCarryLimit(SurvivalToolsSettings settings)
        {
            if (settings.extraHardcoreMode)
                return 1; // Nightmare
            if (settings.hardcoreMode)
                return 2; // Hardcore
            return 3; // Normal
        }

        private static bool HasToolbelt(Pawn pawn)
        {
            // Stub: returns false unless specific apparel tag/comp detected
            // TODO: Implement toolbelt detection when available
            return false;
        }

        private static int CountCarriedTools(Pawn pawn)
        {
            int count = 0;

            // Count inventory tools
            var inventory = pawn.inventory?.innerContainer;
            if (inventory != null)
            {
                for (int i = 0; i < inventory.Count; i++)
                {
                    var thing = inventory[i];
                    if (thing != null && IsSurvivalTool(thing))
                    {
                        count++;
                        LogDebug($"[SurvivalTools.Assignment] Counted inventory tool for {pawn.LabelShort}: {thing.LabelShort}", $"Assign.Count.Inv|{pawn.ThingID}|{thing.thingIDNumber}");
                    }
                }
            }

            // Count equipped tools
            var equipment = pawn.equipment?.AllEquipmentListForReading;
            if (equipment != null)
            {
                for (int i = 0; i < equipment.Count; i++)
                {
                    var thing = equipment[i];
                    if (thing != null && IsSurvivalTool(thing))
                    {
                        count++;
                        LogDebug($"[SurvivalTools.Assignment] Counted equipped tool for {pawn.LabelShort}: {thing.LabelShort}", $"Assign.Count.Eqp|{pawn.ThingID}|{thing.thingIDNumber}");
                    }
                }
            }

            LogDebug($"Total carried tools for {pawn.LabelShort}: {count}", "AssignmentSearch.ToolCount");
            return count;
        }

        private static bool TryDropWorstTool(Pawn pawn)
        {
            Thing worstTool = FindWorstCarriedTool(pawn);
            if (worstTool == null)
                return false;

            return QueueDropJob(pawn, worstTool);
        }

        /// <summary>
        /// Queue a job to drop/store a specific tool.
        /// Enhanced with comprehensive validation and proper job cloning.
        /// </summary>
        private static bool QueueDropJob(Pawn pawn, Thing toolToDrop)
        {
            if (toolToDrop == null || pawn == null)
                return false;

            LogDebug($"[SurvivalTools.Assignment] QueueDropJob: {pawn.LabelShort} attempting to drop {toolToDrop.LabelShort}", $"Assign.QueueDrop|{pawn.ThingID}|{toolToDrop.thingIDNumber}");

            // ENHANCED VALIDATION: Use new validation method
            if (!ValidateToolStateForDrop(pawn, toolToDrop))
            {
                LogWarning($"[SurvivalTools.Assignment] QueueDropJob: Validation failed for {toolToDrop.LabelShort} and {pawn.LabelShort}");
                return false;
            }

            LogDebug($"QueueDropJob: {pawn.LabelShort} dropping {toolToDrop.LabelShort}", "AssignmentSearch.QueueDrop");

            // If an equivalent queued drop job exists and pawn is idle, start it instead of creating a new one
            if (TryStartQueuedToolJobFor(pawn, toolToDrop, ST_JobDefOf.DropSurvivalTool, JobDefOf.DropEquipment))
            {
                // Purge any other duplicates for this target
                RemoveQueuedToolJobsFor(pawn, toolToDrop, ST_JobDefOf.DropSurvivalTool, JobDefOf.DropEquipment);
                return true;
            }

            // Check what type of container the tool is in to determine drop method
            bool isInInventory = pawn.inventory?.innerContainer?.Contains(toolToDrop) == true;
            bool isEquipped = pawn.equipment?.AllEquipmentListForReading?.Contains(toolToDrop) == true;

            LogDebug($"[SurvivalTools.Assignment] Tool location: isInInventory={isInInventory}, isEquipped={isEquipped}", $"Assign.ToolLoc|{pawn.ThingID}|{toolToDrop.thingIDNumber}");

            // Prefer legacy custom drop flow for correctness with unspawned inventory items.
            Job dropJob = null;
            if (isEquipped)
            {
                // Equipped tools use vanilla DropEquipment which handles equipment trackers correctly
                dropJob = JobMaker.MakeJob(JobDefOf.DropEquipment, toolToDrop);
                LogDebug($"Created DropEquipment job for {pawn.LabelShort}", "AssignmentSearch.DropEquipment");
            }
            else if (isInInventory)
            {
                // Inventory-held tools should use our legacy driver that safely removes from ThingOwner
                dropJob = JobMaker.MakeJob(ST_JobDefOf.DropSurvivalTool, toolToDrop);
                dropJob.count = 1;
                LogDebug($"Created DropSurvivalTool job for {pawn.LabelShort}", "AssignmentSearch.DropSurvivalTool");
            }
            else
            {
                // If neither, try a gentle haul to nearest stockpile only when the thing is spawned
                var stockpile = FindNearestStockpileFor(pawn, toolToDrop);
                if (toolToDrop.Spawned && stockpile != null)
                {
                    var targetCell = stockpile.Cells.FirstOrDefault();
                    if (targetCell.IsValid && targetCell.InBounds(pawn.Map))
                    {
                        dropJob = JobMaker.MakeJob(JobDefOf.HaulToCell, toolToDrop, targetCell);
                        dropJob.count = 1;
                        LogDebug($"Created HaulToCell job to stockpile for {pawn.LabelShort}", "AssignmentSearch.HaulToStockpile");
                    }
                }

                if (dropJob == null)
                {
                    LogWarning($"[SurvivalTools.Assignment] Tool {toolToDrop.LabelShort} not in inventory/equipment for {pawn.LabelShort}");
                    return false;
                }
            }

            if (dropJob == null)
            {
                LogDebug($"Failed to create drop job for {pawn.LabelShort}", "AssignmentSearch.DropJobCreationFailed");
                return false;
            }

            // ENHANCED VALIDATION: Use JobUtils to validate job before queuing
            if (!JobUtils.IsJobStillValid(dropJob, pawn))
            {
                LogDebug($"Drop job {dropJob.def.defName} is invalid for {pawn.LabelShort}, not queuing", "AssignmentSearch.DropJobInvalid");
                return false;
            }

            // Reservation is not required for DropSurvivalTool/DropEquipment; HaulToCell handles its own when applicable.

            try
            {
                // SAFETY: Clone job for queue to prevent reference issues
                var clonedJob = JobUtils.CloneJobForQueue(dropJob);
                clonedJob.playerForced = true;

                // If pawn is idle, start immediately
                if (pawn.jobs?.curJob == null)
                {
                    bool taken = pawn.jobs.TryTakeOrderedJob(clonedJob);
                    if (taken)
                    {
                        Log.Message($"[SurvivalTools.Assignment] Started {clonedJob.def.defName} immediately for {pawn.LabelShort} to drop {toolToDrop.LabelShort}");
                        // Remove any duplicate queued drop jobs for this target
                        RemoveQueuedToolJobsFor(pawn, toolToDrop, ST_JobDefOf.DropSurvivalTool, JobDefOf.DropEquipment);
                        return true;
                    }
                }

                // Otherwise queue drop job with higher priority (it should happen first)
                // Avoid adding duplicates
                var jq = pawn.jobs?.jobQueue;
                bool alreadyQueued = false;
                if (jq != null)
                {
                    for (int i = 0; i < jq.Count; i++)
                    {
                        var j = jq[i]?.job;
                        if (j?.def == clonedJob.def && j.targetA.Thing != null && j.targetA.Thing.thingIDNumber == toolToDrop.thingIDNumber)
                        { alreadyQueued = true; break; }
                    }
                }
                if (!alreadyQueued)
                {
                    pawn.jobs?.jobQueue?.EnqueueFirst(clonedJob, JobTag.Misc);
                }
                LogDebug($"[SurvivalTools.Assignment] Successfully queued {clonedJob.def.defName} job for {pawn.LabelShort} to drop {toolToDrop.LabelShort}", $"Assign.EnqueuedDrop|{pawn.ThingID}|{toolToDrop.thingIDNumber}|{clonedJob.def?.defName}");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"[SurvivalTools.Assignment] Exception queueing drop job for {pawn.LabelShort}: {ex}");
                return false;
            }
        }

        private static Thing FindWorstCarriedTool(Pawn pawn)
        {
            Thing worstTool = null;
            float worstScore = float.MaxValue;
            int now = Find.TickManager?.TicksGame ?? 0;
            int protectedThingId = -1;
            if (_recentAcquisitions.TryGetValue(pawn.thingIDNumber, out var acq) && now < acq.untilTick)
                protectedThingId = acq.thingID;

            // Check inventory tools
            var inventory = pawn.inventory?.innerContainer;
            if (inventory != null)
            {
                for (int i = 0; i < inventory.Count; i++)
                {
                    var thing = inventory[i];
                    if (thing != null && IsSurvivalTool(thing))
                    {
                        if (protectedThingId == thing.thingIDNumber) continue; // don't drop just-acquired
                        float score = GetOverallToolScore(pawn, thing);
                        LogDebug($"[SurvivalTools.Assignment] Evaluating inventory tool {thing.LabelShort} for {pawn.LabelShort}: score={score:F3}", $"Assign.ScoreInv|{pawn.ThingID}|{thing.thingIDNumber}");
                        if (score < worstScore)
                        {
                            worstScore = score;
                            worstTool = thing;
                        }
                    }
                }
            }

            // Check equipped tools
            var equipment = pawn.equipment?.AllEquipmentListForReading;
            if (equipment != null)
            {
                for (int i = 0; i < equipment.Count; i++)
                {
                    var thing = equipment[i];
                    if (thing != null && IsSurvivalTool(thing))
                    {
                        if (protectedThingId == thing.thingIDNumber) continue; // don't drop just-acquired
                        float score = GetOverallToolScore(pawn, thing);
                        LogDebug($"[SurvivalTools.Assignment] Evaluating equipped tool {thing.LabelShort} for {pawn.LabelShort}: score={score:F3}", $"Assign.ScoreEq|{pawn.ThingID}|{thing.thingIDNumber}");
                        if (score < worstScore)
                        {
                            worstScore = score;
                            worstTool = thing;
                        }
                    }
                }
            }

            if (worstTool != null)
            {
                LogDebug($"FindWorstCarriedTool for {pawn.LabelShort}: {worstTool.LabelShort} (score: {worstScore:F3})", "AssignmentSearch.WorstTool");
            }
            else
            {
                LogDebug($"FindWorstCarriedTool for {pawn.LabelShort}: no tools found", "AssignmentSearch.NoWorstTool");
            }

            return worstTool;
        }

        // Avoid dropping the tool that is best for the requested focus stat when carry-limit is 1
        private static Thing FindWorstCarriedToolRespectingFocus(Pawn pawn, StatDef focusStat)
        {
            Thing bestForFocus = null;
            if (focusStat != null)
            {
                try
                {
                    bestForFocus = ToolScoring.GetBestTool(pawn, focusStat, out float _);
                }
                catch { bestForFocus = null; }
            }

            Thing worstTool = null;
            float worstScore = float.MaxValue;
            int now = Find.TickManager?.TicksGame ?? 0;
            int protectedThingId = -1;
            if (_recentAcquisitions.TryGetValue(pawn.thingIDNumber, out var acq) && now < acq.untilTick)
                protectedThingId = acq.thingID;

            // Check inventory tools
            var inventory = pawn.inventory?.innerContainer;
            if (inventory != null)
            {
                for (int i = 0; i < inventory.Count; i++)
                {
                    var thing = inventory[i];
                    if (thing != null && IsSurvivalTool(thing))
                    {
                        // Protect best-for-focus tool from being selected as worst
                        if (bestForFocus != null && TargetsSameTool(pawn, thing, bestForFocus))
                            continue;
                        if (protectedThingId == thing.thingIDNumber) continue; // don't drop just-acquired
                        float score = GetOverallToolScore(pawn, thing);
                        if (score < worstScore)
                        {
                            worstScore = score;
                            worstTool = thing;
                        }
                    }
                }
            }

            // Check equipped tools
            var equipment = pawn.equipment?.AllEquipmentListForReading;
            if (equipment != null)
            {
                for (int i = 0; i < equipment.Count; i++)
                {
                    var thing = equipment[i];
                    if (thing != null && IsSurvivalTool(thing))
                    {
                        if (bestForFocus != null && TargetsSameTool(pawn, thing, bestForFocus))
                            continue;
                        if (protectedThingId == thing.thingIDNumber) continue; // don't drop just-acquired
                        float score = GetOverallToolScore(pawn, thing);
                        if (score < worstScore)
                        {
                            worstScore = score;
                            worstTool = thing;
                        }
                    }
                }
            }

            if (worstTool != null)
            {
                LogDebug($"FindWorstCarriedToolRespectingFocus for {pawn.LabelShort}: {worstTool.LabelShort} (score: {worstScore:F3})", "AssignmentSearch.WorstTool.RespectingFocus");
            }
            else
            {
                LogDebug($"FindWorstCarriedToolRespectingFocus for {pawn.LabelShort}: no droppable tool found (protecting best for {focusStat?.defName})", "AssignmentSearch.NoWorstTool.RespectingFocus");
            }

            return worstTool;
        }

        private static Zone_Stockpile FindNearestStockpileFor(Pawn pawn, Thing thing)
        {
            if (pawn.Map?.zoneManager?.AllZones == null)
                return null;

            Zone_Stockpile nearest = null;
            float nearestDist = float.MaxValue;

            var zones = pawn.Map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                var stockpile = zones[i] as Zone_Stockpile;
                if (stockpile?.settings?.AllowedToAccept(thing) == true)
                {
                    float dist = pawn.Position.DistanceTo(stockpile.Cells.FirstOrDefault());
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = stockpile;
                    }
                }
            }

            return nearest;
        }

        private static bool IsValidTool(Thing thing, StatDef workStat)
        {
            if (thing == null || workStat == null)
                return false;

            // Accept real tools immediately.
            if (thing is SurvivalTool || (thing.def?.IsSurvivalTool() == true))
                return true;

            // For virtual tool-stuff (e.g., cloth/wood/leather), only accept if it actually
            // improves the requested workStat according to ToolStatResolver.
            if (thing.def?.IsToolStuff() == true)
            {
                float baseline = SurvivalToolUtility.GetNoToolBaseline(workStat);
                float factor = ToolStatResolver.GetToolStatFactor(thing.def, thing.Stuff, workStat);
                return factor > baseline + GatingEpsilon;
            }

            return false;
        }

        private static bool IsSurvivalTool(Thing thing)
        {
            return thing is SurvivalTool || (thing?.def?.IsSurvivalTool() == true);
        }

        private static bool CanPawnReserveAndReach(Pawn pawn, Thing thing)
        {
            if (thing == null || pawn == null)
                return false;

            if (!pawn.CanReserveAndReach(thing, PathEndMode.Touch, Danger.None))
                return false;

            return true;
        }

        private static bool IsWithinRadius(IntVec3 center, IntVec3 target, float radius)
        {
            return center.DistanceTo(target) <= radius;
        }

        private static int GetPathCost(Pawn pawn, Thing thing)
        {
            if (pawn?.Map == null || thing == null)
                return int.MaxValue;

            // Simple approximation: distance * average movement cost
            float distance = pawn.Position.DistanceTo(thing.Position);

            // Estimate movement cost - most terrain is walkable at moderate cost
            int estimatedMoveCost = 13; // Default movement cost for most terrain

            return (int)(distance * estimatedMoveCost);
        }

        // QUICK FILTERS ------------------------------------------------------
        private static bool ShouldSkipCandidate(Pawn pawn, Thing tool)
        {
            if (tool == null) return true;
            // Cooldown check
            int now = Find.TickManager?.TicksGame ?? 0;
            if (_candidateCooldownTicks.TryGetValue(tool.thingIDNumber, out int until) && now < until)
                return true;
            // Forbidden fast-path
            if (pawn != null && tool.IsForbidden(pawn))
                return true;
            return false;
        }

        private static void SetCandidateCooldown(Thing tool)
        {
            try
            {
                if (tool == null) return;
                int now = Find.TickManager?.TicksGame ?? 0;
                _candidateCooldownTicks[tool.thingIDNumber] = now + CandidateCooldownTicks;
            }
            catch { /* best-effort */ }
        }

        private static bool HasPendingDropJob(Pawn pawn)
        {
            try
            {
                // Consider current job
                var cur = pawn?.jobs?.curJob;
                var curDef = cur?.def;
                if (curDef == ST_JobDefOf.DropSurvivalTool || curDef == JobDefOf.DropEquipment)
                {
                    LogDebug($"HasPendingDropJob: current job is drop for {pawn?.LabelShort}", "AssignmentSearch.PendingDrop");
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool HasPendingAcquisitionJob(Pawn pawn)
        {
            try
            {
                // Consider current job
                var cur = pawn?.jobs?.curJob;
                var curDef = cur?.def;
                if (curDef == JobDefOf.Equip || curDef == JobDefOf.TakeInventory)
                {
                    LogDebug($"HasPendingAcquisitionJob: current job is acquisition for {pawn?.LabelShort}", "AssignmentSearch.PendingAcquire");
                    return true;
                }
            }
            catch { }
            return false;
        }

        // HYSTERESIS ---------------------------------------------------------
        private static bool IsInHysteresis(int pawnID, int currentTick, string candidateDefName, float candidateGainPct, float minGainPct)
        {
            if (!_hysteresisData.TryGetValue(pawnID, out var data))
                return false;

            int ticksSinceUpgrade = currentTick - data.lastUpgradeTick;
            if (ticksSinceUpgrade < HysteresisTicksNormal)
            {
                // If we are trying to equip the same def again within the window,
                // require an extra gain to avoid thrashing.
                if (!string.IsNullOrEmpty(candidateDefName) && candidateDefName == data.lastEquippedDefName)
                {
                    return candidateGainPct < (minGainPct + HysteresisExtraGainPct);
                }
            }

            return false;
        }
        private static float GetOverallToolScore(Pawn pawn, Thing tool)
        {
            // Simple heuristic: average score across common work stats
            float totalScore = 0f;
            int statCount = 0;

            var commonStats = new[]
            {
                ST_StatDefOf.DiggingSpeed,
                StatDefOf.ConstructionSpeed,
                ST_StatDefOf.TreeFellingSpeed,
                ST_StatDefOf.PlantHarvestingSpeed
            };

            for (int i = 0; i < commonStats.Length; i++)
            {
                var stat = commonStats[i];
                if (stat != null && IsValidTool(tool, stat))
                {
                    totalScore += ToolScoring.Score(tool, pawn, stat);
                    statCount++;
                }
            }

            return statCount > 0 ? totalScore / statCount : 0f;
        }

        private static List<ThingDef> GetRelevantToolDefs(StatDef workStat)
        {
            // Return tool defs that could provide this work stat
            var result = new List<ThingDef>();

            foreach (var toolDef in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                // Only consider real tool defs; skip generic tool-stuff resources here to avoid false positives.
                if (toolDef?.IsSurvivalTool() == true)
                {
                    // Quick check if this tool type could provide the stat
                    var dummyTool = new SurvivalTool();
                    dummyTool.def = toolDef;

                    var factor = SurvivalToolUtility.GetToolProvidedFactor(dummyTool, workStat);
                    if (factor > SurvivalToolUtility.GetNoToolBaseline(workStat) + GatingEpsilon)
                    {
                        result.Add(toolDef);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Alternative approach: Direct tool swapping without job queuing.
        /// Used as fallback when job-based approach fails.
        /// </summary>
        private static bool TryDirectToolSwap(Pawn pawn, ToolCandidate candidate)
        {
            Log.Warning($"[SurvivalTools.Assignment] Attempting DIRECT tool swap for {pawn.LabelShort} with {candidate.tool.LabelShort}");

            try
            {
                // Validate tool state once more
                if (!ValidateToolStateForAcquisition(pawn, candidate.tool))
                {
                    Log.Warning($"[SurvivalTools.Assignment] Direct swap validation failed for {candidate.tool.LabelShort}");
                    return false;
                }

                // Handle carry limits by dropping worst tool first
                if (!CanCarryAdditionalTool(pawn))
                {
                    Thing worstTool = FindWorstCarriedTool(pawn);
                    if (worstTool != null && ValidateToolStateForDrop(pawn, worstTool))
                    {
                        Log.Warning($"[SurvivalTools.Assignment] Direct drop of worst tool: {worstTool.LabelShort}");
                        if (!TryDirectDropTool(pawn, worstTool))
                        {
                            Log.Warning($"[SurvivalTools.Assignment] Direct drop failed for {worstTool.LabelShort}");
                            return false;
                        }
                    }
                    else
                    {
                        Log.Warning($"[SurvivalTools.Assignment] Cannot make room for new tool");
                        return false;
                    }
                }

                // Try direct tool acquisition based on location
                bool success = false;
                switch (candidate.location)
                {
                    case ToolLocation.Inventory:
                        // Already in inventory, try to equip directly
                        success = TryDirectEquipFromInventory(pawn, candidate.tool);
                        break;

                    case ToolLocation.Equipment:
                        // Already equipped, no action needed
                        success = true;
                        Log.Warning($"[SurvivalTools.Assignment] Tool {candidate.tool.LabelShort} already equipped");
                        break;

                    case ToolLocation.SameCell:
                        // Pick up directly from ground
                        success = TryDirectPickupAndEquip(pawn, candidate.tool);
                        break;

                    default:
                        // Try to haul to inventory directly
                        success = TryDirectHaulToInventory(pawn, candidate.tool);
                        break;
                }

                if (success)
                {
                    Log.Message($"[SurvivalTools.Assignment] DIRECT tool swap succeeded for {pawn.LabelShort} with {candidate.tool.LabelShort}");

                    // Update hysteresis
                    int currentTick = Find.TickManager?.TicksGame ?? 0;
                    _hysteresisData[pawn.thingIDNumber] = new HysteresisData
                    {
                        lastUpgradeTick = currentTick,
                        lastEquippedDefName = candidate.tool.def.defName
                    };

                    // Notify score cache
                    ScoreCache.NotifyInventoryChanged(pawn);
                    ScoreCache.NotifyToolChanged(candidate.tool);

                    return true;
                }
                else
                {
                    Log.Warning($"[SurvivalTools.Assignment] DIRECT tool swap failed for {pawn.LabelShort} with {candidate.tool.LabelShort}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools.Assignment] Exception in direct tool swap: {ex}");
                return false;
            }
        }

        private static bool TryDirectDropTool(Pawn pawn, Thing tool)
        {
            try
            {
                bool isInInventory = pawn.inventory?.innerContainer?.Contains(tool) == true;
                bool isEquipped = pawn.equipment?.AllEquipmentListForReading?.Contains(tool) == true;

                if (isEquipped && tool is ThingWithComps equipmentTool)
                {
                    // Drop from equipment
                    if (pawn.equipment?.TryDropEquipment(equipmentTool, out ThingWithComps droppedTool, pawn.Position) == true)
                    {
                        Log.Message($"[SurvivalTools.Assignment] Directly dropped equipment: {tool.LabelShort}");
                        return true;
                    }
                }
                else if (isInInventory)
                {
                    // Drop from inventory
                    var dropped = pawn.inventory?.innerContainer?.TryDrop(tool, pawn.Position, pawn.Map, ThingPlaceMode.Near, out Thing droppedTool);
                    if (dropped == true)
                    {
                        Log.Message($"[SurvivalTools.Assignment] Directly dropped from inventory: {tool.LabelShort}");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools.Assignment] Exception in direct drop: {ex}");
                return false;
            }
        }

        private static bool TryDirectEquipFromInventory(Pawn pawn, Thing tool)
        {
            try
            {
                if (pawn.inventory?.innerContainer?.Contains(tool) == true && tool is ThingWithComps equipmentTool)
                {
                    // Try to equip directly from inventory
                    var transferred = pawn.equipment?.TryTransferEquipmentToContainer(equipmentTool, pawn.inventory.innerContainer);
                    if (transferred == true)
                    {
                        Log.Message($"[SurvivalTools.Assignment] Directly equipped from inventory: {tool.LabelShort}");
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools.Assignment] Exception in direct equip from inventory: {ex}");
                return false;
            }
        }

        private static bool TryDirectPickupAndEquip(Pawn pawn, Thing tool)
        {
            try
            {
                // Try to pick up and equip directly
                if (tool.Map == pawn.Map && pawn.CanReach(tool, PathEndMode.Touch, Danger.None))
                {
                    // Try to transfer to inventory first, then equip
                    var added = pawn.inventory?.innerContainer?.TryAdd(tool);
                    if (added == true)
                    {
                        Log.Message($"[SurvivalTools.Assignment] Directly picked up to inventory: {tool.LabelShort}");
                        return TryDirectEquipFromInventory(pawn, tool);
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools.Assignment] Exception in direct pickup: {ex}");
                return false;
            }
        }

        private static bool TryDirectHaulToInventory(Pawn pawn, Thing tool)
        {
            try
            {
                // For distant tools, we might not be able to do direct manipulation
                // This is a limitation of the direct approach
                Log.Warning($"[SurvivalTools.Assignment] Direct haul not supported for distant tool {tool.LabelShort}");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools.Assignment] Exception in direct haul: {ex}");
                return false;
            }
        }
    }
}
