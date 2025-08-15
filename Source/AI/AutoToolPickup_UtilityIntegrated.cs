// SurvivalTools – Auto tool pickup before work (Hardcore-aware via SurvivalToolUtility)
// RimWorld 1.6, C# 7.3
//
// How Hardcore mode is respected:
// - Helpfulness check uses SurvivalTool.BetterThanWorkingToollessFor(stat), which compares against
//   StatPart_SurvivalTool.NoToolStatFactor (your code switches this based on Settings.hardcoreMode).
// - Scoring multiplies by estimated lifespan (ST_StatDefOf.ToolEstimatedLifespan), which your
//   StatWorker_EstimatedLifespan computes with a Hardcore-adjusted BaseWearInterval.
// Result: In Hardcore, tools are considered helpful sooner (lower no-tool factor) and short-lived
// tools are down-weighted (faster wear), matching your user setting.

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;


namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    public static class Patch_JobGiver_Work_TryIssueJobPackage_AutoTool
    {
        // Tunables (can be surfaced as mod settings later)
        private const int SearchRadius = 28;
        private const bool AllowForbidden = false;

        public static void Postfix(Pawn pawn, JobIssueParams jobParams, ref ThinkResult __result)
        {
            try
            {
                // User Settings: Do you want pawns to optimize tools before jobs?
                bool autoTool = (SurvivalTools.Settings != null && SurvivalTools.Settings.autoTool);

                // If autoTool checkbox is not enabled, bail out. 
                if (!autoTool) return;

                bool debug = (SurvivalTools.Settings != null && SurvivalTools.Settings.debugLogging);//Prefs.DevMode;

                var job = __result.Job;
                if (pawn == null || pawn.Map == null || job == null)
                {
                    if (debug) Log.Message("[SurvivalTools] Skip: null pawn/map/job");
                    return;
                }

                if (debug)
                {
                    Log.Message($"[SurvivalTools] Postfix enter | Pawn='{pawn.LabelShort}' Job='{job.def?.defName ?? "null"}' WGD='{job.workGiverDef?.defName ?? "null"}'");
                }

                // Guard rails
                if (!pawn.RaceProps.Humanlike) { if (debug) Log.Message("[SurvivalTools] Skip: non-humanlike"); return; }
                if (pawn.Drafted) { if (debug) Log.Message("[SurvivalTools] Skip: drafted"); return; }
                if (pawn.InMentalState) { if (debug) Log.Message("[SurvivalTools] Skip: mental state"); return; }
                if (job.def == JobDefOf.TakeInventory) { if (debug) Log.Message("[SurvivalTools] Skip: TakeInventory recursion"); return; }
                if (!pawn.CanUseSurvivalTools()) { if (debug) Log.Message("[SurvivalTools] Skip: pawn.CanUseSurvivalTools()==false"); return; }

                var wg = job.workGiverDef;
                if (wg == null) { if (debug) Log.Message("[SurvivalTools] Skip: null workGiverDef"); return; }

                var requiredStats = wg.GetModExtension<WorkGiverExtension>()?.requiredStats;
                if (requiredStats == null || requiredStats.Count == 0)
                {
                    if (debug) Log.Message($"[SurvivalTools] Skip: no requiredStats for WGD='{wg.defName}'");
                    return;
                }

                if (!pawn.MeetsWorkGiverStatRequirements(requiredStats))
                {
                    if (debug) Log.Message("[SurvivalTools] Skip: pawn doesn't meet workgiver stat requirements");
                    return;
                }

                if (PawnHasHelpfulTool(pawn, requiredStats))
                {
                    if (debug) Log.Message("[SurvivalTools] Skip: pawn already has a helpful tool");
                    return;
                }

                // Find a best helpful tool to acquire
                SurvivalTool best = FindBestHelpfulTool(pawn, wg, requiredStats, SearchRadius, AllowForbidden);

                if (debug)
                {
                    Log.Message(
                        $"[SurvivalTools] Searching | Pawn='{pawn.LabelShort}' WGD='{wg.defName}' " +
                        $"Stats=[{string.Join(", ", requiredStats.Select(s => s.defName))}] " +
                        $"Radius={SearchRadius} AllowForbidden={AllowForbidden}"
                    );
                }

                if (best == null)
                {
                    if (debug) Log.Message("[SurvivalTools] Result: no suitable tool found");
                    return;
                }

                if (debug)
                {
                    float score = SurvivalToolScore(best, requiredStats);
                    Log.Message(
                        $"[SurvivalTools] Selected | Tool='{best.LabelCap}' HP={best.HitPoints}/{best.MaxHitPoints} " +
                        $"Score={score:F3} Stats=[{string.Join(", ", requiredStats.Select(s => s.defName))}]"
                    );
                }

                // Drop-to-swap (optional Hardcore gate—uncomment to enable only in Hardcore)
                // bool allowSwap = SurvivalTools.Settings?.hardcoreMode == true;
                bool allowSwap = true;

                var tracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
                SurvivalTool droppable = null;
                if (allowSwap && !pawn.CanCarryAnyMoreSurvivalTools())
                {
                    droppable = FindDroppableHeldTool(pawn, requiredStats, tracker, best);
                    if (debug)
                    {
                        Log.Message(droppable != null
                            ? $"[SurvivalTools] Swap: will drop '{droppable.LabelCap}' to make room"
                            : "[SurvivalTools] Swap: no droppable tool found (will try pickup anyway if capacity allows)");
                    }
                }

                // Queue: (1) original job, (2) pickup, (3) optional drop runs NOW or pickup runs NOW
                pawn.jobs?.jobQueue?.EnqueueFirst(job);

                var pickup = JobMaker.MakeJob(JobDefOf.TakeInventory, best);
                pickup.count = 1;
                pickup.playerForced = job.playerForced; // preserve intent
                pawn.jobs?.jobQueue?.EnqueueFirst(pickup);

                if (droppable != null)
                {
                    var dropJob = pawn.DequipAndTryStoreSurvivalTool(droppable, enqueueCurrent: false);
                    if (debug) Log.Message($"[SurvivalTools] Now executing: Drop '{droppable.LabelCap}'");
                    __result = new ThinkResult(dropJob, __result.SourceNode, __result.Tag, fromQueue: false);
                }
                else
                {
                    if (debug) Log.Message($"[SurvivalTools] Now executing: Pickup '{best.LabelCap}'");
                    __result = new ThinkResult(pickup, __result.SourceNode, __result.Tag, fromQueue: false);
                }
            }
            catch (Exception e)
            {
                // Collapse spammy errors
                int key = Gen.HashCombineInt(Gen.HashCombineInt("ST_AutoTool_Patch".GetHashCode(), pawn?.thingIDNumber ?? 0), __result.Job?.def?.shortHash ?? 0);
                Log.WarningOnce($"[SurvivalTools] AutoTool pre-work pickup failed: {e}", key);
            }
        }


        // Pick a held tool we’re allowed to auto-drop that doesn’t help the required stats,
        // or (if all help) the strictly-worst one vs the target “best” we plan to pick up.
        private static SurvivalTool FindDroppableHeldTool(
            Pawn pawn,
            List<StatDef> requiredStats,
            Pawn_SurvivalToolAssignmentTracker tracker,
            SurvivalTool toolWeWant)
        {
            if (!pawn.CanRemoveExcessSurvivalTools()) return null; // your utility guard

            // Only tools under the carry limit context (inventory list)
            var held = pawn.GetHeldSurvivalTools().ToList();
            if (held.Count == 0) return null;

            // Filter to tools we’re allowed to auto-drop
            var droppables = new List<SurvivalTool>();
            for (int i = 0; i < held.Count; i++)
            {
                var st = held[i] as SurvivalTool;
                if (st == null) continue;
                if (tracker != null && !tracker.forcedHandler.AllowedToAutomaticallyDrop(st)) continue;
                droppables.Add(st);
            }
            if (droppables.Count == 0) return null;

            // 1) Prefer dropping any tool that does NOT help these required stats at all
            for (int i = 0; i < droppables.Count; i++)
            {
                if (!ToolImprovesAnyRequiredStat(droppables[i], requiredStats))
                    return droppables[i];
            }

            // 2) Otherwise, drop the strictly worst helpful tool compared to the one we want
            //    (sum of factors across required stats as in SurvivalToolScore, but without lifespan)
            float wantScore = 0f;
            for (int i = 0; i < requiredStats.Count; i++)
                wantScore += GetStatValueFromEnumerable(toolWeWant.WorkStatFactors, requiredStats[i], 0f);

            SurvivalTool worst = null;
            float worstScore = float.MaxValue;

            for (int i = 0; i < droppables.Count; i++)
            {
                float s = 0f;
                for (int j = 0; j < requiredStats.Count; j++)
                    s += GetStatValueFromEnumerable(droppables[i].WorkStatFactors, requiredStats[j], 0f);

                // Only consider dropping if it’s strictly worse than the tool we want
                if (s < wantScore && s < worstScore)
                {
                    worstScore = s;
                    worst = droppables[i];
                }
            }

            return worst;
        }


        // ---------- Selection & scoring (Hardcore-aware) ----------

        private static bool PawnHasHelpfulTool(Pawn pawn, List<StatDef> requiredStats)
        {
            // Equipment
            if (pawn.equipment != null)
            {
                var eq = pawn.equipment.AllEquipmentListForReading;
                for (int i = 0; i < eq.Count; i++)
                {
                    var st = eq[i] as SurvivalTool;
                    if (st != null && ToolImprovesAnyRequiredStat(st, requiredStats))
                        return true;
                }
            }
            // Inventory (only tools under carry limit)
            foreach (var t in pawn.GetUsableHeldSurvivalTools())
            {
                var st = t as SurvivalTool;
                if (st != null && ToolImprovesAnyRequiredStat(st, requiredStats))
                    return true;
            }
            return false;
        }

        private static SurvivalTool FindBestHelpfulTool(Pawn pawn, WorkGiverDef wg, List<StatDef> requiredStats, int radius, bool allowForbidden)
        {
            var map = pawn.Map;
            if (map == null) return null;

            var tracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
            var assignment = tracker != null ? tracker.CurrentSurvivalToolAssignment : null;

            // Prefer inventory
            SurvivalTool best = null;
            float bestScore = 0f;

            foreach (var t in pawn.GetUsableHeldSurvivalTools())
            {
                var st = t as SurvivalTool;
                if (st == null) continue;
                if (!AllowedByAssignment(assignment, st)) continue;

                var score = SurvivalToolScore(st, requiredStats);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = st;
                }
            }

            // Then equipment
            if (pawn.equipment != null)
            {
                var eq = pawn.equipment.AllEquipmentListForReading;
                for (int i = 0; i < eq.Count; i++)
                {
                    var st = eq[i] as SurvivalTool;
                    if (st == null) continue;
                    if (!AllowedByAssignment(assignment, st)) continue;

                    var score = SurvivalToolScore(st, requiredStats);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = st;
                    }
                }
            }

            if (best != null)
                return best;

            // Ground search
            SurvivalTool bestGround = null;
            float bestGroundScore = float.MinValue;

            foreach (var c in GenRadial.RadialCellsAround(pawn.Position, radius, true))
            {
                if (!c.InBounds(map)) continue;
                var list = c.GetThingList(map);
                for (int i = 0; i < list.Count; i++)
                {
                    var st = list[i] as SurvivalTool;
                    if (st == null || !st.Spawned) continue;

                    if (!pawn.CanUseSurvivalTool(st.def)) continue;
                    if (!AllowedByAssignment(assignment, st)) continue;
                    if (!ToolIsAcquirableByPolicy(pawn, st)) continue;
                    if (!allowForbidden && st.IsForbidden(pawn)) continue;
                    if (st.IsBurning()) continue;
                    if (!pawn.CanReserveAndReach(st, PathEndMode.OnCell, pawn.NormalMaxDanger())) continue;
                    if (!ToolImprovesAnyRequiredStat(st, requiredStats)) continue;

                    float sc = SurvivalToolScore(st, requiredStats);
                    if (sc <= 0f) continue;

                    sc -= 0.01f * c.DistanceTo(pawn.Position); // prefer closer on ties

                    if (sc > bestGroundScore)
                    {
                        bestGroundScore = sc;
                        bestGround = st;
                    }
                }
            }

            return bestGround;
        }


        // Assignment filter (if none, allow)
        private static bool AllowedByAssignment(SurvivalToolAssignment assignment, SurvivalTool tool)
        {
            return assignment == null || assignment.filter == null || assignment.filter.Allows(tool);
        }

        // Hardcore-aware helpfulness: uses SurvivalTool.BetterThanWorkingToollessFor(stat)
        // which reads StatPart_SurvivalTool.NoToolStatFactor (hardcore toggles this).
        private static bool ToolImprovesAnyRequiredStat(SurvivalTool tool, List<StatDef> requiredStats)
        {
            for (int i = 0; i < requiredStats.Count; i++)
            {
                if (tool.BetterThanWorkingToollessFor(requiredStats[i]))
                    return true;
            }
            return false;
        }

        // Scoring: sum WorkStatFactors for the job’s required stats and multiply by
        // Hardcore-aware estimated lifespan (via ST_StatDefOf.ToolEstimatedLifespan).
        private static float SurvivalToolScore(Thing toolThing, List<StatDef> workRelevantStats)
        {
            var tool = toolThing as SurvivalTool;
            if (tool == null) return 0f;

            float optimality = 0f;

            for (int i = 0; i < workRelevantStats.Count; i++)
            {
                optimality += GetStatValueFromEnumerable(tool.WorkStatFactors, workRelevantStats[i], 0f);
            }

            if (tool.def.useHitPoints)
            {
                float hpFrac = tool.MaxHitPoints > 0 ? (float)tool.HitPoints / tool.MaxHitPoints : 0f;

                // StatWorker_EstimatedLifespan already factors Hardcore (BaseWearInterval) and degradation setting
                float lifespanDays = tool.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan);
                float lifespanRemaining = lifespanDays * hpFrac;

                optimality *= LifespanDaysToOptimalityMultiplierCurve.Evaluate(lifespanRemaining);
            }

            return optimality;
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

        // Curve: low value if breaking imminently; full value after ~2–4 days.
        private static readonly SimpleCurve LifespanDaysToOptimalityMultiplierCurve = new SimpleCurve
        {
            new CurvePoint(0f,   0.04f),
            new CurvePoint(0.5f, 0.20f),
            new CurvePoint(1f,   0.50f),
            new CurvePoint(2f,   1.00f),
            new CurvePoint(4f,   1.00f),
            new CurvePoint(999f, 1.00f)
        };

        // ---------- Storage/Home policy (mirrors your optimize jobgiver) ----------

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
    }
}
