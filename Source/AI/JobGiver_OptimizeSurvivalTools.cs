// RimWorld 1.6 / C# 7.3
// Source/AI/JobGiver_OptimizeSurvivalTools.cs
//
// SurvivalTools — Optimizer
// - Drops unneeded/duplicate tools
// - Picks up better tools (incl. tool-stuff via VirtualSurvivalTool)
// - Reduced frequency when AutoTool is enabled
// - Safe map/area checks to avoid OOB errors

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;
using SurvivalTools.Helpers;

namespace SurvivalTools
{
    public class JobGiver_OptimizeSurvivalTools : ThinkNode_JobGiver
    {
        #region Tuning

        // Base cadence
        private const int OPTIMIZE_TICK_MIN = 3600;    // ~3h
        private const int OPTIMIZE_TICK_MAX = 14400;   // ~6h

        // Reduced cadence when AutoTool is doing the heavy lifting
        private const int OPTIMIZE_TICK_MIN_AUTOTOOL = 60000;  // ~24h
        private const int OPTIMIZE_TICK_MAX_AUTOTOOL = 72000;  // ~30h

        #endregion

        #region Core

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (SurvivalTools.Settings == null || !SurvivalTools.Settings.toolOptimization) return null;

            var assignmentTracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
            if (!pawn.CanUseSurvivalTools() || assignmentTracker == null) return null;

            // Skip if downed/asleep/in bed
            if (pawn.Downed || !pawn.Awake() || pawn.InBed())
            {
                SetNextOptimizeTick(pawn);
                return null;
            }

            var map = pawn.MapHeld;
            if (map == null)
            {
                SetNextOptimizeTick(pawn);
                return null;
            }

            var curAssignment = assignmentTracker.CurrentSurvivalToolAssignment;
            var heldTools = pawn.GetHeldSurvivalTools().ToList();

            // 1) Drop tools that are disallowed by assignment or no longer needed
            foreach (var tool in heldTools)
            {
                Thing backingForTool = tool is SurvivalTool st ? SurvivalToolUtility.BackingThing(st, pawn) ?? st : tool;

                bool disallowedByFilter = curAssignment?.filter != null && !curAssignment.filter.Allows(backingForTool);
                bool notNeeded = tool is SurvivalTool surv && !pawn.NeedsSurvivalTool(surv);

                if ((disallowedByFilter || notNeeded) &&
                    AllowedToAutomaticallyDropSafe(assignmentTracker, backingForTool))
                {
                    LogDebug($"[SurvivalTools.Optimizer] {pawn.LabelShort} is dropping unneeded tool: {tool.LabelShort}", $"Optimizer_DropUnneeded_{pawn.ThingID}");

                    SetNextOptimizeTick(pawn, 300, 600); // quick cool-down after drop
                    return pawn.DequipAndTryStoreSurvivalTool(tool);
                }
            }

            // 2. Drop duplicate tools (includes tool-stuff via virtual wrappers)
            var duplicateThingToDrop = FindDuplicateToolToDropIncludingVirtuals(pawn, heldTools);
            if (duplicateThingToDrop != null)
            {
                if (duplicateThingToDrop is SurvivalTool stDup)
                {
                    LogDebug($"[SurvivalTools.Optimizer] {pawn.LabelShort} is dropping duplicate tool: {stDup.LabelShort}", $"Optimizer_DropDupe_{pawn.ThingID}");
                    SetNextOptimizeTick(pawn, 300, 600);
                    return pawn.DequipAndTryStoreSurvivalTool(stDup);
                }
                else
                {
                    LogDebug($"[SurvivalTools.Optimizer] {pawn.LabelShort} is dropping duplicate tool-stuff: {duplicateThingToDrop.LabelShort}", $"Optimizer_DropDupeStuff_{pawn.ThingID}");
                    var inv = pawn.inventory;
                    if (inv?.innerContainer != null && inv.innerContainer.Contains(duplicateThingToDrop))
                    {
                        Thing outThing;

                        // (optional — see #2) drop exactly one from stacks
                        var toDrop = duplicateThingToDrop;
                        if (toDrop.stackCount > 1)
                            toDrop = toDrop.SplitOff(1);

                        inv.innerContainer.TryDrop(
                            toDrop,
                            pawn.Position,
                            pawn.Map,
                            ThingPlaceMode.Near,
                            out outThing
                        );
                    }

                    SetNextOptimizeTick(pawn, 300, 600);
                    return null; // drop completed immediately, no job needed
                }
            }

            // 3) Build relevant stat set
            var workRelevantStats = pawn.AssignedToolRelevantWorkGiversStatDefs().Distinct().ToList();
            if (workRelevantStats.NullOrEmpty())
            {
                SetNextOptimizeTick(pawn);
                return null;
            }

            // 3b) Respect cool-down unless pawn lacks any relevant tool
            bool hasAnyRelevantTool = heldTools.Any(t => t is SurvivalTool st && st.WorkStatFactors.Any(m => workRelevantStats.Contains(m.stat)));
            if (hasAnyRelevantTool && !assignmentTracker.NeedsOptimization)
                return null;

            LogDebug($"[SurvivalTools.Optimizer] Running tool optimization for {pawn.LabelShort}. Relevant stats: {string.Join(", ", workRelevantStats.Select(s => s.defName))}", $"Optimizer_Running_{pawn.ThingID}");

            // 4) Find a better tool to acquire
            var bestNewTool = FindBestToolToAcquire(pawn, workRelevantStats, curAssignment, heldTools);
            if (bestNewTool == null)
            {
                LogDebug($"[SurvivalTools.Optimizer] No better tool found for {pawn.LabelShort}.", $"Optimizer_NoBetter_{pawn.ThingID}");
                SetNextOptimizeTick(pawn);
                return null;
            }

            // 5) Make space if needed, then queue pickup
            Thing toolToDrop = GetToolToDrop(pawn, bestNewTool, workRelevantStats, heldTools);
            int heldToolOffset = 0;

            var dropBacking = toolToDrop is SurvivalTool stDrop ? SurvivalToolUtility.BackingThing(stDrop, pawn) ?? toolToDrop : toolToDrop;
            if (toolToDrop != null && AllowedToAutomaticallyDropSafe(assignmentTracker, dropBacking))
            {
                LogDebug($"[SurvivalTools.Optimizer] {pawn.LabelShort} will drop {toolToDrop.LabelShort} to make space for {bestNewTool.LabelShort}.", $"Optimizer_DropForSpace_{pawn.ThingID}");

                pawn.jobs.jobQueue.EnqueueFirst(pawn.DequipAndTryStoreSurvivalTool(toolToDrop, enqueueCurrent: false));
                heldToolOffset = -1;
            }

            if (pawn.CanCarryAnyMoreSurvivalTools(heldToolOffset))
            {
                LogDebug($"[SurvivalTools.Optimizer] {pawn.LabelShort} is creating job to pick up {bestNewTool.LabelShort}.", $"Optimizer_Pickup_{pawn.ThingID}");

                var pickupTarget = SurvivalToolUtility.BackingThing(bestNewTool, pawn) ?? (Thing)bestNewTool;
                var pickupJob = JobMaker.MakeJob(JobDefOf.TakeInventory, pickupTarget);
                pickupJob.count = 1;

                SetNextOptimizeTick(pawn, 600, 900);
                return pickupJob;
            }

            LogDebug($"[SurvivalTools.Optimizer] {pawn.LabelShort} found better tool {bestNewTool.LabelShort}, but cannot carry more.", $"Optimizer_CannotCarry_{pawn.ThingID}");

            SetNextOptimizeTick(pawn);
            return null;
        }

        #endregion

        #region Scheduling

        private void SetNextOptimizeTick(Pawn pawn, int min = -1, int max = -1)
        {
            if (min == -1 || max == -1)
            {
                if (SurvivalTools.Settings?.autoTool == true)
                {
                    min = OPTIMIZE_TICK_MIN_AUTOTOOL;
                    max = OPTIMIZE_TICK_MAX_AUTOTOOL;

                    string logKey = $"OptFreq_AutoTool_{pawn.ThingID}";
                    LogDebug($"[SurvivalTools.Optimizer] Using reduced optimization frequency for {pawn.LabelShort} (AutoTool enabled): ~{min / 2500f:F1}-{max / 2500f:F1} in-game hours", logKey);
                }
                else
                {
                    min = OPTIMIZE_TICK_MIN;
                    max = OPTIMIZE_TICK_MAX;

                    string logKey = $"OptFreq_Standard_{pawn.ThingID}";
                    LogDebug($"[SurvivalTools.Optimizer] Using standard optimization frequency for {pawn.LabelShort}: ~{min / 2500f:F1}-{max / 2500f:F1} in-game hours", logKey);
                }
            }

            pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>()?.Optimized(min, max);
        }

        #endregion

        #region Discovery & selection

        private SurvivalTool FindBestToolToAcquire(Pawn pawn, List<StatDef> workRelevantStats, SurvivalToolAssignment curAssignment, List<Thing> heldTools)
        {
            SurvivalTool bestNewTool = null;
            float bestScore = 0f;

            // Baseline: best held tool
            foreach (var tool in heldTools)
            {
                float score = SurvivalToolScore(tool, pawn, workRelevantStats);
                if (score > bestScore) bestScore = score;
            }
            if (IsDebugLoggingEnabled && pawn != null && workRelevantStats != null)
            {
                foreach (var stat in workRelevantStats)
                {
                    DumpStatDiag(pawn, stat, pawn.CurJob?.def?.defName ?? "ToolOptimization");
                }
            }

            var candidates = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];

                // A) Real SurvivalTool
                if (candidate is SurvivalTool sTool)
                {
                    if (!sTool.Spawned || sTool.IsForbidden(pawn) || sTool.IsBurning()) continue;
                    if (curAssignment?.filter != null && !curAssignment.filter.Allows(sTool)) continue;
                    if (!ToolIsAcquirableByPolicySafe(pawn, sTool)) continue;
                    if (!pawn.CanReserveAndReach(sTool, PathEndMode.OnCell, pawn.NormalMaxDanger())) continue;

                    float sScore = SurvivalToolScore(sTool, pawn, workRelevantStats);
                    if (sScore <= 0f) continue; // irrelevant tool

                    var sameTypeHeld = FindSameTypeHeldTool(pawn, sTool, heldTools);
                    if (sameTypeHeld != null)
                    {
                        float sameTypeScore = SurvivalToolScore(sameTypeHeld, pawn, workRelevantStats);
                        if (sScore <= sameTypeScore)
                        {
                            LogDebug($"[SurvivalTools.Optimizer] {pawn.LabelShort} skipping {sTool.LabelShort} (score {sScore:F2}) - has better {sameTypeHeld.LabelShort} (score {sameTypeScore:F2})", $"Optimizer_Skip_{pawn.ThingID}_{sTool.LabelShort}");
                            continue;
                        }

                        LogDebug($"[SurvivalTools.Optimizer] {pawn.LabelShort} considering {sTool.LabelShort} (score {sScore:F2}) to replace {sameTypeHeld.LabelShort} (score {sameTypeScore:F2})", $"Optimizer_Consider_{pawn.ThingID}_{sTool.LabelShort}");
                    }

                    LogDebug($"[SurvivalTools.Optimizer] {pawn.LabelShort} evaluating {sTool.LabelShort}: score {sScore:F2} vs best {bestScore:F2}", $"Optimizer_Eval_{pawn.ThingID}_{sTool.LabelShort}");

                    if (sScore > bestScore)
                    {
                        bestScore = sScore;
                        bestNewTool = sTool;
                    }

                    continue;
                }

                // B) Tool-stuff (cloth/wool/hyperweave) -> VirtualSurvivalTool
                if (candidate.def.IsToolStuff())
                {
                    var item = candidate;
                    if (!item.Spawned || item.IsForbidden(pawn) || item.IsBurning()) continue;
                    if (curAssignment?.filter != null && !curAssignment.filter.Allows(item)) continue;
                    if (!pawn.CanReserveAndReach(item, PathEndMode.OnCell, pawn.NormalMaxDanger())) continue;

                    var vtool = VirtualSurvivalTool.FromThing(item);
                    if (vtool == null) continue;

                    if (!ToolIsAcquirableByPolicySafe(pawn, vtool)) continue;

                    bool hasRelevant = vtool.WorkStatFactors.Any(m => workRelevantStats.Contains(m.stat));
                    if (!hasRelevant) continue;

                    float vScore = vtool.WorkStatFactors
                        .Where(m => workRelevantStats.Contains(m.stat))
                        .Sum(m => m.value);

                    // Distance tie-breaker
                    vScore -= 0.01f * item.Position.DistanceTo(pawn.Position);

                    LogDebug($"[SurvivalTools.Optimizer] Considering virtual tool-stuff {item.def.defName} with score {vScore:F2}", $"Optimizer_Virtual_{pawn.ThingID}_{item.def.defName}");

                    if (vScore > bestScore)
                    {
                        bestScore = vScore;
                        bestNewTool = vtool;
                    }
                }
            }

            return bestNewTool;
        }


        private Thing GetToolToDrop(Pawn pawn, SurvivalTool newTool, List<StatDef> workRelevantStats, List<Thing> heldTools)
        {
            // Prefer replacing same-type with worse score
            var sameTypeHeld = FindSameTypeHeldTool(pawn, newTool, heldTools);
            if (sameTypeHeld != null)
            {
                var tracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
                var backing = sameTypeHeld is SurvivalTool st ? SurvivalToolUtility.BackingThing(st, pawn) ?? (Thing)sameTypeHeld : (Thing)sameTypeHeld;

                if (AllowedToAutomaticallyDropSafe(tracker, backing))
                {
                    LogDebug($"[SurvivalTools.Optimizer] {pawn.LabelShort} will drop same-type tool {sameTypeHeld.LabelShort} for better {newTool.LabelShort}.", $"Optimizer_DropSameType_{pawn.ThingID}_{sameTypeHeld.LabelShort}");
                    return sameTypeHeld;
                }
            }

            // If at capacity, drop the worst allowed tool
            if (!pawn.CanCarryAnyMoreSurvivalTools())
            {
                Thing worst = null;
                float worstScore = float.MaxValue;

                foreach (var tool in heldTools)
                {
                    var tracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
                    var backing = tool is SurvivalTool st ? SurvivalToolUtility.BackingThing(st, pawn) ?? tool : tool;
                    if (!AllowedToAutomaticallyDropSafe(tracker, backing)) continue;

                    float score = SurvivalToolScore(tool, pawn, workRelevantStats);
                    if (score < worstScore)
                    {
                        worstScore = score;
                        worst = tool;
                    }
                }
                return worst;
            }

            return null;
        }

        private SurvivalTool FindSameTypeHeldTool(Pawn pawn, SurvivalTool targetTool, List<Thing> heldTools)
        {
            return heldTools.OfType<SurvivalTool>()
                .FirstOrDefault(held => AreSameToolType(held, targetTool));
        }

        private static bool AreSameToolType(SurvivalTool a, SurvivalTool b)
        {
            return ToolScoring.AreSameToolType(a, b);
        }

        #endregion

        #region Scoring


        private static float SurvivalToolScore(Thing toolThing, Pawn pawn, List<StatDef> workRelevantStats)
        {
            var tool = toolThing as SurvivalTool;
            if (tool == null) return 0f;
            return ToolScoring.CalculateToolScore(tool, pawn, workRelevantStats);
        }

        private static readonly SimpleCurve LifespanDaysToOptimalityMultiplierCurve = new SimpleCurve
        {
            new CurvePoint(0f,   0.04f),
            new CurvePoint(0.5f, 0.20f),
            new CurvePoint(1f,   0.50f),
            new CurvePoint(2f,   1.00f),
            new CurvePoint(4f,   1.00f),
            new CurvePoint(999f, 10.00f) // kept as-is from your original
        };

        #endregion

        #region Safety / policy helpers

        /// <summary>
        /// Safe wrapper for forcedHandler.AllowedToAutomaticallyDrop that handles nulls.
        /// </summary>
        private static bool AllowedToAutomaticallyDropSafe(Pawn_SurvivalToolAssignmentTracker tracker, Thing thing)
        {
            if (tracker?.parent is Pawn pawn)
                return PawnToolValidator.AllowedToAutomaticallyDrop(pawn, thing);
            return true;
        }

        // Groups both real SurvivalTools and held tool-stuff (wrapped as VirtualSurvivalTool) by their functional stat set.
        // Returns the physical Thing to drop: either a SurvivalTool to dequip/store, or a tool-stuff Thing to drop from inventory.
        private Thing FindDuplicateToolToDropIncludingVirtuals(Pawn pawn, List<Thing> heldThings)
        {
            var assignmentTracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();

            // Build a list of (physicalThing, toolLike) where toolLike exposes WorkStatFactors
            var toolLikes = new List<(Thing thing, SurvivalTool toolLike)>();
            foreach (var ht in heldThings)
            {
                if (ht is SurvivalTool st)
                {
                    toolLikes.Add((ht, st));
                    continue;
                }

                // Wrap held tool-stuff into a virtual tool for grouping/scoring
                if (ht?.def != null && ht.def.HasModExtension<SurvivalToolProperties>())
                {
                    var v = VirtualSurvivalTool.FromThing(ht);
                    if (v != null && v.WorkStatFactors.Any())
                        toolLikes.Add((ht, v));
                }
            }

            if (toolLikes.Count <= 1) return null;

            // Group by the set of stat defNames
            string KeyFor(SurvivalTool t) =>
                string.Join(",",
                    t.WorkStatFactors
                     .Select(f => f.stat?.defName)
                     .Where(n => !string.IsNullOrEmpty(n))
                     .OrderBy(n => n));

            float Score(SurvivalTool t)
            {
                float s = 0f;
                foreach (var m in t.WorkStatFactors) s += m.value;
                return s;
            }

            var groups = toolLikes
                .GroupBy(x => KeyFor(x.toolLike))
                .Where(g => !string.IsNullOrEmpty(g.Key));

            foreach (var grp in groups)
            {
                var members = grp.ToList();
                if (members.Count <= 1) continue;

                // best to keep
                var best = members.OrderByDescending(x => Score(x.toolLike)).First();

                // choose a droppable non-best member
                foreach (var m in members.OrderBy(x => Score(x.toolLike)))
                {
                    if (ReferenceEquals(m.thing, best.thing)) continue;

                    var backing = (m.thing is SurvivalTool st)
                        ? SurvivalToolUtility.BackingThing(st, pawn) ?? m.thing
                        : m.thing;

                    if (!AllowedToAutomaticallyDropSafe(assignmentTracker, backing))
                        continue;

                    return m.thing; // could be SurvivalTool OR tool-stuff Thing
                }
            }

            return null;
        }

        /// <summary>
        /// Storage/home-area policy with bounds & map checks to avoid Area.get_Item OOB.
        /// </summary>
        private static bool ToolIsAcquirableByPolicySafe(Pawn pawn, SurvivalTool tool)
        {
            if (pawn == null || pawn.Map == null || tool == null) return false;

            var backing = SurvivalToolUtility.BackingThing(tool, pawn) ?? (tool as Thing);
            if (backing == null) return false;

            // Storage always OK
            if (backing.IsInAnyStorage()) return true;

            // Respect "storage only"
            if (SurvivalTools.Settings?.pickupFromStorageOnly == true) return false;

            // Must be spawned on the pawn's map and in bounds
            if (!backing.Spawned || backing.Map != pawn.Map) return false;

            var map = pawn.Map;
            var pos = backing.Position;
            if (!pos.IsValid || !pos.InBounds(map)) return false;

            var home = map.areaManager?.Home;
            if (home == null) return false;

            return home[pos];
        }

        #endregion
    }
}
