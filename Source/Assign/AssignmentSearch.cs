
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
        private const float SameToolFamilyExtraGainPct = 0.20f; // +20% required for same family (e.g., hammer → hammer)
        private const float GatingEpsilon = 0.001f;
        private const int FocusTicksWindow = 600; // 10 seconds: prefer current work stat, avoid thrash
                                                  // Cooldown after performing tool management so we don't immediately re-enter assignment logic
        private const int ManagementCooldownTicks = 180; // 3 seconds @ 60 TPS
        private static readonly Dictionary<int, int> _managementCooldownUntil = new Dictionary<int, int>(64);

        // Cooldown for repeatedly failing candidates (forbidden, unreachable, reserved by others)
        // Key: ThingIDNumber -> nextAllowedTick
        private static readonly Dictionary<int, int> _candidateCooldownTicks = new Dictionary<int, int>(128);
        private const int CandidateCooldownTicks = 600; // 10 seconds (at 60 TPS)

        // Short-lived focus window to avoid cross-stat thrashing
        // pawnID -> (untilTick, statDefName)
        private static readonly Dictionary<int, FocusData> _statFocus = new Dictionary<int, FocusData>(64);

        // Recently acquired protection: pawnID -> (untilTick, lastAcquiredThingID)
        private static readonly Dictionary<int, RecentAcqData> _recentAcquisitions = new Dictionary<int, RecentAcqData>(64);

        // Recently dropped tools cooldown: prevents immediate re-pickup of just-dropped tools
        // Key: "pawnID_toolID" -> untilTick
        private static readonly Dictionary<string, int> _recentlyDroppedTools = new Dictionary<string, int>(64);
        private const int DroppedToolCooldownTicks = 1500; // 25 seconds @ 60 TPS - balance between preventing loops and minimizing idle time

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
                // Cache tick access
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
            // Hot path: don't log routine entry parameters

            // Hard scope guard: only player-controlled humanlikes.
            if (!SurvivalTools.Helpers.PawnEligibility.IsEligibleColonistHuman(pawn))
            {
                return false;
            }
            // Skip while eating (avoid churn during ingest sequence)
            if (pawn.CurJobDef == JobDefOf.Ingest)
            {
                return false;
            }
            // If no workStat provided or workStat has no gating baseline (unlikely), skip quickly.
            if (workStat == null)
            {
                return false;
            }

            // Early-out blacklist
            if (!CanPawnUpgrade(pawn))
            {
                // Hot path: don't log routine validation failures
                return false;
            }

            // If we recently focused a different work stat, skip to avoid tool thrashing
            if (IsBlockedByFocus(pawn.thingIDNumber, workStat?.defName))
            {
                // Hot path: don't log routine focus blocks
                return false;
            }

            // Management cooldown: if we just managed tools and we're no longer at baseline, allow work instead of more churn
            // Cache tick access for performance
            int nowTickCooldown = Find.TickManager?.TicksGame ?? 0;
            if (_managementCooldownUntil.TryGetValue(pawn.thingIDNumber, out int cooldownUntil) && nowTickCooldown < cooldownUntil)
            {
                if (workStat != null)
                {
                    float baselineCooldown = SurvivalToolUtility.GetNoToolBaseline(workStat);
                    ToolScoring.GetBestTool(pawn, workStat, out float curScoreCooldown);
                    if (curScoreCooldown > baselineCooldown + GatingEpsilon)
                    {
                        // Hot path: don't log routine cooldown skips
                        return false;
                    }
                }
                else
                {
                    // Hot path: don't log routine cooldown skips
                    return false;
                }
            }

            // Anti-recursion check
            int pawnID = pawn.thingIDNumber;
            if (_processingPawns.TryGetValue(pawnID, out bool processing) && processing)
            {
                // Hot path: don't log routine anti-recursion triggers
                return false;
            }

            try
            {
                _processingPawns[pawnID] = true;

                // Cache settings and current tick for performance
                var settings = SurvivalToolsMod.Settings;
                int currentTick = Find.TickManager?.TicksGame ?? 0;

                if (workStat == null)
                {
                    // Hot path: don't log routine validation failures
                    return false;
                }

                // Get current score and tool
                var currentTool = ToolScoring.GetBestTool(pawn, workStat, out float currentScore);
                string currentDefName = currentTool?.def?.defName ?? "none";
                // Hot path: don't log current tool state every time

                // If a drop or acquisition is already being performed as the CURRENT job, defer any new work this pass
                // Note: we intentionally IGNORE queued items here because queued ordered jobs do not auto-start.
                bool pendingDrop = HasPendingDropJob(pawn);
                bool pendingAcquire = HasPendingAcquisitionJob(pawn);
                if (pendingDrop || pendingAcquire)
                {
                    // Hot path: don't log routine pending job deferrals
                    // Do NOT signal success when nothing new was enqueued; this avoids repeated requeue loops in PreWork
                    return false;
                }

                // Find best candidate
                var candidate = FindBestCandidate(pawn, workStat, currentScore, minGainPct, radius, pathCostBudget);
                if (candidate.tool == null)
                {
                    // Hot path: don't log routine no-candidate failures
                    return false;
                }

                LogDebug($"Found candidate tool for {pawn.LabelShort}: {candidate.tool?.LabelShort ?? "null"} (score: {candidate.score:F3}, gain: {candidate.gainPct:P1}, location: {candidate.location})", "AssignmentSearch.FoundCandidate");
                // Hot path: don't log job queue state before queueing

                // ANTI-THRASHING: Check if candidate is same tool family as current tool
                // Require much higher gain to swap within same family (e.g., hammer → hammer)
                if (currentTool != null && candidate.tool != null && IsSameToolFamily(currentTool, candidate.tool))
                {
                    float requiredGain = minGainPct + SameToolFamilyExtraGainPct;
                    if (candidate.gainPct < requiredGain)
                    {
                        LogDebug($"Rejecting {candidate.tool.LabelShort} - same family as {currentTool.LabelShort}, gain {candidate.gainPct:P1} < required {requiredGain:P1}", "AssignmentSearch.SameFamilyBlock");
                        return false;
                    }
                }

                // Apply hysteresis AFTER selecting a concrete candidate so we can use its gain and def
                // Use cached currentTick from method entry
                var candDefName = candidate.tool.def?.defName ?? string.Empty;
                if (IsInHysteresis(pawn.thingIDNumber, currentTick, candDefName, candidate.gainPct, minGainPct))
                {
                    // Hot path: don't log routine hysteresis blocks
                    return false;
                }

                // Queue the job to acquire/equip the tool
                bool acquisitionEnqueued;
                if (QueueAcquisitionJob(pawn, candidate, workStat, priority, out acquisitionEnqueued))
                {
                    if (acquisitionEnqueued)
                    {
                        LogDebug($"Successfully queued acquisition job for {pawn.LabelShort}: {candidate.tool?.LabelShort ?? "null"}", "AssignmentSearch.QueueSuccess");

                        // Update hysteresis only when we actually enqueued an acquisition
                        _hysteresisData[pawn.thingIDNumber] = new HysteresisData
                        {
                            lastUpgradeTick = currentTick,
                            lastEquippedDefName = candidate.tool?.def?.defName ?? string.Empty
                        };

                        // Set a short focus window on this stat to prevent other stats from thrashing
                        SetFocus(pawn, workStat);

                        // Notify score cache
                        ScoreCache.NotifyInventoryChanged(pawn);
                        if (candidate.tool != null)
                            ScoreCache.NotifyToolChanged(candidate.tool);
                        // Hot path: don't log job queue state after every enqueue

                        _managementCooldownUntil[pawn.thingIDNumber] = currentTick + ManagementCooldownTicks;
                        return true;
                    }
                    else
                    {
                        // We queued a drop and deferred acquisition; report handled but don't update hysteresis yet
                        LogDebug($"Queued drop for {pawn.LabelShort} and deferred acquisition of {candidate.tool?.LabelShort ?? "null"}", "AssignmentSearch.DeferredAfterDrop");
                        // Set a short focus window to prioritize this stat; this avoids cross-stat thrashing.
                        SetFocus(pawn, workStat);
                        // Hot path: don't log job queue state after every enqueue
                        _managementCooldownUntil[pawn.thingIDNumber] = currentTick + ManagementCooldownTicks;
                        return true;
                    }
                }
                else
                {
                    LogDebug($"Failed to queue acquisition job for {pawn.LabelShort}: {candidate.tool?.LabelShort ?? "null"}", "AssignmentSearch.QueueFailed");
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
                // Block if another stat is currently focused (use ordinal comparison)
                return !string.Equals(focus.statDefName, statDefName, StringComparison.Ordinal);
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
                string key = $"Assign.JobQueue.{pawn?.ThingID}|{tag}";
                if (jq == null)
                {
                    LogDebug($"[SurvivalTools.Assignment][{tag}] jobQueue=null", key);
                    return;
                }
                int count = jq.Count;
                if (count == 0)
                {
                    LogDebug($"[SurvivalTools.Assignment][{tag}] jobQueue count=0", key);
                    return;
                }
                int maxEntries = 20;
                int shown = Math.Min(count, maxEntries);
                var sb = new System.Text.StringBuilder(64 + shown * 24);
                sb.Append($"[SurvivalTools.Assignment][{tag}] jobQueue count={count} :: ");
                for (int i = 0; i < shown; i++)
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
                if (count > shown) sb.Append($" | … +{count - shown} more");
                LogDebug(sb.ToString(), key);
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
            if (tool == null || tool.Destroyed)
            {
                LogDebug($"ValidateToolStateForAcquisition: {tool?.LabelShort ?? "null"} is destroyed or null", "AssignmentSearch.ValidationDestroyed");
                return false;
            }

            // Check if tool is on same map
            if (tool.Map != pawn.Map)
            {
                LogDebug($"ValidateToolStateForAcquisition: {tool?.LabelShort ?? "null"} is on different map", "AssignmentSearch.ValidationDifferentMap");
                if (tool != null) SetCandidateCooldown(tool);
                return false;
            }

            // Check if tool is held by another pawn (not stockpiles/ground)
            // Allow tools in stockpiles, on ground, or in our own inventory/equipment
            var parentHolder = tool.ParentHolder;
            if (parentHolder is Pawn_InventoryTracker inventoryTracker &&
                inventoryTracker.pawn != pawn)
            {
                LogDebug($"ValidateToolStateForAcquisition: {tool?.LabelShort ?? "null"} is held by another pawn ({inventoryTracker.pawn?.LabelShort ?? "null"})", "AssignmentSearch.ValidationHeldByPawn");
                if (tool != null) SetCandidateCooldown(tool);
                return false;
            }

            // Check if tool is equipped by another pawn
            if (parentHolder is Pawn_EquipmentTracker equipmentTracker &&
                equipmentTracker.pawn != pawn)
            {
                LogDebug($"ValidateToolStateForAcquisition: {tool?.LabelShort ?? "null"} is equipped by another pawn ({equipmentTracker.pawn?.LabelShort ?? "null"})", "AssignmentSearch.ValidationEquippedByPawn");
                if (tool != null) SetCandidateCooldown(tool);
                return false;
            }

            // Check if pawn can reserve and reach the tool (legacy approach)
            if (!pawn.CanReserveAndReach(tool, PathEndMode.OnCell, pawn.NormalMaxDanger()))
            {
                LogDebug($"ValidateToolStateForAcquisition: {pawn.LabelShort} cannot reserve and reach {tool?.LabelShort ?? "null"}", "AssignmentSearch.ValidationCannotReserveAndReach");
                if (tool != null) SetCandidateCooldown(tool);
                return false;
            }

            // Check if tool is forbidden
            if (tool.IsForbidden(pawn))
            {
                LogDebug($"ValidateToolStateForAcquisition: {tool?.LabelShort ?? "null"} is forbidden for {pawn.LabelShort}", "AssignmentSearch.ValidationForbidden");
                if (tool != null) SetCandidateCooldown(tool);
                return false;
            }

            LogDebug($"[SurvivalTools.Assignment] Tool validation PASSED for {pawn.LabelShort} acquiring {tool?.LabelShort ?? "null"}", $"Assign.ToolValidation.Acq|{pawn.ThingID}|{tool?.thingIDNumber ?? -1}");
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
            if (tool == null || tool.Destroyed)
            {
                LogDebug($"ValidateToolStateForDrop: {tool?.LabelShort ?? "null"} is destroyed or null", "AssignmentSearch.DropValidationDestroyed");
                return false;
            }

            // Check if tool is on same map (allow null for carried items)
            if (tool.Map != null && tool.Map != pawn.Map)
            {
                LogDebug($"ValidateToolStateForDrop: {tool?.LabelShort ?? "null"} is on different map", "AssignmentSearch.DropValidationDifferentMap");
                return false;
            }

            // Check if tool is actually carried by this pawn
            bool isInInventory = pawn.inventory?.innerContainer?.Contains(tool) == true;
            bool isEquipped = pawn.equipment?.AllEquipmentListForReading?.Contains(tool) == true;

            if (!isInInventory && !isEquipped)
            {
                LogDebug($"ValidateToolStateForDrop: {tool?.LabelShort ?? "null"} is not carried by {pawn.LabelShort}", "AssignmentSearch.DropValidationNotCarried");
                return false;
            }

            // Check if pawn is incapacitated (can't drop if downed)
            if (pawn.Downed || pawn.Dead)
            {
                LogDebug($"ValidateToolStateForDrop: {pawn.LabelShort} is downed or dead", "AssignmentSearch.DropValidationIncapacitated");
                return false;
            }

            LogDebug($"[SurvivalTools.Assignment] Drop validation PASSED for {pawn.LabelShort} dropping {tool?.LabelShort ?? "null"}", $"Assign.ToolValidation.Drop|{pawn.ThingID}|{tool?.thingIDNumber ?? -1}");
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
                if (pawn == null || tool == null || jobDefs == null || jobDefs.Length == 0) return false;
                var jobTracker = pawn.jobs; if (jobTracker == null) return false;
                var queue = jobTracker.jobQueue; if (queue == null) return false;
                if (jobTracker.curJob != null) return false; // only auto-start when idle

                // Snapshot queued jobs defensively (avoid mutation mid-iteration)
                var snapshot = new List<Job>(queue.Count);
                for (int i = 0; i < queue.Count; i++)
                {
                    var qi = queue[i];
                    var j = qi?.job;
                    if (j?.def == null) continue;
                    // Only consider requested job defs
                    bool defMatch = false;
                    for (int d = 0; d < jobDefs.Length; d++) { if (jobDefs[d] != null && j.def == jobDefs[d]) { defMatch = true; break; } }
                    if (!defMatch) continue;
                    // Extract target safely
                    Thing targetThing = null;
                    try { if (j.targetA.HasThing) targetThing = j.targetA.Thing; } catch { targetThing = null; }
                    if (targetThing == null) continue;
                    if (targetThing.DestroyedOrNull()) continue; // stale
                    if (!TargetsSameTool(pawn, targetThing, tool)) continue;
                    snapshot.Add(j);
                }

                if (snapshot.Count == 0) return false;

                // Select first non-stale job; clone + start
                for (int s = 0; s < snapshot.Count; s++)
                {
                    var jobToClone = snapshot[s];
                    if (jobToClone == null || jobToClone.def == null) continue;
                    Job cloned = null;
                    try { cloned = JobUtils.CloneJobForQueue(jobToClone); } catch { cloned = null; }
                    if (cloned == null || cloned.def == null) continue;
                    cloned.playerForced = true;
                    bool started = false;
                    try { started = jobTracker.TryTakeOrderedJob(cloned); }
                    catch (Exception ex)
                    { LogError($"[SurvivalTools.Assignment] Exception starting cloned queued job: {ex}"); started = false; }
                    if (!started) continue;
                    // Enhanced null safety for logging
                    string clonedDefName = cloned?.def?.defName ?? "(null)";
                    string toolLabel = tool?.LabelShort ?? "(null)";
                    int toolId = tool?.thingIDNumber ?? -1;
                    LogDebug($"[SurvivalTools.Assignment] Started queued {clonedDefName} immediately for {pawn.LabelShort} targeting {toolLabel}", $"Assign.StartQueued|{pawn.ThingID}|{toolId}|{clonedDefName}");
                    // Remove stale/duplicate queued jobs now that one started
                    RemoveQueuedToolJobsFor(pawn, tool, JobDefOf.Equip, JobDefOf.TakeInventory);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogError($"[SurvivalTools.Assignment] Exception in TryStartQueuedToolJobFor: {ex}");
            }
            return false;
        }

        /// <summary>
        /// Public helper: returns true if the pawn is currently performing or has queued
        /// an acquisition job (Equip or TakeInventory). Used by gating to decide whether
        /// to allow a work job to proceed or block until acquisition is in motion.
        /// </summary>
        public static bool HasAcquisitionPendingOrQueued(Pawn pawn)
        {
            try
            {
                if (pawn?.jobs == null) return false;
                // Current job check
                var curDef = pawn.jobs.curJob?.def;
                if (curDef == JobDefOf.Equip || curDef == JobDefOf.TakeInventory)
                    return true;

                // Queue scan
                var jq = pawn.jobs.jobQueue;
                if (jq == null) return false;
                for (int i = 0; i < jq.Count; i++)
                {
                    var j = jq[i]?.job;
                    if (j == null || j.def == null) continue;
                    if (j.def == JobDefOf.Equip || j.def == JobDefOf.TakeInventory)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static bool HasQueuedAcquisitionFor(Pawn pawn, Thing tool)
        {
            try
            {
                var jq = pawn?.jobs?.jobQueue;
                if (jq == null || tool == null) return false;
                for (int i = 0; i < jq.Count; i++)
                {
                    var j = jq[i]?.job;
                    if (j == null || j.def == null) continue;
                    if (j.def != JobDefOf.Equip && j.def != JobDefOf.TakeInventory) continue;
                    Thing targetThing = null;
                    try { if (j.targetA.HasThing) targetThing = j.targetA.Thing; } catch { targetThing = null; }
                    if (targetThing != null && TargetsSameTool(pawn, targetThing, tool))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static void RemoveQueuedToolJobsFor(Pawn pawn, Thing tool, params JobDef[] jobDefs)
        {
            try
            {
                var jq = pawn?.jobs?.jobQueue;
                if (jq == null || tool == null) return;
                // First pass: remove entries with destroyed/null targets for these jobDefs (stale cleanup)
                for (int i = jq.Count - 1; i >= 0; i--)
                {
                    var q = jq[i];
                    var jj = q?.job;
                    if (jj?.def == null) continue;
                    bool matches = false; for (int d = 0; d < jobDefs.Length; d++) { if (jj.def == jobDefs[d]) { matches = true; break; } }
                    if (!matches) continue;
                    Thing t = null; try { if (jj.targetA.HasThing) t = jj.targetA.Thing; } catch { t = null; }
                    if (t == null || t.DestroyedOrNull())
                    {
                        var list = jq as System.Collections.IList; list?.RemoveAt(i);
                        LogDebug($"[SurvivalTools.Assignment] Removed stale queued {jj.def.defName} (null/destroyed target)", "AssignmentSearch.Cleanup.Stale");
                    }
                }
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
                        LogDebug($"Removed duplicate queued {j.def.defName} for {pawn?.LabelShort} targeting {tool?.LabelShort ?? "null"}", "AssignmentSearch.DedupeRemove");
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

                // EARLY MAP CHECK: Fail fast if tool is on different map
                if (tool?.Map != pawn?.Map)
                {
                    LogDebug($"Skipping {tool?.LabelShort} - different map (tool: {tool?.Map?.Index ?? -1}, pawn: {pawn?.Map?.Index ?? -1})", "AssignmentSearch.MapMismatch");
                    continue;
                }

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

                // EARLY MAP CHECK: Fail fast if tool is on different map
                if (tool?.Map != pawn?.Map)
                {
                    LogDebug($"Skipping {tool?.LabelShort} - different map (tool: {tool?.Map?.Index ?? -1}, pawn: {pawn?.Map?.Index ?? -1})", "AssignmentSearch.MapMismatch");
                    continue;
                }

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

            // CRITICAL: Cache tool reference immediately to prevent race conditions
            // The tool can become null/destroyed at any point during this method
            var tool = candidate.tool;
            if (tool == null)
            {
                LogDebug($"QueueAcquisitionJob: candidate.tool is null for {pawn?.LabelShort}", "AssignmentSearch.NullTool");
                return false;
            }

            LogDebug($"QueueAcquisitionJob: attempting to queue job for {pawn.LabelShort} to acquire {tool.LabelShort} from {candidate.location} (priority={priority})", "AssignmentSearch.QueueAttempt");

            // If an equivalent queued acquisition job exists and pawn is idle, start it instead of creating a new one
            if (TryStartQueuedToolJobFor(pawn, tool, JobDefOf.Equip, JobDefOf.TakeInventory))
            {
                acquisitionEnqueued = true;
                // Purge any other duplicates for the same target
                RemoveQueuedToolJobsFor(pawn, tool, JobDefOf.Equip, JobDefOf.TakeInventory);
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
            // Nightmare exception: we still need to purge everything before acquiring.
            var settings = SurvivalToolsMod.Settings;
            bool nightmare = settings?.extraHardcoreMode == true;
            if (!nightmare && (HasPendingDropJob(pawn) || HasPendingAcquisitionJob(pawn)))
            {
                LogDebug($"QueueAcquisitionJob: tool-management pending for {pawn.LabelShort} — deferring acquisition", "AssignmentSearch.DeferAcquireForPendingQueue");
                // handled this pass by deferring; no acquisition enqueued
                acquisitionEnqueued = false;
                return false;
            }

            // ENHANCED VALIDATION: Real-time tool state check before any job creation
            if (!ValidateToolStateForAcquisition(pawn, tool))
            {
                LogDebug($"Tool validation failed for {tool.LabelShort} and {pawn.LabelShort}", "AssignmentSearch.ToolValidationFailed");
                return false;
            }

            // NIGHTMARE MODE: Must purge ALL carried tools (except the target if already held) BEFORE any acquisition/equip job.
            if (nightmare)
            {
                try
                {
                    if (NightmarePurgeBeforeAcquire(pawn, tool))
                    {
                        LogDebug($"[Nightmare] Full purge queued for {pawn.LabelShort}; deferring acquisition of {tool.LabelShort}", "Nightmare.PurgeBeforeAcquire");
                        acquisitionEnqueued = false;
                        return true; // handled (we *did* something: queued drops)
                    }
                }
                catch (Exception nmEx)
                {
                    LogDebug($"Nightmare purge exception: {nmEx}", "Nightmare.Purge.Exception");
                }
            }

            // Check carry limits BEFORE creating jobs and try to make room
            if (!CanCarryAdditionalTool(pawn, tool))
            {
                LogDebug($"Carry limit reached for {pawn.LabelShort}, need to drop worst tool first", "AssignmentSearch.CarryLimit");

                // Try to drop worst tool first - this must complete before acquisition
                Thing worstTool = null;
                try
                {
                    // If carry-limit is effectively 1, protect the best tool for the requested stat to avoid ping-pong
                    // Use cached settings
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

                LogDebug($"Found worst tool to drop: {worstTool?.LabelShort ?? "null"} for {pawn.LabelShort}", "AssignmentSearch.FoundWorstTool");

                // Validate worst tool before trying to drop it
                if (!ValidateToolStateForDrop(pawn, worstTool))
                {
                    LogDebug($"Worst tool validation failed for {worstTool?.LabelShort ?? "null"} and {pawn.LabelShort}", "AssignmentSearch.WorstToolValidationFailed");
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
                    LogDebug($"Failed to queue drop job for {worstTool?.LabelShort ?? "null"} by {pawn.LabelShort}", "AssignmentSearch.DropJobFailed");
                    return false;
                }

                LogDebug($"Successfully queued drop job for {worstTool?.LabelShort ?? "null"} by {pawn.LabelShort}", "AssignmentSearch.DropJobSuccess");
                // Defer acquisition until after drop completes to avoid queue ordering issues
                acquisitionEnqueued = false;
                return true;
            }

            // SAFETY: Final validation that tool is still valid before creating job
            if (tool == null || tool.Destroyed)
            {
                LogDebug($"Tool became null or destroyed before job creation for {pawn.LabelShort}", "AssignmentSearch.ToolInvalidBeforeJob");
                return false;
            }

            Job job = null;

            LogDebug($"[SurvivalTools.Assignment] Creating acquisition job for {tool.LabelShort} at {candidate.location}", $"Assign.CreateJob.Acq|{pawn.ThingID}|{tool.thingIDNumber}");

            switch (candidate.location)
            {
                case ToolLocation.Inventory:
                    // Already in inventory, just equip
                    job = JobMaker.MakeJob(JobDefOf.Equip, tool);
                    LogDebug($"Created Equip job for {pawn.LabelShort} (tool in inventory)", "AssignmentSearch.EquipInventory");
                    break;

                case ToolLocation.Equipment:
                case ToolLocation.SameCell:
                    // Pick up and equip
                    job = JobMaker.MakeJob(JobDefOf.Equip, tool);
                    LogDebug($"Created Equip job for {pawn.LabelShort} (tool at {candidate.location})", "AssignmentSearch.EquipLocation");
                    break;

                default:
                    // Haul to inventory first
                    job = JobMaker.MakeJob(JobDefOf.TakeInventory, tool);
                    job.count = 1;
                    LogDebug($"Created TakeInventory job for {pawn.LabelShort} (tool at {candidate.location})", "AssignmentSearch.TakeInventory");
                    break;
            }

            if (job == null)
            {
                LogDebug($"Failed to create job for {pawn.LabelShort}", "AssignmentSearch.JobCreationFailed");
                return false;
            }

            // CRITICAL: Verify tool is still valid immediately after job creation
            // Tool can despawn between JobMaker.MakeJob and here
            if (tool == null || tool.Destroyed || tool.Map != pawn?.Map)
            {
                LogDebug($"Tool became invalid immediately after job creation for {pawn.LabelShort}", "AssignmentSearch.ToolInvalidPostJobCreate");
                return false;
            }

            // ENHANCED VALIDATION: Use JobUtils to validate job before queuing
            // Wrap in try-catch as this internally accesses job.targetA.Thing
            bool jobValid = false;
            try
            {
                jobValid = JobUtils.IsJobStillValid(job, pawn);
            }
            catch (NullReferenceException)
            {
                LogDebug($"NullRef during IsJobStillValid - tool became invalid for {pawn.LabelShort}", "AssignmentSearch.JobValidationNullRef");
                return false;
            }

            if (!jobValid)
            {
                LogDebug($"Acquisition job {job?.def?.defName ?? "unknown"} is invalid for {pawn?.LabelShort ?? "unknown"}, not queuing", "AssignmentSearch.AcquisitionJobInvalid");
                return false;
            }

            try
            {
                // SAFETY: Clone job for queue to prevent reference issues
                var clonedJob = JobUtils.CloneJobForQueue(job);
                if (clonedJob == null)
                {
                    LogDebug($"CloneJobForQueue returned null for {pawn.LabelShort}", "AssignmentSearch.CloneJobNull");
                    return false;
                }

                // Make this behave like a player-forced ordered job so it starts immediately when idle
                clonedJob.playerForced = true;

                // ENHANCED VALIDATION: Double-check tool state right before reservation
                if (!ValidateToolStateForAcquisition(pawn, tool))
                {
                    LogDebug($"Tool validation failed right before reservation for {tool?.LabelShort ?? "null"} and {pawn?.LabelShort ?? "unknown"}", "AssignmentSearch.PreReservationValidationFailed");
                    return false;
                }

                // CRITICAL: Final null check immediately before Reserve to catch race conditions
                if (tool == null || tool.Destroyed)
                {
                    LogDebug($"Tool became null/destroyed immediately before Reserve for {pawn.LabelShort}", "AssignmentSearch.ToolNullBeforeReserve");
                    return false;
                }

                // Reserve using the cloned job instance that will actually run
                // Wrap in try-catch as Reserve might access tool properties internally
                bool reserveSuccess = false;
                try
                {
                    reserveSuccess = pawn.Reserve(tool, clonedJob, 1, -1, null, true);
                }
                catch (NullReferenceException)
                {
                    LogDebug($"NullReferenceException during Reserve for {pawn.LabelShort} - tool likely became invalid", "AssignmentSearch.ReserveNullRef");
                    return false;
                }

                if (!reserveSuccess)
                {
                    LogDebug($"Failed to reserve {tool?.LabelShort ?? "null"} for {pawn?.LabelShort ?? "unknown"}", "AssignmentSearch.ReserveFailed");
                    return false;
                }

                // If pawn is idle, start immediately to avoid queue starvation
                if (pawn.jobs?.curJob == null && pawn.jobs != null)
                {
                    bool taken = pawn.jobs.TryTakeOrderedJob(clonedJob);
                    if (taken)
                    {
                        LogDebug($"[SurvivalTools.Assignment] Started {clonedJob?.def?.defName ?? "unknown"} immediately for {pawn?.LabelShort ?? "unknown"} targeting {tool?.LabelShort ?? "null"}", $"Assign.StartImmediate|{pawn?.ThingID ?? "unknown"}|{tool?.thingIDNumber ?? -1}|{clonedJob?.def?.defName ?? "unknown"}");
                        // Remove any duplicate queued acquisition jobs for this target
                        RemoveQueuedToolJobsFor(pawn, tool, JobDefOf.Equip, JobDefOf.TakeInventory);
                        // Protect the just-acquired tool from being dropped immediately
                        try
                        {
                            // Cache tick access
                            int nowTick = Find.TickManager?.TicksGame ?? 0;
                            if (tool != null)
                            {
                                _recentAcquisitions[pawn.thingIDNumber] = new RecentAcqData { untilTick = nowTick + FocusTicksWindow, thingID = tool.thingIDNumber };
                                LogDebug($"Set recent-acquisition protect for {pawn?.LabelShort ?? "unknown"} on {tool?.LabelShort ?? "null"} for {FocusTicksWindow} ticks", "AssignmentSearch.SetRecentAcq");
                            }
                        }
                        catch { /* best-effort */ }
                        acquisitionEnqueued = true;
                        return true;
                    }
                    // If failed to start now, fall back to enqueue with priority
                }

                // CRITICAL: Final validation before enqueue - tool must still be valid
                // Use null-safe checks to prevent NullRef during validation itself
                var toolMap = tool?.Map;
                if (tool == null || tool.Destroyed || (toolMap != null && toolMap != pawn.Map))
                {
                    LogDebug($"Tool became invalid before enqueue for {pawn.LabelShort}", "AssignmentSearch.ToolInvalidBeforeEnqueue");
                    return false;
                }

                // Enqueue according to requested priority to avoid preempting AI-selected jobs
                // Wrap in try-catch as enqueue operations may internally access tool properties
                try
                {
                    if (priority == QueuePriority.Front)
                    {
                        // Avoid enqueueing duplicates
                        if (!TryStartQueuedToolJobFor(pawn, tool, JobDefOf.Equip, JobDefOf.TakeInventory) && !HasQueuedAcquisitionFor(pawn, tool))
                            pawn.jobs?.jobQueue?.EnqueueFirst(clonedJob, JobTag.Misc);
                    }
                    else
                    {
                        // For Append and idle=false case we should never reach here due to early return.
                        // Still avoid enqueueing duplicates in case of future changes
                        if (!HasQueuedAcquisitionFor(pawn, tool))
                            pawn.jobs?.jobQueue?.EnqueueLast(clonedJob, JobTag.Misc);
                    }
                }
                catch (NullReferenceException)
                {
                    LogDebug($"NullReferenceException during enqueue for {pawn.LabelShort} - tool likely became invalid", "AssignmentSearch.EnqueueNullRef");
                    return false;
                }

                LogDebug($"[SurvivalTools.Assignment] Successfully enqueued {clonedJob?.def?.defName ?? "unknown"} job for {pawn?.LabelShort ?? "unknown"} targeting {tool?.LabelShort ?? "null"}", $"Assign.Enqueued|{pawn?.ThingID ?? "unknown"}|{tool?.thingIDNumber ?? -1}|{clonedJob?.def?.defName ?? "unknown"}");
                // Protect the just-enqueued acquisition target as "recently acquired" to avoid immediate drop
                try
                {
                    // Cache tick access
                    int nowTick2 = Find.TickManager?.TicksGame ?? 0;
                    if (tool != null)
                    {
                        _recentAcquisitions[pawn.thingIDNumber] = new RecentAcqData { untilTick = nowTick2 + FocusTicksWindow, thingID = tool.thingIDNumber };
                        LogDebug($"Set recent-acquisition protect (enqueued) for {pawn?.LabelShort ?? "unknown"} on {tool?.LabelShort ?? "null"} for {FocusTicksWindow} ticks", "AssignmentSearch.SetRecentAcq.Enqueued");
                    }
                }
                catch { /* best-effort */ }
                acquisitionEnqueued = true;
                return true;
            }
            catch (Exception ex)
            {
                LogError($"[SurvivalTools.Assignment] Exception enqueueing job for {pawn.LabelShort}: {ex}");

                // Clean up reservation on failure - only if we actually reserved it
                // Check if the tool is reserved by this pawn before attempting release
                try
                {
                    // Cache tool reference to prevent race conditions during cleanup
                    var toolForCleanup = tool;
                    if (toolForCleanup != null && !toolForCleanup.Destroyed && pawn?.Map?.reservationManager != null)
                    {
                        var resMan = pawn.Map.reservationManager;
                        if (resMan.ReservedBy(toolForCleanup, pawn))
                        {
                            // Release with null job (reservation cleanup doesn't require the job reference)
                            resMan.Release(toolForCleanup, pawn, null);
                        }
                    }
                }
                catch (Exception releaseEx)
                {
                    LogError($"[SurvivalTools.Assignment] Exception releasing reservation: {releaseEx}");
                }

                // FALLBACK: Try direct tool swapping if job queuing fails
                LogWarning($"[SurvivalTools.Assignment] Job queuing failed, attempting direct tool swap fallback");
                // Cache tool reference to prevent race conditions during fallback attempt
                var toolForFallback = tool;
                if (toolForFallback != null && !toolForFallback.Destroyed)
                {
                    var swap = TryDirectToolSwap(pawn, candidate);
                    acquisitionEnqueued = swap;
                    return swap;
                }
                else
                {
                    LogWarning($"[SurvivalTools.Assignment] Cannot attempt direct swap - tool is null or destroyed");
                    acquisitionEnqueued = false;
                    return false;
                }
            }
        }

        /// <summary>
        /// Nightmare rule: drop ALL currently carried real tools BEFORE attempting to acquire/equip a new tool.
        /// Returns true if at least one drop job was queued (so acquisition should be deferred this pass).
        /// </summary>
        // External callers (e.g., passive pickup rescues) can invoke this to enforce Nightmare rule before any acquisition.
        internal static bool NightmarePurgeAllTools(Pawn pawn, Thing excludeTool) => NightmarePurgeBeforeAcquire(pawn, excludeTool);

        private static bool NightmarePurgeBeforeAcquire(Pawn pawn, Thing targetTool)
        {
            if (pawn == null) return false;
            // Gather all real carried tools (inventory + equipment). We do not count tool-stuff stacks here; focus on tangible tools.
            var toDrop = new List<Thing>();
            var inv = pawn.inventory?.innerContainer;
            if (inv != null)
            {
                for (int i = 0; i < inv.Count; i++)
                {
                    var t = inv[i]; if (t != null && IsRealTool(t) && t != targetTool) toDrop.Add(t);
                }
            }
            var eq = pawn.equipment?.AllEquipmentListForReading;
            if (eq != null)
            {
                for (int i = 0; i < eq.Count; i++)
                {
                    var t = eq[i]; if (t != null && IsRealTool(t) && t != targetTool) toDrop.Add(t);
                }
            }
            if (toDrop.Count == 0)
                return false;

            bool any = false;
            // Queue drop jobs for every carried tool so pawn ends up with zero before acquisition.
            // Order: drop lowest-score tools first, though in Nightmare we expect few.
            toDrop.Sort((a, b) => GetOverallToolScore(pawn, a).CompareTo(GetOverallToolScore(pawn, b)));
            for (int i = 0; i < toDrop.Count; i++)
            {
                var tool = toDrop[i];
                if (!ValidateToolStateForDrop(pawn, tool)) continue;
                if (!QueueDropJob(pawn, tool)) continue;
                any = true;
            }
            if (any)
            {
                LogDebug($"[Nightmare] Queued {toDrop.Count} drop jobs before acquisition attempt (target={(targetTool != null ? targetTool.LabelShort : "null")}) for {pawn.LabelShort}", "Nightmare.PurgeQueued");
            }
            return any;
        }

        // Compute the effective carry limit combining difficulty mode cap (1/2/3) and stat-based capacity.
        // We take the MIN of (difficulty cap, stat cap) unless toolLimit setting is off (then unlimited).
        public static int GetEffectiveCarryLimit(Pawn pawn, SurvivalToolsSettings settings)
        {
            if (pawn == null || settings == null) return 9999; // fail-open
            int diffCap = GetCarryLimit(settings); // 1/2/3 based on hardcore modes
            int statCap = 9999;
            if (settings.toolLimit)
            {
                try
                {
                    // Floor to int; stat typically small (e.g. 1-3) but future scaling safe
                    statCap = (int)Math.Floor(pawn.GetStatValue(ST_StatDefOf.SurvivalToolCarryCapacity) + 0.001f);
                }
                catch { statCap = diffCap; }
            }
            int cap = Math.Min(diffCap, statCap);
            if (HasToolbelt(pawn)) cap = Math.Max(cap, 3); // toolbelt exception
            if (cap < 0) cap = 0; if (cap > 9999) cap = 9999;
            return cap;
        }

        private static bool CanCarryAdditionalTool(Pawn pawn, Thing candidateTool = null)
        {
            var settings = SurvivalToolsMod.Settings;
            if (settings == null)
                return true;

            int carryLimit = GetEffectiveCarryLimit(pawn, settings);
            int currentTools = CountCarriedTools(pawn);

            // If the candidate tool is already carried, we're not adding a NEW tool
            if (candidateTool != null && IsToolAlreadyCarried(pawn, candidateTool))
            {
                LogDebug($"CanCarryAdditionalTool for {pawn.LabelShort}: candidate {candidateTool?.LabelShort ?? "null"} is already carried, no drop needed", "AssignmentSearch.CarryCheck.AlreadyCarried");
                return true;
            }

            bool canCarry = currentTools < carryLimit;

            LogDebug($"CanCarryAdditionalTool for {pawn.LabelShort}: current={currentTools}, effectiveLimit={carryLimit} (toolLimit={settings.toolLimit} diffCap={GetCarryLimit(settings)} statCap={(settings.toolLimit ? pawn.GetStatValue(ST_StatDefOf.SurvivalToolCarryCapacity).ToString("F2") : "∞")}), canCarry={canCarry}", "AssignmentSearch.CarryCheck");

            return canCarry;
        }

        private static bool IsToolAlreadyCarried(Pawn pawn, Thing tool)
        {
            if (pawn == null || tool == null) return false;

            // Check inventory
            var inventory = pawn.inventory?.innerContainer;
            if (inventory != null && inventory.Contains(tool))
                return true;

            // Check equipment
            var equipment = pawn.equipment?.AllEquipmentListForReading;
            if (equipment != null)
            {
                for (int i = 0; i < equipment.Count; i++)
                {
                    if (equipment[i] == tool)
                        return true;
                }
            }

            return false;
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

        internal static int CountCarriedTools(Pawn pawn)
        {
            int count = 0;

            // Count inventory tools
            var inventory = pawn.inventory?.innerContainer;
            if (inventory != null)
            {
                for (int i = 0; i < inventory.Count; i++)
                {
                    var thing = inventory[i];
                    if (thing != null && IsRealTool(thing))
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
                    if (thing != null && IsRealTool(thing))
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
                // Use our unified drop driver so we can choose a preferred drop location and enqueue hauling if needed
                dropJob = JobMaker.MakeJob(ST_JobDefOf.DropSurvivalTool, toolToDrop);
                dropJob.count = 1;
                LogDebug($"Created unified DropSurvivalTool job (equipped) for {pawn.LabelShort}", "AssignmentSearch.DropSurvivalTool.Unified");
            }
            else if (isInInventory)
            {
                // Inventory-held tools also use our unified drop driver
                dropJob = JobMaker.MakeJob(ST_JobDefOf.DropSurvivalTool, toolToDrop);
                dropJob.count = 1;
                LogDebug($"Created DropSurvivalTool job (inventory) for {pawn.LabelShort}", "AssignmentSearch.DropSurvivalTool");
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
                int now = Find.TickManager?.TicksGame ?? 0;
                string droppedKey = $"{pawn.thingIDNumber}_{toolToDrop.thingIDNumber}";

                // SAFETY: Clone job for queue to prevent reference issues
                var clonedJob = JobUtils.CloneJobForQueue(dropJob);
                clonedJob.playerForced = true;

                // If pawn is idle, start immediately
                if (pawn.jobs?.curJob == null)
                {
                    bool taken = pawn.jobs.TryTakeOrderedJob(clonedJob);
                    if (taken)
                    {
                        // Track dropped tool to prevent immediate re-acquisition
                        _recentlyDroppedTools[droppedKey] = now + DroppedToolCooldownTicks;

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

                // Track dropped tool to prevent immediate re-acquisition
                _recentlyDroppedTools[droppedKey] = now + DroppedToolCooldownTicks;

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
                    if (thing != null && IsRealTool(thing))
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
                    if (thing != null && IsRealTool(thing))
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
                LogDebug($"FindWorstCarriedTool for {pawn.LabelShort}: {worstTool?.LabelShort ?? "null"} (score: {worstScore:F3})", "AssignmentSearch.WorstTool");
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
                    if (thing != null && IsRealTool(thing))
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
                    if (thing != null && IsRealTool(thing))
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
                LogDebug($"FindWorstCarriedToolRespectingFocus for {pawn.LabelShort}: {worstTool?.LabelShort ?? "null"} (score: {worstScore:F3})", "AssignmentSearch.WorstTool.RespectingFocus");
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

            // For virtual tool-stuff (e.g., cloth and other textiles), only accept if it actually
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

        // Real tool excludes raw tool-stuff (materials) so carry limit & drop logic only count tangible tools
        private static bool IsRealTool(Thing thing)
        {
            if (thing == null) return false;
            if (thing.def?.IsToolStuff() == true) return false;
            return IsSurvivalTool(thing);
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

            int now = Find.TickManager?.TicksGame ?? 0;

            // Cooldown check for failed candidates
            if (_candidateCooldownTicks.TryGetValue(tool.thingIDNumber, out int until) && now < until)
                return true;

            // Check if this pawn recently dropped this tool
            if (pawn != null)
            {
                string droppedKey = $"{pawn.thingIDNumber}_{tool.thingIDNumber}";
                if (_recentlyDroppedTools.TryGetValue(droppedKey, out int dropUntil) && now < dropUntil)
                {
                    LogDebug($"Skipping {tool.LabelShort} - recently dropped by {pawn.LabelShort}", "AssignmentSearch.DroppedCooldown");
                    return true;
                }

                // NIGHTMARE MODE COORDINATION: Check if another pawn recently acquired this tool
                // This prevents pawns from fighting over the same tool
                if (SurvivalToolsMod.Settings?.CurrentMode == DifficultyMode.Nightmare)
                {
                    foreach (var kvp in _recentAcquisitions)
                    {
                        // Skip if it's this pawn's own acquisition
                        if (kvp.Key == pawn.thingIDNumber)
                            continue;

                        // Check if another pawn recently acquired this exact tool
                        if (kvp.Value.thingID == tool.thingIDNumber && now < kvp.Value.untilTick)
                        {
                            LogDebug($"Skipping {tool.LabelShort} - recently acquired by another pawn (Nightmare coordination)", "AssignmentSearch.NightmareConflict");
                            return true;
                        }
                    }
                }
            }

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

        /// <summary>
        /// Check if two tools are in the same family (e.g., both hammers, both axes)
        /// Used to prevent frequent micro-optimization swaps within the same tool type
        /// </summary>
        private static bool IsSameToolFamily(Thing tool1, Thing tool2)
        {
            if (tool1 == null || tool2 == null) return false;
            if (tool1 == tool2) return true; // Same exact tool

            // Use existing ToolUtility to check if both have the same ToolKind
            var kind1 = ToolUtility.ToolKindOf(tool1);
            var kind2 = ToolUtility.ToolKindOf(tool2);

            if (kind1 != STToolKind.None && kind2 != STToolKind.None && kind1 == kind2)
                return true;

            return false;
        }

        internal static float GetOverallToolScore(Pawn pawn, Thing tool)
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


        // Cache for GetRelevantToolDefs to avoid repeated DefDatabase queries
        private static readonly Dictionary<StatDef, List<ThingDef>> _relevantToolDefsCache = new Dictionary<StatDef, List<ThingDef>>();
        private static bool _relevantToolDefsCacheBuilt = false;

        /// <summary>
        /// Public helper to pre-warm the GetRelevantToolDefs cache during game load.
        /// Called by StaticConstructorClass to eliminate first-click lag.
        /// </summary>
        internal static void WarmRelevantToolDefsCache()
        {
            if (!_relevantToolDefsCacheBuilt)
            {
                BuildRelevantToolDefsCache();
                _relevantToolDefsCacheBuilt = true;
            }
        }

        private static List<ThingDef> GetRelevantToolDefs(StatDef workStat)
        {
            // Build cache on first use (fallback if pre-warming didn't run)
            if (!_relevantToolDefsCacheBuilt)
            {
                BuildRelevantToolDefsCache();
                _relevantToolDefsCacheBuilt = true;
            }

            // Return cached list or empty list
            if (_relevantToolDefsCache.TryGetValue(workStat, out var cached))
                return cached;

            return new List<ThingDef>(); // Empty list for unknown stats
        }

        /// <summary>
        /// Build cache of tool defs per work stat once at startup.
        /// This avoids repeated DefDatabase queries during hot path.
        /// </summary>
        private static void BuildRelevantToolDefsCache()
        {
            var allToolDefs = new List<ThingDef>();

            // Collect all tool defs once
            foreach (var toolDef in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (toolDef?.IsSurvivalTool() == true)
                {
                    allToolDefs.Add(toolDef);
                }
            }

            // Build cache for each registered work stat
            var workStats = new[]
            {
                ST_StatDefOf.DiggingSpeed,
                ST_StatDefOf.MiningYieldDigging,
                ST_StatDefOf.PlantHarvestingSpeed,
                ST_StatDefOf.SowingSpeed,
                ST_StatDefOf.TreeFellingSpeed,
                ST_StatDefOf.MaintenanceSpeed,
                ST_StatDefOf.DeconstructionSpeed,
                ST_StatDefOf.ResearchSpeed,
                ST_StatDefOf.CleaningSpeed,
                ST_StatDefOf.MedicalOperationSpeed,
                ST_StatDefOf.MedicalSurgerySuccessChance,
                ST_StatDefOf.ButcheryFleshSpeed,
                ST_StatDefOf.ButcheryFleshEfficiency,
                StatDefOf.ConstructionSpeed,
                ST_StatDefOf.WorkSpeedGlobal
            };

            foreach (var workStat in workStats)
            {
                if (workStat == null) continue;

                var relevantDefs = new List<ThingDef>();
                float baseline = SurvivalToolUtility.GetNoToolBaseline(workStat);

                foreach (var toolDef in allToolDefs)
                {
                    // Create dummy tool to check if it provides this stat
                    var dummyTool = new SurvivalTool();
                    dummyTool.def = toolDef;

                    var factor = SurvivalToolUtility.GetToolProvidedFactor(dummyTool, workStat);
                    if (factor > baseline + GatingEpsilon)
                    {
                        relevantDefs.Add(toolDef);
                    }
                }

                _relevantToolDefsCache[workStat] = relevantDefs;
            }
        }

        /// <summary>
        /// Alternative approach: Direct tool swapping without job queuing.
        /// Used as fallback when job-based approach fails.
        /// </summary>
        private static bool TryDirectToolSwap(Pawn pawn, ToolCandidate candidate)
        {
            Log.Warning($"[SurvivalTools.Assignment] Attempting DIRECT tool swap for {pawn.LabelShort} with {candidate.tool?.LabelShort ?? "null"}");

            try
            {
                // Nightmare rule: must purge all existing tools first (except target if already possessed)
                try
                {
                    if (SurvivalToolsMod.Settings?.extraHardcoreMode == true)
                    {
                        if (NightmarePurgeBeforeAcquire(pawn, candidate.tool))
                        {
                            Log.Warning($"[SurvivalTools.Assignment][Nightmare] Direct swap deferred pending purge for {pawn.LabelShort}");
                            return true; // we queued drops; treat as handled this pass
                        }
                    }
                }
                catch { }
                // Validate tool state once more
                if (!ValidateToolStateForAcquisition(pawn, candidate.tool))
                {
                    Log.Warning($"[SurvivalTools.Assignment] Direct swap validation failed for {candidate.tool?.LabelShort ?? "null"}");
                    return false;
                }

                // Handle carry limits by dropping worst tool first
                if (!CanCarryAdditionalTool(pawn))
                {
                    Thing worstTool = FindWorstCarriedTool(pawn);
                    if (worstTool != null && ValidateToolStateForDrop(pawn, worstTool))
                    {
                        Log.Warning($"[SurvivalTools.Assignment] Direct drop of worst tool: {worstTool?.LabelShort ?? "null"}");
                        if (!TryDirectDropTool(pawn, worstTool))
                        {
                            Log.Warning($"[SurvivalTools.Assignment] Direct drop failed for {worstTool?.LabelShort ?? "null"}");
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
                        Log.Warning($"[SurvivalTools.Assignment] Tool {candidate.tool?.LabelShort ?? "null"} already equipped");
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
                    Log.Message($"[SurvivalTools.Assignment] DIRECT tool swap succeeded for {pawn.LabelShort} with {candidate.tool?.LabelShort ?? "null"}");

                    // Update hysteresis
                    int currentTick = Find.TickManager?.TicksGame ?? 0;
                    _hysteresisData[pawn.thingIDNumber] = new HysteresisData
                    {
                        lastUpgradeTick = currentTick,
                        lastEquippedDefName = candidate.tool?.def?.defName ?? string.Empty
                    };

                    // Notify score cache
                    ScoreCache.NotifyInventoryChanged(pawn);
                    ScoreCache.NotifyToolChanged(candidate.tool);

                    return true;
                }
                else
                {
                    Log.Warning($"[SurvivalTools.Assignment] DIRECT tool swap failed for {pawn.LabelShort} with {candidate.tool?.LabelShort ?? "null"}");
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
                    ThingWithComps droppedEq = null;
                    if (pawn.equipment?.TryDropEquipment(equipmentTool, out droppedEq, pawn.Position) == true)
                    {
                        Log.Message($"[SurvivalTools.Assignment] Directly dropped equipment: {tool?.LabelShort ?? "null"}");
                        try { droppedEq?.SetForbidden(false, false); } catch { }
                        // Enqueue haul to storage if available
                        try
                        {
                            IntVec3 storeCell;
                            var map = pawn.Map; var faction = pawn.Faction;
                            if (map != null && droppedEq != null && (StoreUtility.TryFindBestBetterStoreCellFor(droppedEq, pawn, map, StoreUtility.CurrentStoragePriorityOf(droppedEq), faction, out storeCell)
                                || StoreUtility.TryFindBestBetterStoreCellFor(droppedEq, pawn, map, StoragePriority.Unstored, faction, out storeCell)))
                            {
                                var haulJob = JobMaker.MakeJob(JobDefOf.HaulToCell, droppedEq, storeCell);
                                haulJob.count = 1;
                                pawn.jobs?.jobQueue?.EnqueueFirst(haulJob);
                            }
                        }
                        catch { }
                        return true;
                    }
                }
                else if (isInInventory)
                {
                    // Drop from inventory
                    Thing droppedInv = null;
                    var dropped = pawn.inventory?.innerContainer?.TryDrop(tool, pawn.Position, pawn.Map, ThingPlaceMode.Near, out droppedInv);
                    if (dropped == true)
                    {
                        Log.Message($"[SurvivalTools.Assignment] Directly dropped from inventory: {tool?.LabelShort ?? "null"}");
                        try { droppedInv?.SetForbidden(false, false); } catch { }
                        // Enqueue haul to storage if available
                        try
                        {
                            IntVec3 storeCell;
                            var map = pawn.Map; var faction = pawn.Faction;
                            if (map != null && droppedInv != null && (StoreUtility.TryFindBestBetterStoreCellFor(droppedInv, pawn, map, StoreUtility.CurrentStoragePriorityOf(droppedInv), faction, out storeCell)
                                || StoreUtility.TryFindBestBetterStoreCellFor(droppedInv, pawn, map, StoragePriority.Unstored, faction, out storeCell)))
                            {
                                var haulJob = JobMaker.MakeJob(JobDefOf.HaulToCell, droppedInv, storeCell);
                                haulJob.count = 1;
                                pawn.jobs?.jobQueue?.EnqueueFirst(haulJob);
                            }
                        }
                        catch { }
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
                        Log.Message($"[SurvivalTools.Assignment] Directly equipped from inventory: {tool?.LabelShort ?? "null"}");
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
                        Log.Message($"[SurvivalTools.Assignment] Directly picked up to inventory: {tool?.LabelShort ?? "null"}");
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
                Log.Warning($"[SurvivalTools.Assignment] Direct haul not supported for distant tool {tool?.LabelShort ?? "null"}");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools.Assignment] Exception in direct haul: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Phase 12: Clear transient state to prevent Job reference warnings on save.
        /// Called from GameComponent during save operation.
        /// Static dictionaries can hold Job references which cause "Object with load ID Job_XXXXX 
        /// is referenced but is not deep-saved" warnings during save.
        /// </summary>
        public static void ClearTransientState()
        {
            try
            {
                _hysteresisData?.Clear();
                _processingPawns?.Clear();
                _managementCooldownUntil?.Clear();
                _candidateCooldownTicks?.Clear();
                _statFocus?.Clear();
                _recentAcquisitions?.Clear();

                LogDebug("AssignmentSearch transient state cleared for save", "AssignmentSearch.ClearState");
            }
            catch (Exception ex)
            {
                Log.Warning($"[SurvivalTools.AssignmentSearch] Error clearing transient state: {ex}");
            }
        }

        /// <summary>
        /// Release all survival tool reservations before saving to prevent "loaded reservation with null job" errors.
        /// When a game is saved, Job instances are not persisted with loadIDs, so reservations that reference
        /// jobs will fail to restore on load. We release all tool reservations before save to avoid this.
        /// </summary>
        public static void ReleaseAllToolReservations()
        {
            try
            {
                if (Find.Maps == null) return;

                int releasedCount = 0;
                foreach (var map in Find.Maps)
                {
                    if (map?.reservationManager == null) continue;

                    // Get all survival tools on the map
                    var tools = map.listerThings?.ThingsInGroup(ThingRequestGroup.HaulableEver)
                        ?.OfType<SurvivalTool>()
                        ?.ToList();

                    if (tools == null || tools.Count == 0) continue;

                    // Release reservations for all tools
                    foreach (var tool in tools)
                    {
                        if (tool == null || tool.Destroyed) continue;

                        try
                        {
                            // Check if tool is reserved
                            var reserver = map.reservationManager.FirstRespectedReserver(tool, null);
                            if (reserver != null)
                            {
                                // Release the reservation (job parameter can be null for cleanup)
                                map.reservationManager.Release(tool, reserver, null);
                                releasedCount++;
                            }
                        }
                        catch
                        {
                            // Skip individual failures
                        }
                    }
                }

                if (releasedCount > 0)
                {
                    LogDebug($"Released {releasedCount} tool reservations before save to prevent null job errors", "AssignmentSearch.ReleaseReservations");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[SurvivalTools.AssignmentSearch] Error releasing tool reservations: {ex}");
            }
        }
    }
}
