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
                    Log.Message($"[SurvivalTools] {LabelCap} factors: {string.Join(", ", list)}");
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
            var factors = new List<StatModifier>();

            try
            {
                // 1) From tool def mod extension (e.g., Abacus has ResearchSpeed here)
                var defExt = def?.GetModExtension<SurvivalToolProperties>();
                if (defExt?.baseWorkStatFactors != null)
                {
                    factors.AddRange(defExt.baseWorkStatFactors
                        .Where(m => m?.stat != null && m.value != 0f));
                }

                // 2) From tool def statBases (legacy / harmless extras)
                if (def?.statBases != null)
                {
                    factors.AddRange(def.statBases
                        .Where(m => m?.stat != null && m.value != 0f));
                }

                // 3) From stuff’s mod extension (cloth, hyperweave, etc.)
                if (def?.MadeFromStuff == true && Stuff != null)
                {
                    var stuffExt = Stuff.GetModExtension<SurvivalToolProperties>();
                    if (stuffExt?.baseWorkStatFactors != null)
                    {
                        factors.AddRange(stuffExt.baseWorkStatFactors
                            .Where(m => m?.stat != null && m.value != 0f));
                    }

                    // Also check for StuffPropsTool (for DLC materials like Bioferrite/Obsidian)
                    var stuffPropsExt = Stuff.GetModExtension<StuffPropsTool>();
                    if (stuffPropsExt?.toolStatFactors != null)
                    {
                        LogDebug($"SurvivalTool.InitializeWorkStatFactors: Found StuffPropsTool on {Stuff?.defName} with {stuffPropsExt.toolStatFactors.Count} factors (applied as multipliers)");
                        // Note: StuffPropsTool factors are applied as multipliers in CalculateWorkStatFactors,
                        // not added as base stats here. This prevents tools from gaining unrelated stats.
                    }
                    else
                    {
                        LogDebug($"SurvivalTool.InitializeWorkStatFactors: No StuffPropsTool found on {Stuff?.defName}");
                    }
                }

                // Dedupe by stat; keep highest value for stability/readability
                _workStatFactors = factors
                    .GroupBy(m => m.stat)
                    .Select(g => new StatModifier { stat = g.Key, value = g.Max(x => x.value) })
                    .ToList();

                LogDebug($"SurvivalTool.InitializeWorkStatFactors: Final factors for {def?.defName} made from {Stuff?.defName}: {string.Join(", ", _workStatFactors.Select(f => $"{f.stat?.defName}={f.value:F2}"))}");
            }
            catch
            {
                // If something goes sideways, keep it empty rather than throwing
                _workStatFactors = new List<StatModifier>();
            }
        }

        #endregion

        #region Ownership & Use

        public Pawn HoldingPawn
        {
            get
            {
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

                // Safe checks across the common targets
                if (curJob.targetA.Thing == this) return true;
                if (curJob.targetB.Thing == this) return true;
                if (curJob.targetC.Thing == this) return true;
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
