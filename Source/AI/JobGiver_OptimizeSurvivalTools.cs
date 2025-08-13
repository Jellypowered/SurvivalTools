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
        private void SetNextOptimizeTick(Pawn pawn)
        {
            var comp = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
            if (comp != null)
            {
                comp.nextSurvivalToolOptimizeTick = Find.TickManager.TicksGame + Rand.Range(6000, 9000);
            }
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (SurvivalTools.Settings == null || !SurvivalTools.Settings.toolOptimization)
                return null;

            var assignmentTracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
            if (!pawn.CanUseSurvivalTools() ||
                assignmentTracker == null ||
                Find.TickManager.TicksGame < assignmentTracker.nextSurvivalToolOptimizeTick)
            {
                return null;
            }

            var map = pawn.MapHeld;
            if (map == null)
            {
                SetNextOptimizeTick(pawn);
                return null;
            }

            var curAssignment = assignmentTracker.CurrentSurvivalToolAssignment;
            var heldTools = pawn.GetHeldSurvivalTools();

            foreach (var tool in heldTools)
            {
                var st = tool as SurvivalTool;
                if (st == null)
                    continue;

                if ((!curAssignment.filter.Allows(st) || !pawn.NeedsSurvivalTool(st)) &&
                    assignmentTracker.forcedHandler.AllowedToAutomaticallyDrop(tool))
                {
                    return pawn.DequipAndTryStoreSurvivalTool(tool);
                }
            }

            List<Thing> mapThings = map.listerThings.AllThings;
            if (mapThings == null || mapThings.Count == 0)
            {
                SetNextOptimizeTick(pawn);
                return null;
            }

            var workRelevantStats = pawn.AssignedToolRelevantWorkGiversStatDefs();
            Thing curTool = null;
            SurvivalTool newTool = null;
            float optimality = 0f;

            var heldList = heldTools as IList<Thing> ?? heldTools.ToList();

            foreach (var stat in workRelevantStats)
            {
                curTool = pawn.GetBestSurvivalTool(stat);
                optimality = SurvivalToolScore(curTool, workRelevantStats);

                for (int i = 0; i < mapThings.Count; i++)
                {
                    var potentialTool = mapThings[i] as SurvivalTool;
                    if (potentialTool == null)
                        continue;

                    // Was: StatUtility.StatListContains(potentialTool.WorkStatFactors, stat)
                    if (!ContainsStat(potentialTool.WorkStatFactors, stat))
                        continue;

                    if (!curAssignment.filter.Allows(potentialTool))
                        continue;

                    if (!potentialTool.BetterThanWorkingToollessFor(stat))
                        continue;

                    if (!pawn.CanUseSurvivalTool(potentialTool.def))
                        continue;
                    if (!potentialTool.IsInAnyStorage())
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
                return pickupJob;
            }

            SetNextOptimizeTick(pawn);
            return null;
        }

        private static float SurvivalToolScore(Thing toolThing, List<StatDef> workRelevantStats)
        {
            var tool = toolThing as SurvivalTool;
            if (tool == null)
                return 0f;

            float optimality = 0f;

            // Was: StatUtility.GetStatValueFromList(tool.WorkStatFactors, stat, 0f)
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

        // Helpers that work with IEnumerable<StatModifier> (no List<> required)
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
