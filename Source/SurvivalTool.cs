// RimWorld 1.6 / C# 7.3
// Source/SurvivalTool.cs
//
// SurvivalTool
// - Caches work stat factors from: tool def (mod ext + statBases) and stuff def (mod ext)
// - Dedupe by stat (keep max value) for stable scoring
// - Hardcoded English inspect labels (intentional)
// - Null-safe and logging-friendly

using System;
using System.Collections.Generic;
using System.Linq;           // Contains / Any / ToList
using System.Text;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools
{
    public class SurvivalTool : ThingWithComps
    {
        #region Fields & Caches

        /// <summary>Persistent usage counter (used by degradation logic and other systems).</summary>
        public int workTicksDone = 0;

        /// <summary>Cached work stat factors sourced from def/stuff; rebuilt on spawn/load when needed.</summary>
        private List<StatModifier> _workStatFactors;

        /// <summary>Exposes cached factors (with lazy initialization when cache is not built).</summary>
        public IEnumerable<StatModifier> WorkStatFactors
        {
            get
            {
                if (_workStatFactors == null)
                    InitializeWorkStatFactors();
                return _workStatFactors ?? Enumerable.Empty<StatModifier>();
            }
        }

        #endregion

        #region Lifecycle

        public override void PostMake()
        {
            base.PostMake();
            InitializeWorkStatFactors();

            if (IsDebugLoggingEnabled)
            {
                try
                {
                    var list = WorkStatFactors.Select(m => $"{m.stat?.defName ?? "null"}={m.value:G}").ToArray();
                    LogDebug($"[SurvivalTools] {LabelCap} factors: {string.Join(", ", list)}", $"SurvivalTool_Factors_{this.GetHashCode()}");
                }
                catch
                {
                    // swallow: debug-only
                }
            }
        }

        // Prefer SpawnSetup(Map,bool) for 1.6 over PostSpawnSetup(bool)
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (_workStatFactors == null)
                InitializeWorkStatFactors();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workTicksDone, "workTicksDone", 0);

            // Rebuild cache after load to avoid stale references
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                _workStatFactors = null;
        }

        #endregion

        #region Factor Build

        /// <summary>
        /// Force recalculation of work stat factors. Useful when mod settings change
        /// or when existing tools need to be updated after mod updates.
        /// </summary>
        public void RefreshWorkStatFactors()
        {
            _workStatFactors = null;
            // Next access to WorkStatFactors will trigger InitializeWorkStatFactors
        }

        private void InitializeWorkStatFactors()
        {
            // Use the centralized ToolFactorCache for fast, safe caching. The cache implements
            // delayed activation so we avoid using it too early during PostLoad init which
            // previously led to CTDs. Before activation the cache computes results on-the-fly
            // without storing them. Once initialized, results are cached and re-used.
            try
            {
                // Request precomputed factors from the central cache. Pass this SurvivalTool
                // so the cache can apply any instance-specific multipliers if necessary.
                var computed = SurvivalToolUtility.ToolFactorCache.GetOrComputeToolFactors(def, Stuff, this);
                _workStatFactors = computed ?? new List<StatModifier>();
            }
            catch (Exception ex)
            {
                // Never throw during initialization; keep an empty list on failure.
                _workStatFactors = new List<StatModifier>();
                if (IsDebugLoggingEnabled)
                {
                    try { LogDebug($"[SurvivalTools] InitializeWorkStatFactors failed for {def?.defName ?? "null"}: {ex}", $"InitFactorsFail_{def?.defName ?? "null"}"); } catch { }
                }
            }
        }

        #endregion

        #region Ownership & Use

        public Pawn HoldingPawn
        {
            get
            {
                // Special-case: if this is a VirtualTool, its physical SourceThing
                // may be in a pawn's inventory/equipment. Prefer that holder when present.
                var vtool = this as VirtualTool;
                if (vtool != null && vtool.SourceThing != null)
                {
                    if (vtool.SourceThing.ParentHolder is Pawn_EquipmentTracker eq2 && eq2?.pawn != null)
                        return eq2.pawn;
                    if (vtool.SourceThing.ParentHolder is Pawn_InventoryTracker inv2 && inv2?.pawn != null)
                        return inv2.pawn;
                }

                if (ParentHolder is Pawn_EquipmentTracker eq && eq?.pawn != null)
                    return eq.pawn;
                if (ParentHolder is Pawn_InventoryTracker inv && inv?.pawn != null)
                    return inv.pawn;
                return null;
            }
        }

        public bool InUse
        {
            get
            {
                var holder = HoldingPawn;
                if (holder == null) return false;

                var curJob = holder.jobs?.curJob;
                if (curJob == null) return false;

                // For virtual wrappers, the job will target the physical backing thing.
                // Use a canonical comparison thing: the source physical thing for virtuals,
                // otherwise the tool object itself.
                Thing comparisonThing = (this is VirtualTool v && v.SourceThing != null) ? (Thing)v.SourceThing : (Thing)this;

                if (curJob.targetA.Thing == comparisonThing) return true;
                if (curJob.targetB.Thing == comparisonThing) return true;
                if (curJob.targetC.Thing == comparisonThing) return true;
                return false;
            }
        }

        #endregion

        #region Wear / Degradation

        /// <summary>
        /// How many work-ticks of usage correspond to one HP of wear.
        /// (estimated lifespan in days * ticks/day) / max HP
        /// </summary>
        public int WorkTicksToDegrade
        {
            get
            {
                float lifespanDays = this.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan);
                int hp = Math.Max(1, MaxHitPoints);
                double ticksPerHp = (lifespanDays * GenDate.TicksPerDay) / (double)hp;
                return (int)Math.Floor(ticksPerHp);
            }
        }

        #endregion

        #region Inspect String (hardcoded English labels)

        public override string GetInspectString()
        {
            var sb = new StringBuilder();

            // Held by
            var holder = HoldingPawn;
            if (holder != null)
                sb.AppendLine($"Held by {holder.LabelShort}");

            // In use
            if (InUse)
                sb.AppendLine("In use");

            // Condition / lifespan
            if (def?.useHitPoints == true)
            {
                float hpFrac = MaxHitPoints > 0 ? (float)HitPoints / MaxHitPoints : 0f;
                float lifespanDays = this.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan);

                sb.AppendLine($"Condition: {HitPoints}/{MaxHitPoints} ({(hpFrac * 100f):F0}%)");
                sb.AppendLine($"Estimated lifespan: {lifespanDays:F1}d");
            }

            // Append base inspect string (if any)
            var baseStr = base.GetInspectString();
            if (!baseStr.NullOrEmpty())
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(baseStr);
            }

            return SanitizeInspectString(sb.ToString());
        }

        private static string SanitizeInspectString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            s = s.Replace("\r\n", "\n").Replace('\r', '\n');

            var lines = s
                .Split('\n')
                .Select(line => line?.Trim())
                .Where(line => !string.IsNullOrEmpty(line));

            return string.Join("\n", lines).TrimEnd();
        }

        #endregion
    }
}
