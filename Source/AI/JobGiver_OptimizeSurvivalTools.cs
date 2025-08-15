using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SurvivalTools
{
    public class JobGiver_OptimizeSurvivalTools : ThinkNode_JobGiver
    {
        // Tuning knobs
        private const int OPTIMIZE_TICK_MIN = 3600;   // ~3.0 in-game hours
        private const int OPTIMIZE_TICK_MAX = 14400;   // ~6.0 in-game hours

        private void SetNextOptimizeTick(Pawn pawn, int min = OPTIMIZE_TICK_MIN, int max = OPTIMIZE_TICK_MAX)
        {
            var comp = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
            if (comp != null)
            {
                comp.nextSurvivalToolOptimizeTick = Find.TickManager.TicksGame + Rand.Range(min, max);
            }
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (SurvivalTools.Settings == null || !SurvivalTools.Settings.toolOptimization)
                return null;

            var assignmentTracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
            if (!pawn.CanUseSurvivalTools() || assignmentTracker == null)
                return null;

            var map = pawn.MapHeld;
            if (map == null)
            {
                SetNextOptimizeTick(pawn);
                return null;
            }

            var curAssignment = assignmentTracker.CurrentSurvivalToolAssignment;
            var heldToolsEnum = pawn.GetHeldSurvivalTools();
            var heldTools = heldToolsEnum as IList<Thing> ?? heldToolsEnum.ToList();

            // Drop anything we shouldn't be holding or don't need (unchanged behavior).
            foreach (var tool in heldTools)
            {
                var st = tool as SurvivalTool;
                if (st == null)
                    continue;

                if ((!curAssignment.filter.Allows(st) || !pawn.NeedsSurvivalTool(st)) &&
                    assignmentTracker.forcedHandler.AllowedToAutomaticallyDrop(tool))
                {
                    // Short cooldown after a drop so we can re-evaluate again soon.
                    SetNextOptimizeTick(pawn, 300, 600);
                    return pawn.DequipAndTryStoreSurvivalTool(tool);
                }
            }

            // Make stat list distinct to avoid redundant work.
            var workRelevantStats = pawn.AssignedToolRelevantWorkGiversStatDefs();
            if (workRelevantStats == null || workRelevantStats.Count == 0)
            {
                SetNextOptimizeTick(pawn);
                return null;
            }
            workRelevantStats = workRelevantStats.Distinct().ToList();

            // If the pawn doesn't have any tool for these stats, bypass the cooldown.
            bool hasAnyRelevantTool = PawnHasAnyToolForStats(heldTools, workRelevantStats);

            // Respect cooldown unless we have no relevant tool at all.
            int now = Find.TickManager.TicksGame;
            if (hasAnyRelevantTool && now < assignmentTracker.nextSurvivalToolOptimizeTick)
                return null;

            // Consider only SurvivalTools on the map (fast filter over AllThings is fine, but we keep it tight).
            var mapThings = map.listerThings.AllThings;
            if (mapThings == null || mapThings.Count == 0)
            {
                SetNextOptimizeTick(pawn);
                return null;
            }

            Thing curTool = null;
            SurvivalTool newTool = null;
            float optimality = 0f;

            foreach (var stat in workRelevantStats)
            {
                curTool = pawn.GetBestSurvivalTool(stat);
                optimality = SurvivalToolScore(curTool, workRelevantStats);

                // Scan candidates; allow Home-area pickups if enabled.
                for (int i = 0; i < mapThings.Count; i++)
                {
                    var potentialTool = mapThings[i] as SurvivalTool;
                    if (potentialTool == null || !potentialTool.Spawned)
                        continue;

                    if (!ContainsStat(potentialTool.WorkStatFactors, stat))
                        continue;

                    if (!curAssignment.filter.Allows(potentialTool))
                        continue;

                    if (!potentialTool.BetterThanWorkingToollessFor(stat))
                        continue;

                    if (!pawn.CanUseSurvivalTool(potentialTool.def))
                        continue;

                    // Storage/Home gating
                    if (!ToolIsAcquirableByPolicy(pawn, potentialTool))
                        continue;

                    if (potentialTool.IsForbidden(pawn) || potentialTool.IsBurning())
                        continue;

                    float potentialOptimality = SurvivalToolScore(potentialTool, workRelevantStats);
                    if (potentialOptimality <= optimality)
                        continue;

                    if (!pawn.CanReserveAndReach(potentialTool, PathEndMode.OnCell, pawn.NormalMaxDanger()))
                        continue;

                    newTool = potentialTool;
                    optimality = potentialOptimality;
                }

                if (newTool != null)
                    break;
            }

            if (newTool == null)
            {
                SetNextOptimizeTick(pawn);
                return null;
            }

            int heldToolOffset = 0;
            if (curTool != null && assignmentTracker.forcedHandler.AllowedToAutomaticallyDrop(curTool))
            {
                pawn.jobs.jobQueue.EnqueueFirst(pawn.DequipAndTryStoreSurvivalTool(curTool, enqueueCurrent: false));
                heldToolOffset = -1;
            }

            if (pawn.CanCarryAnyMoreSurvivalTools(heldToolOffset))
            {
                var pickupJob = JobMaker.MakeJob(JobDefOf.TakeInventory, newTool);
                pickupJob.count = 1;

                // Set a short cooldown so we don't spam evaluations while the pickup is happening.
                SetNextOptimizeTick(pawn, 600, 900);
                return pickupJob;
            }

            SetNextOptimizeTick(pawn);
            return null;
        }

        private static bool PawnHasAnyToolForStats(IEnumerable<Thing> heldTools, List<StatDef> stats)
        {
            foreach (var t in heldTools)
            {
                var st = t as SurvivalTool;
                if (st == null) continue;

                foreach (var s in stats)
                {
                    if (ContainsStat(st.WorkStatFactors, s))
                        return true;
                }
            }
            return false;
        }

        private static bool ToolIsAcquirableByPolicy(Pawn pawn, SurvivalTool tool)
        {
            if (tool.IsInAnyStorage())
                return true;

            bool storageOnly = SurvivalTools.Settings != null && SurvivalTools.Settings.pickupFromStorageOnly;
            if (storageOnly)
                return false;

            var map = pawn.Map;
            if (map == null || map.areaManager == null)
                return false;

            var home = map.areaManager.Home;
            return home != null && home[tool.Position];
        }

        private static float SurvivalToolScore(Thing toolThing, List<StatDef> workRelevantStats)
        {
            var tool = toolThing as SurvivalTool;
            if (tool == null)
                return 0f;

            float optimality = 0f;

            foreach (var stat in workRelevantStats)
            {
                optimality += GetStatValueFromEnumerable(tool.WorkStatFactors, stat, 0f);
            }

            if (tool.def.useHitPoints)
            {
                float hpFrac = tool.MaxHitPoints > 0 ? (float)tool.HitPoints / tool.MaxHitPoints : 0f;
                float lifespanRemaining = tool.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan) * hpFrac;
                optimality *= LifespanDaysToOptimalityMultiplierCurve.Evaluate(lifespanRemaining);
            }

            return optimality;
        }

        private static bool ContainsStat(IEnumerable<StatModifier> mods, StatDef stat)
        {
            if (mods == null) return false;
            foreach (var m in mods)
                if (m != null && m.stat == stat)
                    return true;
            return false;
        }

        private static float GetStatValueFromEnumerable(IEnumerable<StatModifier> mods, StatDef stat, float defaultValue)
        {
            if (mods != null)
            {
                foreach (var m in mods)
                    if (m != null && m.stat == stat)
                        return m.value;
            }
            return defaultValue;
        }

        private static readonly SimpleCurve LifespanDaysToOptimalityMultiplierCurve = new SimpleCurve
        {
            new CurvePoint(0f,   0.04f),
            new CurvePoint(0.5f, 0.20f),
            new CurvePoint(1f,   0.50f),
            new CurvePoint(2f,   1.00f),
            new CurvePoint(4f,   1.00f),
            new CurvePoint(999f, 10.0f)
        };
    }
}
