// RimWorld 1.6 / C# 7.3
// Source/UI/RightClickRescue/Provider_STPrioritizeWithRescue.cs
// Public float menu option provider that injects Survival Tools rescue ("will fetch tool") prioritized options.
// Acts before fallback Harmony patch; scanners/builder contain the gating logic.

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using System;
using System.Linq;
using SurvivalTools.Helpers;
using SurvivalTools.Compat;
using SurvivalTools.Compatibility.TreeStack;
using System.Diagnostics;
using System.Text;

namespace SurvivalTools.UI.RightClickRescue
{
    // IMPORTANT: public + non-abstract so RimWorld can construct via reflection.
    public sealed class Provider_STPrioritizeWithRescue : FloatMenuOptionProvider
    {
        // Offer in both drafted & undrafted states; allow single-select only (rescue builder ignores multiselect currently)
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        static Provider_STPrioritizeWithRescue() => ST_Logging.DevOnce("RightClick.ProviderType", "[ST.RightClick] Provider: " + typeof(Provider_STPrioritizeWithRescue).FullName);
        public Provider_STPrioritizeWithRescue() { }
        // Prewarm heavy reflection on first provider construction
        private static bool _prewarmed;
        private static void Prewarm()
        {
            if (_prewarmed) return; _prewarmed = true;
            try
            {
                // Resolve and cache all key WorkGivers once to avoid reflection on first click
                string[] workers = new[] {
                    "WorkGiver_Miner","WorkGiver_Mine","WorkGiver_Deconstruct","WorkGiver_ConstructDeconstruct",
                    "WorkGiver_Uninstall","WorkGiver_ConstructSmoothWall","WorkGiver_ConstructSmoothFloor","WorkGiver_ConstructFinishFrames",
                    "WorkGiver_PlantsCut","WorkGiver_CutPlant","WorkGiver_PlantsHarvest","WorkGiver_Harvest",
                    "WorkGiver_PlantsSow","WorkGiver_Sow","WorkGiver_GrowerSow","WorkGiver_Grower_Sow","WorkGiver_Grower",
                    "WorkGiver_Repair","WorkGiver_Clean","WorkGiver_Research","WorkGiver_DoResearch"
                };
                foreach (var w in workers) WGResolve.ByWorkerTypes(w);
                // Touch stat resolution for these so StatGatingHelper internal caches populate (if any)
                foreach (var def in DefDatabase<WorkGiverDef>.AllDefsListForReading)
                {
                    if (def == null) continue;
                    try { StatGatingHelper.GetStatsForWorkGiver(def); } catch { }
                }
                // Explicit sow fast-path prewarm to avoid first-click latency
                try { RightClickRescue.RightClickRescueBuilder.PrewarmSow(); } catch { }
                // Research scanner prewarm
                try { RightClickRescue.RightClickRescueBuilder.PrewarmResearch(); } catch { }
            }
            catch { }
        }

        // ---- Per-click aggregation ----
        private struct ClickKey
        {
            public int tick;
            public int pawnId;
            public IntVec3 cell;
            public override int GetHashCode() => (tick * 397) ^ pawnId ^ cell.GetHashCode();
            public override bool Equals(object obj)
            {
                if (obj is ClickKey other)
                    return tick == other.tick && pawnId == other.pawnId && cell == other.cell;
                return false;
            }
        }
        private static ClickKey _current;
        private static int _optionsAddedThisClick;
        private static readonly HashSet<Type> _scannedWorkersThisClick = new HashSet<Type>();
        private static readonly Dictionary<int, int> _lastLogTickByCode = new Dictionary<int, int>();
        private static readonly Dictionary<Type, bool> _eligibilityCacheThisClick = new Dictionary<Type, bool>();
        private static readonly Dictionary<Type, StatDef[]> _reqStatsCacheThisClick = new Dictionary<Type, StatDef[]>();
        private const int NO_OPTION_LOG_COOLDOWN_TICKS = 60; // ~1s
        // Cache per-click WG -> required stats (array) for scoring (separate from worker type cache)
        private static readonly Dictionary<WorkGiverDef, StatDef[]> _wgReqStatsCacheThisClick = new Dictionary<WorkGiverDef, StatDef[]>();

        // Lightweight scoring for ordering only (never excludes). Higher score = earlier.
        internal static int ScoreWGForClick(WorkGiverDef wg, Thing primaryThing, IntVec3 cell, Map map)
        {
            if (wg == null) return 0;
            int score = 0;
            try
            {
                StatDef[] stats;
                if (!_wgReqStatsCacheThisClick.TryGetValue(wg, out stats) || stats == null)
                {
                    stats = CompatAPI.GetRequiredStatsFor(wg) ?? Array.Empty<StatDef>();
                    _wgReqStatsCacheThisClick[wg] = stats;
                }
                bool requiresTree = stats.Any(s => s != null && (s == ST_StatDefOf.TreeFellingSpeed || ToolStatResolver.IsAliasOf(s, ST_StatDefOf.TreeFellingSpeed)));
                bool requiresPlantHarvest = stats.Any(s => s == ST_StatDefOf.PlantHarvestingSpeed);
                bool requiresConstruct = stats.Any(s => s == StatDefOf.ConstructionSpeed);
                bool requiresMine = stats.Any(s => s == ST_StatDefOf.DiggingSpeed);
                bool requiresResearch = stats.Any(s => s == ST_StatDefOf.ResearchSpeed || s == DefDatabase<StatDef>.GetNamedSilentFail("ResearchSpeed"));

                // Primary thing hints
                if (primaryThing != null)
                {
                    if (primaryThing is Plant plant)
                    {
                        if (plant.def?.plant?.IsTree == true)
                            score += requiresTree ? 30 : 5;
                        else
                            score += requiresPlantHarvest ? 25 : 3;
                    }
                    if (primaryThing.def?.mineable == true)
                        score += requiresMine ? 30 : 5;
                    // Research bench hint (use same type resolution as ResearchScanner to avoid comp dependency)
                    try
                    {
                        var benchType = HarmonyLib.AccessTools.TypeByName("RimWorld.Building_ResearchBench") ?? HarmonyLib.AccessTools.TypeByName("Building_ResearchBench");
                        if (benchType != null && benchType.IsAssignableFrom(primaryThing.GetType()))
                            score += requiresResearch ? 30 : 5;
                    }
                    catch { }
                }

                // Designation hints
                if (map != null && cell.IsValid)
                {
                    var desigs = map.designationManager?.AllDesignationsAt(cell);
                    if (desigs != null)
                    {
                        bool hasSmooth = desigs.Any(d => d != null && (d.def == DesignationDefOf.SmoothWall || d.def == DesignationDefOf.SmoothFloor));
                        bool hasMine = desigs.Any(d => d != null && d.def == DesignationDefOf.Mine);
                        bool hasCutHarvest = desigs.Any(d => d != null && (d.def == DesignationDefOf.CutPlant || d.def == DesignationDefOf.HarvestPlant));
                        if (hasSmooth) score += requiresConstruct ? 30 : 5;
                        if (hasMine) score += requiresMine ? 25 : 3;
                        if (hasCutHarvest) score += (requiresTree || requiresPlantHarvest) ? 25 : 3;
                    }
                }

                // WorkType nudges
                try
                {
                    if (wg.workType == WorkTypeDefOf.Research && requiresResearch) score += 10;
                    if (wg.workType == WorkTypeDefOf.Construction && requiresConstruct) score += 10;
                    if (wg.workType == WorkTypeDefOf.Mining && requiresMine) score += 10;
                    if (wg.workType == WorkTypeDefOf.PlantCutting && (requiresTree || requiresPlantHarvest)) score += 10;
                }
                catch { }
            }
            catch { }
            return score;
        }

        public static void BeginClick(Pawn pawn, IntVec3 cell)
        {
            try
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                var key = new ClickKey { tick = now, pawnId = pawn?.thingIDNumber ?? -1, cell = cell };
                if (!key.Equals(_current))
                {
                    _current = key;
                    _optionsAddedThisClick = 0;
                    _scannedWorkersThisClick.Clear();
                    _eligibilityCacheThisClick.Clear();
                    _reqStatsCacheThisClick.Clear();
                    _wgReqStatsCacheThisClick.Clear();
                    RightClickRescue.RightClickRescueBuilder.ResetClickStatLog();
                    // Per-click instrumentation reset (B) for clearer per-click timing snapshots
                    try
                    {
                        var s = SurvivalToolsMod.Settings;
                        if (s != null && Prefs.DevMode && s.debugLogging)
                            RightClickRescue.RightClickRescueBuilder.ResetInstrumentation();
                    }
                    catch { }
                    // Reset per-click rejection logging scope for eligibility provider
                    try { ST_RightClickRescueProvider.BeginClickScope(now); } catch { }
                }
            }
            catch { }
        }
        public static void EndClick()
        {
            try
            {
                // Legacy "No rescue options" spam removed. Summary + detail logs (when enabled) now convey empty results once per signature.
                RightClickRescue.RightClickRescueBuilder.ResetClickStatLog();
            }
            catch { }
        }

        // Helpers used by builder/fallback to count successful adds
        internal static void NotifyOptionAdded() { _optionsAddedThisClick++; }
        internal static bool AlreadySatisfiedThisClick() => _optionsAddedThisClick > 0;
        internal static bool CachedEligibility(Type t, Func<Type, bool> compute)
        {
            if (t == null) return false;
            if (_eligibilityCacheThisClick.TryGetValue(t, out var v)) return v;
            v = compute(t);
            _eligibilityCacheThisClick[t] = v;
            return v;
        }
        internal static StatDef[] CachedRequiredStats(Type t, Func<Type, StatDef[]> compute)
        {
            if (t == null) return Array.Empty<StatDef>();
            if (_reqStatsCacheThisClick.TryGetValue(t, out var arr)) return arr;
            arr = compute(t) ?? Array.Empty<StatDef>();
            _reqStatsCacheThisClick[t] = arr;
            return arr;
        }

        public override bool Applies(FloatMenuContext context)
        {
            if (context == null) return false;
            var s = SurvivalToolsMod.Settings;
            if (s == null) return false;
            // Only participate when feature enabled and in Hardcore / Nightmare (extraHardcore implies hardcore semantics)
            if (!s.enableRightClickRescue) return false;
            if (!(s.hardcoreMode || s.extraHardcoreMode)) return false;
            return context.FirstSelectedPawn != null;
        }

        public override bool SelectedPawnValid(Pawn p, FloatMenuContext context)
        {
            return p != null && p.Spawned && p.Faction == Faction.OfPlayer && p.CanTakeOrder;
        }

        public override IEnumerable<FloatMenuOption> GetOptions(FloatMenuContext context)
        {
            var list = new List<FloatMenuOption>();
            try
            {
                var pawn = context?.FirstSelectedPawn;
                if (pawn != null)
                    BeginClick(pawn, context.ClickedCell);
                Prewarm();
                if (Provider_STPrioritizeWithRescue.AlreadySatisfiedThisClick()) return list; // short-circuit
                if (pawn != null)
                {
                    var settings = SurvivalToolsMod.Settings;
                    Stopwatch sw = null; long startTicks = 0;
                    bool timing = Prefs.DevMode && settings != null && settings.debugLogging;
                    if (timing)
                    {
                        try { sw = Stopwatch.StartNew(); startTicks = GenTicks.TicksAbs; } catch { timing = false; }
                    }
                    bool stcExternal = TreeSystemArbiter.Authority == TreeAuthority.SeparateTreeChopping;
                    if (stcExternal)
                    {
                        // No need to scan tree-felling options; builder already suppresses but we still allow non-tree rescues.
                    }
                    RightClickRescue.RightClickRescueBuilder.TryAddRescueOptions(pawn, context, list);
                    if (list.Count > 0) NotifyOptionAdded();
                    if (timing && sw != null)
                    {
                        sw.Stop();
                        try
                        {
                            LogSummaryWithDetail(pawn, context, list, sw.ElapsedMilliseconds);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return list;
        }

        // ---- Logging refinement ----
        private const int DETAIL_COOLDOWN_TICKS = 180; // 3 seconds @60 tps
        private static readonly Dictionary<int, int> _detailCooldown = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> _summaryCooldown = new Dictionary<int, int>();
        private static readonly StringBuilder _sb = new StringBuilder(512);
        private const bool SOW_SNAPSHOT_ENABLE = true; // flip false to silence SowSnapshot diagnostics entirely
        private static void LogSummaryWithDetail(Pawn pawn, FloatMenuContext context, List<FloatMenuOption> list, long elapsedMs)
        {
            var counters = RightClickRescue.RightClickRescueBuilder.Counters;
            int considered = RightClickRescue.RightClickRescueBuilder.LastConsideredCount;
            int now = Find.TickManager?.TicksGame ?? 0;
            // Build summary signature. Treat <=1ms as effectively zero to collapse ultra-fast spam.
            long effMs = elapsedMs <= 1 ? 0 : elapsedMs;
            int sig = pawn.thingIDNumber;
            unchecked
            {
                sig = (sig * 397) ^ context.ClickedCell.GetHashCode();
                sig = (sig * 397) ^ considered;
                sig = (sig * 397) ^ list.Count;
                if (effMs > 0) // only include gating counters in signature when there was measurable latency
                {
                    sig = (sig * 397) ^ counters.gatingNeeded;
                    sig = (sig * 397) ^ counters.gatingNotNeeded;
                }
            }
            const int SUMMARY_CD_TICKS = 60; // 1s window for duplicate zero-ms summaries
            bool suppress = false;
            if (effMs == 0)
            {
                if (_summaryCooldown.TryGetValue(sig, out var last) && now - last < SUMMARY_CD_TICKS)
                    suppress = true;
                else
                    _summaryCooldown[sig] = now;
            }
            else
            {
                _summaryCooldown[sig] = now; // always allow non-zero ms (first expensive pass) and reset cooldown
            }
            if (!suppress)
            {
#if DEBUG
                if (effMs == 0)
                {
                    // Ultra-short form for effectively free clicks; omit ms and gating counters
                    Log.Message($"[RightClick] Summary | pawn={pawn.LabelShort} | cell={context.ClickedCell} | considered={considered} | added={list.Count}");
                }
                else
                {
                    Log.Message($"[RightClick] Summary | pawn={pawn.LabelShort} | cell={context.ClickedCell} | considered={considered} | added={list.Count} | ms={elapsedMs} | gateYes={counters.gatingNeeded} | gateNo={counters.gatingNotNeeded}");
                }
#endif
            }

            // Only produce detail line(s) when there's actual information (not all zeros, or slow, or considered but failed)
            bool hasUsefulInfo = counters.canHandleFalse > 0 || counters.describeFailed > 0 || counters.treeSuppressed > 0 ||
                                 counters.workTypeDisabled > 0 || counters.described > 0 || counters.gatingNeeded > 0;
            bool needDetail =
                (considered > 0 && list.Count == 0 && hasUsefulInfo) ||   // scanners ran but nothing added - useful to see why
                (elapsedMs >= 80 && counters.gatingNeeded > 0); // only slow if actual gating evaluated
            if (!needDetail) return;
            // 'now' already computed above
            // Build signature of reasons to gate detail spam
            var scans = RightClickRescue.RightClickRescueBuilder.ScannerRecords;
            int sigHash = considered ^ list.Count ^ counters.gatingNeeded ^ counters.gatingNotNeeded;
            unchecked
            {
                for (int i = 0; i < scans.Count; i++)
                {
                    sigHash = (sigHash * 397) ^ (scans[i].Reason?.GetHashCode() ?? 0);
                }
            }
            if (_detailCooldown.TryGetValue(sigHash, out var until) && now - until < DETAIL_COOLDOWN_TICKS) return;
            _detailCooldown[sigHash] = now;

            // Aggregate reason counts
            int canHandleFalse = counters.canHandleFalse;
            int describeFailed = counters.describeFailed;
            int treeSuppressed = counters.treeSuppressed;
            int workTypeDisabled = counters.workTypeDisabled;
            int described = counters.described;

#if DEBUG
            _sb.Clear();
            _sb.Append("[RightClick] Detail | reasons=")
               .Append("CanHandleFalse:").Append(canHandleFalse).Append(',')
               .Append("DescribeFailed:").Append(describeFailed).Append(',')
               .Append("TreeSuppressed:").Append(treeSuppressed).Append(',')
               .Append("WorkTypeDisabled:").Append(workTypeDisabled).Append(',')
               .Append("Described:").Append(described);
            Log.Message(_sb.ToString());

            if (scans.Count == 0) return;
            // Chunk scanner breakdown across multiple lines with length guard
            _sb.Clear();
            _sb.Append("[RightClick] Scanners | ");
            int lineLen = _sb.Length; int emitted = 0; const int MAX_LINE = 180; int linesOut = 0; const int MAX_LINES = 3;
            for (int i = 0; i < scans.Count && linesOut < MAX_LINES; i++)
            {
                var r = scans[i];
                string fragment = r.Name + '(' + r.Reason + (r.Ms > 0 ? (":" + r.Ms + "ms") : "") + ')';
                if (lineLen + fragment.Length + 2 > MAX_LINE)
                {
                    Log.Message(_sb.ToString());
                    linesOut++;
                    if (linesOut >= MAX_LINES) break;
                    _sb.Clear(); _sb.Append("[RightClick] Scanners | ");
                    lineLen = _sb.Length;
                }
                if (emitted > 0) { _sb.Append(','); lineLen++; }
                _sb.Append(fragment); lineLen += fragment.Length; emitted++;
            }
            if (_sb.Length > 0 && linesOut < MAX_LINES)
            {
                if (emitted < scans.Count) _sb.Append(",+").Append(scans.Count - emitted).Append("more");
                Log.Message(_sb.ToString());
            }
#endif

            // Sow-specific context snapshot (only when sow failed to describe) with cooldown.
            // Suppress noisy 'NoZone' failures (these are extremely common when mousing over non-growing cells).
            if (SOW_SNAPSHOT_ENABLE)
            {
                bool sowFailure = scans.Any(r => r.Name != null && r.Name.Contains("SowScanner") && (r.Reason ?? string.Empty).StartsWith("DescribeFailed"));
                if (sowFailure && list.Count == 0)
                {
                    var sowRec = scans.FirstOrDefault(r => r.Name != null && r.Name.Contains("SowScanner"));
                    if (sowRec.Reason == null || !sowRec.Reason.Contains("NoZone"))
                    {
                        LogSowSnapshot(pawn, context, scans);
                    }
                }
            }
        }

        private static readonly Dictionary<int, int> _sowSnapshotCooldown = new Dictionary<int, int>();
        private const int SOW_SNAPSHOT_CD_TICKS = 600; // 10s (match other throttling)
        private static void LogSowSnapshot(Pawn pawn, FloatMenuContext context, IReadOnlyList<RightClickRescue.RightClickRescueBuilder.ScannerRecord> scans)
        {
#if (DEBUG)
            {
                try
                {
                    var map = pawn?.Map; if (map == null) return; var cell = context.ClickedCell; if (!cell.IsValid) return;
                    int key = (cell.x * 397) ^ cell.z ^ (pawn?.thingIDNumber ?? -1);
                    int now = Find.TickManager?.TicksGame ?? 0;
                    if (_sowSnapshotCooldown.TryGetValue(key, out var until) && now < until) return;
                    _sowSnapshotCooldown[key] = now + SOW_SNAPSHOT_CD_TICKS;
                    var zone = cell.GetZone(map) as Zone_Growing;
                    var plantDef = zone?.GetPlantDefToGrow();
                    var existingPlant = cell.GetPlant(map);
                    var desigs = map.designationManager?.AllDesignationsAt(cell)?.ToList();
                    bool fertileOk = false; float fert = 0f; float fertMin = 0f;
                    if (plantDef != null)
                    {
                        fert = map.fertilityGrid.FertilityAt(cell);
                        fertMin = plantDef.plant?.fertilityMin ?? 0f;
                        fertileOk = fert >= fertMin;
                    }
                    // Work type enabled?
                    bool growDisabled = pawn?.WorkTypeIsDisabled(WorkTypeDefOf.Growing) ?? false;
                    var sowRec = scans.FirstOrDefault(r => r.Name != null && r.Name.Contains("SowScanner"));
                    string sowScanReason = sowRec.Reason;
                    string desigStr = string.Join(";", desigs?.Select(d => d?.def?.defName ?? "null") ?? Enumerable.Empty<string>());
                    Log.Message($"[RightClick] SowSnapshot | pawn={pawn?.LabelShort} | cell={cell} | reason={sowScanReason} | zone={(zone != null)} | plantDef={(plantDef?.defName ?? "null")} | existing={(existingPlant?.def?.defName ?? "null")} | fert={fert:0.##}/{fertMin:0.##} ok={fertileOk} | growDisabled={growDisabled} | desigs={desigStr}");
                }
                catch { }

            }
#else
#endif
        }

        public override bool TargetThingValid(Thing thing, FloatMenuContext context) => false; // context-wide only
        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing target, FloatMenuContext context) { yield break; }
        public override bool TargetPawnValid(Pawn target, FloatMenuContext context) => false;
        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn target, FloatMenuContext context) { yield break; }
    }
}
