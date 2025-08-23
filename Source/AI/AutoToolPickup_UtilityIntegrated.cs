// SurvivalTools – Auto tool pickup before work (Hardcore-aware + verbose logging)
// RimWorld 1.6, C# 7.3

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
        // Tunables (surface as settings if you like)
        private const int SearchRadius = 28;
        private const bool AllowForbidden = false;

        public static void Postfix(Pawn pawn, JobIssueParams jobParams, ref ThinkResult __result)
        {
            try
            {
                bool autoTool = SurvivalTools.Settings?.autoTool == true;
                if (!autoTool) return;

                bool debug = SurvivalTools.Settings?.debugLogging == true;

                var job = __result.Job;
                if (pawn == null || pawn.Map == null || job == null)
                {
                    if (debug) Log.Message("[SurvivalTools] Skip: null pawn/map/job");
                    return;
                }

                if (debug)
                    Log.Message($"[SurvivalTools] Postfix enter | Pawn='{pawn.LabelShort}' Job='{job.def?.defName ?? "null"}' WGD='{job.workGiverDef?.defName ?? "null"}'");

                // Guard rails
                if (!pawn.RaceProps.Humanlike) { if (debug) Log.Message("[SurvivalTools] Skip: non-humanlike"); return; }
                if (pawn.Drafted) { if (debug) Log.Message("[SurvivalTools] Skip: drafted"); return; }
                if (pawn.InMentalState) { if (debug) Log.Message("[SurvivalTools] Skip: mental state"); return; }
                if (job.def == JobDefOf.TakeInventory) { if (debug) Log.Message("[SurvivalTools] Skip: TakeInventory recursion"); return; }
                if (!pawn.CanUseSurvivalTools()) { if (debug) Log.Message("[SurvivalTools] Skip: pawn.CanUseSurvivalTools()==false"); return; }

                var wg = job.workGiverDef;
                if (wg == null) { if (debug) Log.Message("[SurvivalTools] Skip: null workGiverDef"); return; }

                // Prefer extension; fall back to our job mapping (now ST-stat aware)
                var requiredStats = SurvivalToolUtility.RelevantStatsFor(wg, job);
                if (requiredStats == null || requiredStats.Count == 0)
                {
                    if (debug) Log.Message($"[SurvivalTools] Skip: no requiredStats (WGD='{wg.defName}', Job='{job.def?.defName ?? "null"}')");
                    return;
                }

                // Let vanilla gates run (skills, work tags etc.). For Hardcore, we *don't* hard-fail
                // here on missing tool (that check moved to the Hardcore section below).
                if (!pawn.MeetsWorkGiverStatRequirements(requiredStats))
                {
                    if (debug) Log.Message("[SurvivalTools] Skip: pawn doesn't meet workgiver base/stat requirements");
                    return;
                }

                // ---------------- Hardcore backstop ----------------
                var s = SurvivalTools.Settings;
                if (s != null && s.hardcoreMode)
                {
                    bool hasAnyTool = false;
                    for (int i = 0; i < requiredStats.Count; i++)
                        if (pawn.HasSurvivalToolFor(requiredStats[i])) { hasAnyTool = true; break; }

                    bool canAcquireNow = hasAnyTool
                        || CanAcquireHelpfulToolNow(pawn, wg, requiredStats, SearchRadius, AllowForbidden, debug);

                    if (!canAcquireNow)
                    {
                        if (debug)
                            Log.Message($"[SurvivalTools] Hardcore cancel | Pawn='{pawn.LabelShort}' Job='{job.def?.defName}' WGD='{wg?.defName}' — no required tool and none reachable.");
                        __result = ThinkResult.NoJob; // cancel so pawn picks a different job
                        return;
                    }
                }
                // ---------------------------------------------------

                // If we already have something helpful, nothing to do.
                if (PawnHasHelpfulTool(pawn, requiredStats))
                {
                    if (debug) Log.Message("[SurvivalTools] Skip: pawn already has a helpful tool");
                    return;
                }

                // Try to find the best tool we can get right now (with verbose reasons)
                var best = FindBestHelpfulTool(pawn, wg, requiredStats, SearchRadius, AllowForbidden, verbose: debug);
                if (best == null)
                {
                    if (debug) Log.Message("[SurvivalTools] Result: no suitable tool found");
                    return;
                }

                if (debug)
                {
                    float score = SurvivalToolScore(best, requiredStats);
                    Log.Message(
                        $"[SurvivalTools] Selected | Tool='{best.LabelCap}' pos={best.Position} HP={best.HitPoints}/{best.MaxHitPoints} " +
                        $"Score={score:F3} Stats=[{string.Join(", ", requiredStats.Select(sdef => sdef.defName))}] " +
                        $"InStorage={best.IsInAnyStorage()} Forbidden={best.IsForbidden(pawn)}"
                    );
                }

                // If we lack capacity, try to drop something first (always on here)
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

                // Queue: original job (last), pickup, (optional dropNear)
                pawn.jobs?.jobQueue?.EnqueueFirst(DuplicateJob(job));

                var pickup = JobMaker.MakeJob(JobDefOf.TakeInventory, best);
                pickup.count = 1;
                pickup.playerForced = job.playerForced;
                pawn.jobs?.jobQueue?.EnqueueFirst(DuplicateJob(pickup));

                if (droppable != null)
                {
                    var dropNear = DequipAndTryStoreSurvivalToolNear(pawn, droppable, best.Position, enqueueCurrent: false);
                    pawn.jobs?.jobQueue?.EnqueueFirst(DuplicateJob(dropNear));

                    var gotoBest = JobMaker.MakeJob(JobDefOf.Goto, best.Position);
                    gotoBest.playerForced = job.playerForced;

                    // Start NOW with a fresh instance too (not a reference that might be shared)
                    __result = new ThinkResult(gotoBest, __result.SourceNode, __result.Tag, fromQueue: false);
                }
                else
                {
                    // Start NOW with a fresh pickup job instance (not the one we enqueued)
                    var pickupNow = DuplicateJob(pickup);
                    __result = new ThinkResult(pickupNow, __result.SourceNode, __result.Tag, fromQueue: false);
                }
            }

            catch (Exception e)
            {
                int key = Gen.HashCombineInt(Gen.HashCombineInt("ST_AutoTool_Patch".GetHashCode(), pawn?.thingIDNumber ?? 0), __result.Job?.def?.shortHash ?? 0);
                Log.WarningOnce($"[SurvivalTools] AutoTool pre-work pickup failed: {e}", key);
            }
        }

        // Create a fresh Job instance so the cached driver isn’t shared between pawns.
        private static Job DuplicateJob(Job src)
        {
            if (src == null) return null;

            // New job with the same def & primary targets
            var j = JobMaker.MakeJob(src.def, src.targetA, src.targetB, src.targetC);

            // Common, safe-to-copy fields
            j.count = src.count;
            j.playerForced = src.playerForced;
            j.locomotionUrgency = src.locomotionUrgency;
            j.haulMode = src.haulMode;
            j.ignoreForbidden = src.ignoreForbidden;
            j.expiryInterval = src.expiryInterval;
            j.checkOverrideOnExpire = src.checkOverrideOnExpire;

            // Queues (clone lists if present)
            j.targetQueueA = src.targetQueueA != null ? new List<LocalTargetInfo>(src.targetQueueA) : null;
            j.targetQueueB = src.targetQueueB != null ? new List<LocalTargetInfo>(src.targetQueueB) : null;
            j.countQueue = src.countQueue != null ? new List<int>(src.countQueue) : null;

            // Try to copy a few optional members by name (exist in some versions/mods, not others)
            TryCopyMemberByName(j, src, "exitMapOnArrival");
            TryCopyMemberByName(j, src, "haulOpportunisticDuplicates");
            TryCopyMemberByName(j, src, "ignoreDesignations");
            TryCopyMemberByName(j, src, "killIncappedTarget");
            TryCopyMemberByName(j, src, "takesTime");
            TryCopyMemberByName(j, src, "chopWoodAmount"); // example of mod-added fields
            TryCopyMemberByName(j, src, "failIfCantJoinOrBeginNow"); // present in some RW versions
            TryCopyMemberByName(j, src, "endIfCantJoinColony");      // present in some RW versions

            return j;
        }

        // Copy a field or property named `member` from src -> dst if it exists on both.
        private static void TryCopyMemberByName(object dst, object src, string member)
        {
            if (dst == null || src == null || string.IsNullOrEmpty(member)) return;

            var srcType = src.GetType();
            var dstType = dst.GetType();

            var sField = AccessTools.Field(srcType, member);
            var dField = AccessTools.Field(dstType, member);
            if (sField != null && dField != null && sField.FieldType == dField.FieldType)
            {
                dField.SetValue(dst, sField.GetValue(src));
                return;
            }

            var sProp = AccessTools.Property(srcType, member);
            var dProp = AccessTools.Property(dstType, member);
            if (sProp != null && dProp != null && sProp.CanRead && dProp.CanWrite &&
                sProp.PropertyType == dProp.PropertyType)
            {
                dProp.SetValue(dst, sProp.GetValue(src, null), null);
            }
        }

        // --------- Verbose helper for Hardcore pre-check ---------
        private static bool CanAcquireHelpfulToolNow(Pawn pawn, WorkGiverDef wg, List<StatDef> stats, int radius, bool allowForbidden, bool verbose)
        {
            var best = FindBestHelpfulTool(pawn, wg, stats, radius, allowForbidden, verbose);
            return best != null;
        }

        // Pick a held tool to drop
        private static SurvivalTool FindDroppableHeldTool(Pawn pawn, List<StatDef> requiredStats, Pawn_SurvivalToolAssignmentTracker tracker, SurvivalTool toolWeWant)
        {
            if (!pawn.CanRemoveExcessSurvivalTools()) return null;

            var held = pawn.GetHeldSurvivalTools().ToList();
            if (held.Count == 0) return null;

            var droppables = new List<SurvivalTool>();
            for (int i = 0; i < held.Count; i++)
            {
                var st = held[i] as SurvivalTool;
                if (st == null) continue;
                if (tracker != null && !tracker.forcedHandler.AllowedToAutomaticallyDrop(st)) continue;
                droppables.Add(st);
            }
            if (droppables.Count == 0) return null;

            for (int i = 0; i < droppables.Count; i++)
                if (!ToolImprovesAnyRequiredStat(droppables[i], requiredStats))
                    return droppables[i];

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

                if (s < wantScore && s < worstScore)
                {
                    worstScore = s;
                    worst = droppables[i];
                }
            }

            return worst;
        }

        // ---------- Selection & scoring (with verbose reasoned logging) ----------
        public static class Patch_JobGiver_Work_TryIssueJobPackage_AutoTool_FindBest
        {
            public static SurvivalTool FindBestHelpfulTool(Pawn pawn, WorkGiverDef wg, List<StatDef> stats)
                => Patch_JobGiver_Work_TryIssueJobPackage_AutoTool.FindBestHelpfulTool(pawn, wg, stats, 28, false, false);
        }

        private static SurvivalTool FindBestHelpfulTool(Pawn pawn, WorkGiverDef wg, List<StatDef> requiredStats, int radius, bool allowForbidden, bool verbose = false)
        {
            var map = pawn.Map;
            if (map == null) return null;

            // Counters for diagnostics
            int cNotTool = 0, cForbidden = 0, cNoStat = 0, cAssignBlock = 0, cPolicyBlock = 0, cUnreach = 0, cScoreLE = 0;

            var tracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
            var assignment = tracker != null ? tracker.CurrentSurvivalToolAssignment : null;
            bool storageOnly = SurvivalTools.Settings != null && SurvivalTools.Settings.pickupFromStorageOnly;

            // First: inventory + equipment (fast path)
            SurvivalTool best = null;
            float bestScore = 0f;

            foreach (var t in pawn.GetUsableHeldSurvivalTools())
            {
                var st = t as SurvivalTool;
                if (st == null) { cNotTool++; continue; }
                if (!AllowedByAssignment(assignment, st)) { cAssignBlock++; continue; }
                if (!ToolImprovesAnyRequiredStat(st, requiredStats)) { cNoStat++; continue; }

                float sc = SurvivalToolScore(st, requiredStats);
                if (sc > bestScore) { bestScore = sc; best = st; }
            }

            if (pawn.equipment != null)
            {
                var eq = pawn.equipment.AllEquipmentListForReading;
                for (int i = 0; i < eq.Count; i++)
                {
                    var st = eq[i] as SurvivalTool;
                    if (st == null) { cNotTool++; continue; }
                    if (!AllowedByAssignment(assignment, st)) { cAssignBlock++; continue; }
                    if (!ToolImprovesAnyRequiredStat(st, requiredStats)) { cNoStat++; continue; }

                    float sc = SurvivalToolScore(st, requiredStats);
                    if (sc > bestScore) { bestScore = sc; best = st; }
                }
            }

            if (best != null)
            {
                if (verbose)
                {
                    Log.Message(
                        $"[SurvivalTools] Finder: selected from held/equipped '{best.LabelCap}' Score={bestScore:F3}"
                    );
                }
                return best;
            }

            // Ground search in radius
            SurvivalTool bestGround = null;
            float bestGroundScore = float.MinValue;

            foreach (var c in GenRadial.RadialCellsAround(pawn.Position, radius, true))
            {
                if (!c.InBounds(map)) continue;
                var list = c.GetThingList(map);
                for (int i = 0; i < list.Count; i++)
                {
                    var st = list[i] as SurvivalTool;
                    if (st == null || !st.Spawned) { cNotTool++; continue; }

                    if (!pawn.CanUseSurvivalTool(st.def)) { cNoStat++; continue; } // no usable work stats for pawn
                    if (!AllowedByAssignment(assignment, st)) { cAssignBlock++; continue; }
                    if (!ToolIsAcquirableByPolicy(pawn, st)) { cPolicyBlock++; continue; }
                    if (!allowForbidden && st.IsForbidden(pawn)) { cForbidden++; continue; }
                    if (st.IsBurning()) { cPolicyBlock++; continue; }
                    if (!pawn.CanReserveAndReach(st, PathEndMode.OnCell, pawn.NormalMaxDanger())) { cUnreach++; continue; }
                    if (!ToolImprovesAnyRequiredStat(st, requiredStats)) { cNoStat++; continue; }

                    float sc = SurvivalToolScore(st, requiredStats);
                    if (sc <= 0f) { cScoreLE++; continue; }

                    sc -= 0.01f * c.DistanceTo(pawn.Position); // prefer closer on ties

                    if (sc > bestGroundScore)
                    {
                        bestGroundScore = sc;
                        bestGround = st;
                    }
                }
            }

            if (verbose)
            {
                if (bestGround != null)
                {
                    Log.Message(
                        $"[SurvivalTools] Finder: ground pick '{bestGround.LabelCap}' pos={bestGround.Position} Score={bestGroundScore:F3} " +
                        $"InStorage={bestGround.IsInAnyStorage()} Forbidden={bestGround.IsForbidden(pawn)}"
                    );
                }
                else
                {
                    Log.Message(
                        "[SurvivalTools] Finder: no candidate found.\n" +
                        $"  NotTool={cNotTool} Forbidden={cForbidden} NoRelevantStat={cNoStat}\n" +
                        $"  AssignmentBlocked={cAssignBlock} PolicyBlocked={cPolicyBlock}\n" +
                        $"  Unreachable={cUnreach} Score<=0={cScoreLE} Radius={radius} AllowForbidden={allowForbidden} PickupFromStorageOnly={storageOnly}"
                    );
                }
            }

            return bestGround;
        }

        private static bool AllowedByAssignment(SurvivalToolAssignment assignment, SurvivalTool tool)
            => assignment == null || assignment.filter == null || assignment.filter.Allows(tool);

        // Hardcore-aware helpfulness via StatPart_SurvivalTool
        private static bool ToolImprovesAnyRequiredStat(SurvivalTool tool, List<StatDef> requiredStats)
        {
            for (int i = 0; i < requiredStats.Count; i++)
                if (tool.BetterThanWorkingToollessFor(requiredStats[i]))
                    return true;
            return false;
        }

        // Sum of relevant factors, adjusted by remaining lifespan curve
        private static float SurvivalToolScore(Thing toolThing, List<StatDef> workRelevantStats)
        {
            var tool = toolThing as SurvivalTool;
            if (tool == null) return 0f;

            float optimality = 0f;
            for (int i = 0; i < workRelevantStats.Count; i++)
                optimality += GetStatValueFromEnumerable(tool.WorkStatFactors, workRelevantStats[i], 0f);

            if (tool.def.useHitPoints)
            {
                float hpFrac = tool.MaxHitPoints > 0 ? (float)tool.HitPoints / tool.MaxHitPoints : 0f;
                float lifespanDays = tool.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan);
                float lifespanRemaining = lifespanDays * hpFrac;
                optimality *= LifespanDaysToOptimalityMultiplierCurve.Evaluate(lifespanRemaining);
            }
            return optimality;
        }

        private static float GetStatValueFromEnumerable(IEnumerable<StatModifier> mods, StatDef stat, float @default)
        {
            if (mods != null)
                foreach (var m in mods)
                    if (m != null && m.stat == stat)
                        return m.value;
            return @default;
        }

        private static readonly SimpleCurve LifespanDaysToOptimalityMultiplierCurve = new SimpleCurve
        {
            new CurvePoint(0f,   0.04f),
            new CurvePoint(0.5f, 0.20f),
            new CurvePoint(1f,   0.50f),
            new CurvePoint(2f,   1.00f),
            new CurvePoint(4f,   1.00f),
            new CurvePoint(999f, 1.00f)
        };

        // Drop the tool near a preferred location (e.g., the new tool), and enqueue a HaulToCell into a nearby stockpile if possible.
        private static Job DequipAndTryStoreSurvivalToolNear(Pawn pawn, Thing tool, IntVec3 preferredNear, bool enqueueCurrent = true)
        {
            if (pawn.CurJob != null && enqueueCurrent)
                pawn.jobs.jobQueue.EnqueueFirst(DuplicateJob(pawn.CurJob));

            var map = pawn.MapHeld;
            var zoneManager = map?.zoneManager;
            Zone_Stockpile nearStock = zoneManager?.ZoneAt(preferredNear) as Zone_Stockpile;

            // Try to keep it in/near the target stockpile if it accepts the tool
            bool enqueuedHaul = false;

            if (nearStock != null && nearStock.settings?.filter?.Allows(tool) == true)
            {
                // Find the closest empty, valid cell within that stockpile
                var cells = nearStock.slotGroup?.CellsList ?? new List<IntVec3>();
                IntVec3 bestCell = IntVec3.Invalid;
                float bestDist = float.MaxValue;

                for (int i = 0; i < cells.Count; i++)
                {
                    var c = cells[i];
                    if (!c.Walkable(map)) continue;
                    if (!pawn.CanReserve(c)) continue;

                    // Can we haul it there? (avoid blocking things)
                    if (StoreUtility.IsGoodStoreCell(c, map, tool, pawn, tool.Faction))
                    {
                        float d = c.DistanceToSquared(preferredNear);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestCell = c;
                        }
                    }
                }

                if (bestCell.IsValid)
                {
                    var haulJob = new Job(JobDefOf.HaulToCell, tool, bestCell) { count = 1 };
                    pawn.jobs.jobQueue.EnqueueFirst(DuplicateJob(haulJob));
                    enqueuedHaul = true;
                }
            }

            // If we couldn't find a “near” cell, fall back to global best storage
            if (!enqueuedHaul)
            {
                if ((zoneManager?.ZoneAt(pawn.PositionHeld) as Zone_Stockpile)?.settings?.filter?.Allows(tool) != true
                    && StoreUtility.TryFindBestBetterStoreCellFor(tool, pawn, map, StoreUtility.CurrentStoragePriorityOf(tool), pawn.Faction, out IntVec3 c2))
                {
                    var haulJob = new Job(JobDefOf.HaulToCell, tool, c2) { count = 1 };
                    pawn.jobs.jobQueue.EnqueueFirst(DuplicateJob(haulJob));
                }
            }

            // Do the actual dequip NOW (so the following Haul can pick it up)
            return new Job(ST_JobDefOf.DropSurvivalTool, tool);
        }


        // Does the pawn already hold/equip ANY tool that helps with these stats?
        private static bool PawnHasHelpfulTool(Pawn pawn, List<StatDef> requiredStats)
        {
            if (pawn == null || requiredStats == null || requiredStats.Count == 0)
                return false;

            // Equipment first
            var eqTracker = pawn.equipment;
            if (eqTracker != null)
            {
                var eq = eqTracker.AllEquipmentListForReading;
                for (int i = 0; i < eq.Count; i++)
                {
                    if (eq[i] is SurvivalTool st && ToolImprovesAnyRequiredStat(st, requiredStats))
                        return true;
                }
            }

            // Inventory (only tools under the carry limit)
            foreach (var t in pawn.GetUsableHeldSurvivalTools())
            {
                if (t is SurvivalTool st && ToolImprovesAnyRequiredStat(st, requiredStats))
                    return true;
            }

            return false;
        }
        // ---------- Storage/Home policy ----------
        private static bool ToolIsAcquirableByPolicy(Pawn pawn, SurvivalTool tool)
        {
            if (tool.IsInAnyStorage())
                return true;

            bool storageOnly = SurvivalTools.Settings != null && SurvivalTools.Settings.pickupFromStorageOnly;
            if (storageOnly) return false;

            var map = pawn.Map;
            if (map == null || map.areaManager == null) return false;
            var home = map.areaManager.Home;
            return home != null && home[tool.Position];
        }
    }
}
