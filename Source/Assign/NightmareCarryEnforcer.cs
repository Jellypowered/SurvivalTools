// RimWorld 1.6 / C# 7.3
// Source/Assign/NightmareCarryEnforcer.cs
// Focused enforcement of Nightmare (extraHardcoreMode) carry invariant:
//  - At most `allowed` real tools (excludes virtual/tool-stuff/rags) before starting/continuing work.
//  - Provides allocation-free collection and prioritised keeper selection.

using System;
using Verse;
using RimWorld;
using Verse.AI; // Job, JobTag
using System.Collections.Generic;
using SurvivalTools.Helpers;
using SurvivalTools.Scoring;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Assign
{
    internal static class NightmareCarryEnforcer
    {
        private static readonly List<Thing> _real = new List<Thing>(8);
        private static readonly List<Thing> _keepers = new List<Thing>(4);
        private static readonly Dictionary<Thing, float> _scoreCache = new Dictionary<Thing, float>(8);
        private static readonly Dictionary<int, int> _logCooldownUntil = new Dictionary<int, int>(64); // pawnId->tick

        // Unified classification predicate (matches scoring path): counts only tools that improve ANY registered work stat.
        // Excludes: virtual wrappers, bound consumable rag units, non-tool defs that don't affect resolver stats.
        internal static bool IsCarryLimitedTool(Thing t)
        {
            if (t == null) return false;
            if (t is VirtualTool) return false;
            if (ST_BoundConsumables.IsBoundUnit(t)) return false; // single textile rag unit, not a real tool
            var def = t.def; if (def == null) return false;
            // Only treat SurvivalTool or defs flagged as survival tool; anything failing that gate is ignored early
            if (!(t is SurvivalTool) && def.IsSurvivalTool() != true) return false;
            // Resolver-based capability check (consistent with scoring)
            if (!ToolStatResolver.AffectsAnyRegisteredWorkStat(def)) return false;
            return true;
        }

        internal static bool IsCompliant(Pawn pawn, Thing keeperOrNull, int allowed)
        {
            // Strict: counts only currently carried real tools. Queued drops do not reduce count.
            if (pawn == null) return true;
            Collect(pawn, collectOnly: true);
            bool ok = _real.Count <= allowed;
            _real.Clear(); _scoreCache.Clear(); // release quickly (no keeper selection required)
            return ok;
        }

        internal static int CountCarried(Pawn pawn)
        {
            if (pawn == null) return 0;
            int c = 0;
            try
            {
                var inv = pawn.inventory?.innerContainer; if (inv != null)
                    for (int i = 0; i < inv.Count; i++) if (IsCarryLimitedTool(inv[i])) c++;
                var eq = pawn.equipment?.AllEquipmentListForReading; if (eq != null)
                    for (int i = 0; i < eq.Count; i++) if (IsCarryLimitedTool(eq[i])) c++;
            }
            catch { }
            return c;
        }

        internal static int EnforceNow(Pawn pawn, Thing keeperOrNull, int allowed, string reason = null)
        {
            if (pawn == null || allowed < 0) return 0;
            Collect(pawn, collectOnly: false);
            if (_real.Count <= allowed) { Cleanup(); return 0; }

            SelectKeepers(pawn, keeperOrNull, allowed);
            int enq = 0;
            for (int i = 0; i < _real.Count; i++)
            {
                var tool = _real[i];
                if (tool == null) continue;
                if (keeperOrNull != null && tool == keeperOrNull) continue;
                if (IsKeeper(tool)) continue;
                if (AlreadyQueuedToDrop(pawn, tool)) continue;
                if (EnqueueDropFirst(pawn, tool)) enq++;
            }

            if (SurvivalToolsMod.Settings?.extraHardcoreMode == true)
            {
                TryLog(pawn, keeperOrNull, enq, reason);
            }
            Cleanup();
            return enq;
        }

        private static void TryLog(Pawn pawn, Thing keeper, int enq, string reason)
        {
            try
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                int pid = pawn.thingIDNumber;
                if (!_logCooldownUntil.TryGetValue(pid, out var until) || now >= until)
                {
                    _logCooldownUntil[pid] = now + 300;
                    var dropsSb = new System.Text.StringBuilder();
                    for (int i = 0, listed = 0; i < _real.Count && listed < 6; i++)
                    {
                        var t = _real[i];
                        if (t == null || IsKeeper(t) || (keeper != null && t == keeper)) continue;
                        if (listed++ > 0) dropsSb.Append(", ");
                        dropsSb.Append(t.LabelShort);
                    }
                    ST_Logging.LogInfo($"[NightmareCarry] pawn={pawn.LabelShort} keep={(keeper != null ? keeper.LabelShort : "(auto)")} drop={enq} ({dropsSb}) reason={(reason ?? "enforce")}");
                }
            }
            catch { }
        }

        private static void SelectKeepers(Pawn pawn, Thing keeperOrNull, int allowed)
        {
            _keepers.Clear();
            if (allowed <= 0) return; // keep none
            if (keeperOrNull != null && Contains(_real, keeperOrNull))
            {
                _keepers.Add(keeperOrNull);
            }
            // Score remaining and pick highest deterministic
            // Build local array of (thing, score, equipped) and do selection without allocations
            for (int i = 0; i < _real.Count; i++)
            {
                var t = _real[i]; if (t == null || t == keeperOrNull) continue;
                float sc = ScoreOf(pawn, t);
                // Insert into keepers if space or better than worst
                if (_keepers.Count < allowed)
                {
                    _keepers.Add(t);
                    continue;
                }
                // If keeper list already full and keeperOrNull consumed slot ensure total size==allowed
                if (_keepers.Count > allowed) TrimKeepers(allowed, pawn);
                if (_keepers.Count < allowed) { _keepers.Add(t); continue; }
                // compare with worst
                int worstIdx = FindWorstKeeperIndex(pawn);
                var worst = _keepers[worstIdx];
                if (BetterThan(pawn, t, worst)) _keepers[worstIdx] = t;
            }
            if (_keepers.Count > allowed) TrimKeepers(allowed, pawn);
        }

        private static bool BetterThan(Pawn pawn, Thing a, Thing b)
        {
            float sa = ScoreOf(pawn, a);
            float sb = ScoreOf(pawn, b);
            if (Math.Abs(sa - sb) > 0.0001f) return sa > sb;
            bool ea = IsEquipped(pawn, a); bool eb = IsEquipped(pawn, b);
            if (ea != eb) return ea; // equipped preferred
            return a.thingIDNumber < b.thingIDNumber; // lower id deterministic
        }

        private static int FindWorstKeeperIndex(Pawn pawn)
        {
            int idx = 0; Thing worst = _keepers[0];
            for (int i = 1; i < _keepers.Count; i++)
            {
                var t = _keepers[i];
                if (BetterThan(pawn, worst, t)) { idx = i; worst = t; }
            }
            return idx;
        }

        private static void TrimKeepers(int allowed, Pawn pawn)
        {
            while (_keepers.Count > allowed)
            {
                int worst = FindWorstKeeperIndex(pawn);
                _keepers.RemoveAt(worst);
            }
        }

        private static float ScoreOf(Pawn pawn, Thing t)
        {
            if (!_scoreCache.TryGetValue(t, out var val))
            {
                val = 0f;
                val += ToolScoring.Score(t, pawn, ST_StatDefOf.DiggingSpeed);
                val += ToolScoring.Score(t, pawn, StatDefOf.ConstructionSpeed);
                val += ToolScoring.Score(t, pawn, ST_StatDefOf.TreeFellingSpeed);
                val += ToolScoring.Score(t, pawn, ST_StatDefOf.PlantHarvestingSpeed);
                _scoreCache[t] = val;
            }
            return val;
        }

        private static bool IsKeeper(Thing t)
        {
            for (int i = 0; i < _keepers.Count; i++) if (_keepers[i] == t) return true; return false;
        }

        internal static Thing SelectKeeperForJob(Pawn pawn, StatDef focusStat)
        {
            if (pawn == null || focusStat == null) return null;
            try { return ToolScoring.GetBestTool(pawn, focusStat, out _); } catch { return null; }
        }

        private static void Collect(Pawn pawn, bool collectOnly)
        {
            _real.Clear(); _scoreCache.Clear();
            var inv = pawn.inventory?.innerContainer; if (inv != null)
            {
                for (int i = 0; i < inv.Count; i++) { var t = inv[i]; if (IsCarryLimitedTool(t)) _real.Add(t); }
            }
            var eq = pawn.equipment?.AllEquipmentListForReading; if (eq != null)
            {
                for (int i = 0; i < eq.Count; i++) { var t = eq[i]; if (IsCarryLimitedTool(t)) _real.Add(t); }
            }
        }

        private static bool IsEquipped(Pawn pawn, Thing t)
        {
            var eq = pawn.equipment?.AllEquipmentListForReading; if (eq == null) return false; for (int i = 0; i < eq.Count; i++) if (eq[i] == t) return true; return false;
        }

        private static bool Contains(List<Thing> list, Thing t)
        { for (int i = 0; i < list.Count; i++) if (list[i] == t) return true; return false; }

        private static bool AlreadyQueuedToDrop(Pawn p, Thing t)
        {
            try
            {
                var jt = p.jobs; if (jt == null) return false;
                var j = jt.curJob; if (j != null && IsDropJobFor(j, t)) return true;
                var q = jt.jobQueue; if (q != null)
                {
                    for (int i = 0; i < q.Count; i++)
                    {
                        var item = q[i]; var qj = item?.job; if (qj != null && IsDropJobFor(qj, t)) return true;
                    }
                }
            }
            catch { }
            return false;
            bool IsDropJobFor(Job job, Thing target)
            {
                if (job == null || target == null) return false;
                try
                {
                    var def = job.def;
                    Thing targ = job.GetTarget(TargetIndex.A).Thing;
                    if (targ != target) return false;
                    if (def == JobDefOf.DropEquipment || def == ST_JobDefOf.DropSurvivalTool) return true;
                    // permissive fallback: name or driver contains "Drop"
                    if ((def?.defName?.IndexOf("Drop", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0) return true;
                }
                catch { }
                return false;
            }
        }

        private static bool EnqueueDropFirst(Pawn pawn, Thing tool)
        {
            try
            {
                bool eq = IsEquipped(pawn, tool);
                Job job = eq ? JobMaker.MakeJob(JobDefOf.DropEquipment, tool) : JobMaker.MakeJob(ST_JobDefOf.DropSurvivalTool, tool);
                if (!eq) job.count = 1;
                pawn.jobs?.jobQueue?.EnqueueFirst(job, JobTag.Misc);
                return true;
            }
            catch { return false; }
        }

        private static void Cleanup()
        {
            _real.Clear(); _keepers.Clear(); _scoreCache.Clear();
        }
    }
}
