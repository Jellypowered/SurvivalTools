// RimWorld 1.6 / C# 7.3
// Source/SurvivalToolUtility.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;
using SurvivalTools.Helpers;

namespace SurvivalTools
{
    public static class SurvivalToolUtility
    {
        #region Unified Stat & Tool Selection (centralized API)
        // These helpers unify previously duplicated logic spread across:
        //  - (Removed) ToolScoring legacy class
        //  - JobGiver_OptimizeSurvivalTools
        //  - AutoToolPickup_UtilityIntegrated
        //  - Alert classes & ad-hoc loops
        // All new code should call these methods; legacy code will be migrated to them.

        private const float ST_MultiStatBonusPerExtra = 0.10f; // 10% per extra matched stat
        private const float ST_DistancePenaltyPerTile = 0.01f; // score penalty per tile (map searches)
        private const int ST_RadialSearchRadius = 28;
        private const int ST_StorageSearchRadius = 80;
        private const int ST_FallbackSearchRadius = 200;

        public static float GetNoToolBaseline(StatDef stat)
        {
            if (stat == null) return 1f;
            try { return ToolFactorCache.GetOrComputeNoToolPenalty(stat); } catch { return 1f; }
        }

        public static float GetToolProvidedFactor(SurvivalTool tool, StatDef stat)
        {
            // If there is no tool instance or stat, return the baseline no-tool factor.
            // This prevents non-tools (or null wrappers) from appearing to improve stats.
            if (tool == null || stat == null) return GetNoToolBaseline(stat);
            try
            {
                foreach (var m in tool.WorkStatFactors)
                {
                    if (m?.stat == stat) return m.value;
                }
            }
            catch { }
            // IMPORTANT: Returning 1f here incorrectly implied an improvement over the baseline
            // no-tool penalty (e.g. 0.4 normal mode) even when the tool does NOT cover this stat.
            // That caused any unrelated tool to appear to "improve" every penalized stat, blocking
            // normal-mode proactive pickup (PawnHasHelpfulTool/ToolImprovesAny early-exited true).
            // We now return the baseline penalty so absent modifiers yield zero delta in
            // ScoreToolForStats and ToolImprovesAny.
            return GetNoToolBaseline(stat);
        }

        public static bool ToolImprovesAny(SurvivalTool tool, List<StatDef> stats)
        {
            if (tool == null || stats == null) return false;
            for (int i = 0; i < stats.Count; i++)
            {
                var s = stats[i];
                if (s == null) continue;
                if (GetToolProvidedFactor(tool, s) > GetNoToolBaseline(s) + 0.001f) return true;
            }
            return false;
        }

        [Obsolete("Internal Phase 3 migration - will be replaced by ToolScoring.Score() in Phase 4")]
        public static float ScoreToolForStats(SurvivalTool tool, Pawn pawn, List<StatDef> stats)
        {
            if (tool == null || pawn == null || stats == null || stats.Count == 0) return 0f;
            float total = 0f; int matched = 0;
            for (int i = 0; i < stats.Count; i++)
            {
                var s = stats[i]; if (s == null) continue;
                float f = GetToolProvidedFactor(tool, s); float baseline = GetNoToolBaseline(s);
                if (f > baseline + 0.001f) { total += (f - baseline); matched++; }
            }
            if (matched > 1) total *= 1f + (matched - 1) * ST_MultiStatBonusPerExtra;
            // Quality weighting if enabled
            try
            {
                if (SurvivalTools.Settings?.useQualityToolScaling ?? false)
                {
                    if (tool is ThingWithComps twc)
                    {
                        var comp = twc.TryGetComp<CompQuality>();
                        if (comp != null) total *= ToolQualityCurve.Evaluate((int)comp.Quality);
                    }
                }
            }
            catch { }
            // Condition weighting 50%..100%
            try
            {
                Thing backing = BackingThing(tool, pawn) ?? (tool as Thing);
                if (backing?.def.useHitPoints == true && backing.MaxHitPoints > 0)
                {
                    float hpPct = backing.HitPoints / (float)backing.MaxHitPoints;
                    total *= 0.5f + 0.5f * hpPct;
                }
            }
            catch { }
            return total;
        }

        public static SurvivalTool FindBestToolCandidate(Pawn pawn, List<StatDef> requiredStats, bool searchMap, out Thing backingThing)
        {
            backingThing = null;
            if (pawn == null || requiredStats == null || requiredStats.Count == 0) return null;
            SurvivalTool best = null; float bestScore = 0f;
            // Held/equipped first
            foreach (var thing in pawn.GetAllUsableSurvivalTools())
            {
                SurvivalTool cand = thing as SurvivalTool;
                if (cand == null && thing.def.IsToolStuff()) cand = VirtualTool.FromThing(thing);
                if (cand == null) continue;
                if (!ToolImprovesAny(cand, requiredStats)) continue;
#pragma warning disable CS0618 // Phase 3: Legacy method still used internally
                float sc = ScoreToolForStats(cand, pawn, requiredStats);
#pragma warning restore CS0618
                if (sc > bestScore)
                {
                    bestScore = sc; best = cand; backingThing = BackingThing(cand, pawn) ?? (cand as Thing);
                }
            }
            if (!searchMap || pawn.Map == null) return best;
            var map = pawn.Map; var seen = new HashSet<int>(); if (backingThing != null) seen.Add(backingThing.thingIDNumber);
            // Radial
            foreach (var t in GenRadial.RadialDistinctThingsAround(pawn.Position, map, ST_RadialSearchRadius, true))
            {
                if (t == null) continue; SurvivalTool cand = t as SurvivalTool;
                if (cand == null && t.def.IsToolStuff()) cand = VirtualTool.FromThing(t);
                if (cand == null) continue; var bt = BackingThing(cand, pawn) ?? (cand as Thing);
                if (bt == null || seen.Contains(bt.thingIDNumber) || bt.IsForbidden(pawn) || !pawn.CanReserveAndReach(bt, PathEndMode.OnCell, pawn.NormalMaxDanger())) continue;
                if (!ToolImprovesAny(cand, requiredStats)) continue; seen.Add(bt.thingIDNumber);
#pragma warning disable CS0618 // Phase 3: Legacy method still used internally
                float sc = ScoreToolForStats(cand, pawn, requiredStats) - ST_DistancePenaltyPerTile * bt.Position.DistanceTo(pawn.Position);
#pragma warning restore CS0618
                if (sc > bestScore) { bestScore = sc; best = cand; backingThing = bt; }
            }
            // Storage + haulables passes
            var haulables = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < haulables.Count; i++)
                {
                    var t = haulables[i]; if (t == null || t.Map != map) continue;
                    float dist = t.Position.DistanceTo(pawn.Position);
                    if (pass == 0 && (!t.IsInAnyStorage() || dist > ST_StorageSearchRadius)) continue;
                    if (pass == 1 && (t.IsInAnyStorage() || dist > ST_StorageSearchRadius)) continue;
                    if (pass == 2 && dist > ST_FallbackSearchRadius) continue;
                    SurvivalTool cand = t as SurvivalTool; if (cand == null && t.def.IsToolStuff()) cand = VirtualTool.FromThing(t);
                    if (cand == null) continue; var bt = BackingThing(cand, pawn) ?? (cand as Thing);
                    if (bt == null || seen.Contains(bt.thingIDNumber) || bt.IsForbidden(pawn) || !pawn.CanReserveAndReach(bt, PathEndMode.OnCell, pawn.NormalMaxDanger())) continue;
                    if (!ToolImprovesAny(cand, requiredStats)) continue; seen.Add(bt.thingIDNumber);
#pragma warning disable CS0618 // Phase 3: Legacy method still used internally
                    float sc = ScoreToolForStats(cand, pawn, requiredStats) - ST_DistancePenaltyPerTile * dist;
#pragma warning restore CS0618
                    if (sc > bestScore) { bestScore = sc; best = cand; backingThing = bt; }
                }
            }
            return best;
        }
        #endregion

        /// <summary>
        /// Deterministic counters for virtual-source wear. Keyed by SourceThing.thingIDNumber.
        /// We use this to implement tick-based deterministic degradation for virtual tools
        /// instead of probabilistic per-call damage. This prevents variance across runs
        /// and makes debugging easier.
        /// </summary>
        private static readonly Dictionary<int, int> _virtualToolDegradeCounters = new Dictionary<int, int>();

        /// <summary>
        /// Safely removes any deterministic counters associated with the provided thing.
        /// Call this when a backing thing is destroyed, dropped, or otherwise replaced so
        /// counters don't accumulate for stale Thing ids across maps/saves.
        /// </summary>
        public static void ClearCountersForThing(Thing t)
        {
            try
            {
                if (t == null) return;
                int key = t.thingIDNumber;
                if (_virtualToolDegradeCounters.ContainsKey(key))
                    _virtualToolDegradeCounters.Remove(key);
            }
            catch { }
        }

        /// <summary>
        /// Remove all deterministic counters. Useful on map unload / game load to avoid
        /// retaining stale thing ids across sessions.
        /// </summary>
        public static void ClearAllCounters()
        {
            try { _virtualToolDegradeCounters.Clear(); } catch { }
        }
        // Deduplication for debug logs in RelevantStatsFor
        private static readonly HashSet<string> loggedJobStatKeys = new HashSet<string>();
        private static readonly HashSet<string> loggedJobDefStatKeys = new HashSet<string>();
        #region Constants & fields

        public static readonly FloatRange MapGenToolHitPointsRange = new FloatRange(0.3f, 0.7f);
        public const float MapGenToolMaxStuffMarketValue = 3f;

        // Quality multiplier curve for tools. This maps QualityCategory (as int) to an effectiveness multiplier.
        // Values chosen to make quality meaningfully impact work speed without dominating base tool factors.
        // Awful=0, Poor=1, Normal=2, Good=3, Excellent=4, Masterwork=5, Legendary=6
        public static readonly SimpleCurve ToolQualityCurve = new SimpleCurve()
        {
            new CurvePoint(0f, 0.6f), // Awful
            new CurvePoint(1f, 0.8f), // Poor
            new CurvePoint(2f, 1.0f), // Normal
            new CurvePoint(3f, 1.1f), // Good
            new CurvePoint(4f, 1.25f), // Excellent
            new CurvePoint(5f, 1.4f), // Masterwork
            new CurvePoint(6f, 1.6f)  // Legendary
        };

        #endregion

        #region Tool factor cache (delayed init)

        /// <summary>
        /// Centralized cache for computed work stat factors per (toolDef, stuffDef) pair.
        /// This cache supports delayed activation: during the first ~3 seconds after a save
        /// loads we avoid populating the cache to prevent CTDs caused by other systems
        /// not being fully initialized. While not activated callers receive computed results
        /// on-the-fly but nothing is persisted in the cache until activation.
        ///
        /// Keys are lightweight strings "toolDef|stuffDef" to minimize allocations in hot paths.
        /// </summary>
        internal static class ToolFactorCache
        {
            private static readonly Dictionary<string, List<StatModifier>> _cache =
                new Dictionary<string, List<StatModifier>>(512);
            // Cache for computed no-tool penalty multipliers per statDefName
            private static readonly Dictionary<string, float> _noToolPenaltyCache = new Dictionary<string, float>(64);

            private static int _activationTick = -1;
            internal static bool Initialized { get; private set; } = false;

            private const int LOG_ONCE_KEY_TTL = 1; // placeholder to use existing keyed logging

            /// <summary>
            /// Schedule cache activation at the specified game tick. Call this from
            /// the validation GameComponent when scheduling delayed init (3s after load).
            /// </summary>
            internal static void ScheduleActivation(int activationTick)
            {
                _activationTick = activationTick;
            }

            /// <summary>
            /// Called each tick by GameComponent_SurvivalToolsValidation.GameComponentTick to
            /// flip the Initialized flag when the scheduled activation tick is reached.
            /// </summary>
            internal static void CheckActivation()
            {
                if (Initialized) return;
                if (_activationTick <= 0) return;
                try
                {
                    if (Find.TickManager != null && Find.TickManager.TicksGame >= _activationTick)
                    {
                        Initialized = true;
                        // Optionally log once when cache becomes active
                        if (ST_Logging.IsDebugLoggingEnabled && ST_Logging.ShouldLogWithCooldown("ToolFactorCache_Activated"))
                        {
                            ST_Logging.LogDebug("[SurvivalTools] ToolFactorCache initialized and ready.", "ToolFactorCache_Activated");
                        }
                    }
                }
                catch { /* best-effort */ }
            }

            /// <summary>
            /// Compute or retrieve cached factors for the given tool definition + stuff.
            /// If the cache isn't initialized yet we compute on-the-fly and do not store.
            /// This method is defensive and avoids LINQ in hot paths.
            /// </summary>
            internal static List<StatModifier> GetOrComputeToolFactors(ThingDef toolDef, ThingDef stuff, SurvivalTool instance = null)
            {
                if (toolDef == null) return new List<StatModifier>();

                string key = MakeKey(toolDef, stuff);

                // If not initialized, compute on the fly and do not cache.
                if (!Initialized)
                {
                    var adHoc = ComputeFactors(toolDef, stuff, instance);
                    // Log computation once per key to aid diagnostics
                    if (ST_Logging.IsDebugLoggingEnabled && ST_Logging.ShouldLogWithCooldown($"ToolFactorComputed_{key}"))
                        ST_Logging.LogDebug($"[SurvivalTools] Computed factors (adhoc) for {key}: {FormatFactors(adHoc)}", $"ToolFactorComputed_{key}");
                    return adHoc;
                }

                // Try cached fast-path
                if (_cache.TryGetValue(key, out var cached) && cached != null)
                    return cached;

                // Miss: compute and populate cache
                var computed = ComputeFactors(toolDef, stuff, instance);
                // Defensive: ensure we always store a non-null list
                _cache[key] = computed ?? new List<StatModifier>();

                if (ST_Logging.IsDebugLoggingEnabled && ST_Logging.ShouldLogWithCooldown($"ToolFactorCached_{key}"))
                    ST_Logging.LogDebug($"[SurvivalTools] Cached factors for {key}: {FormatFactors(_cache[key])}", $"ToolFactorCached_{key}");

                return _cache[key];
            }

            /// <summary>
            /// Remove cached entries related to the provided thing (tool instance or stuff def).
            /// Call this when a tool is crafted, destroyed, dropped, or when its HP reaches 0.
            /// </summary>
            internal static void InvalidateForThing(Thing thing)
            {
                if (thing == null) return;
                // If it's a SurvivalTool, clear its specific key(s). If it's a stuff def we need to
                // clear any key that references that stuff (scan keys). Scanning is rare (on events)
                // so it's acceptable to iterate the keys.
                try
                {
                    if (thing is SurvivalTool st)
                    {
                        string k = MakeKey(st.def, st.Stuff);
                        _cache.Remove(k);
                    }
                    else
                    {
                        // Stuff-level invalidation: remove any key that ends with |stuffDefName
                        string sd = thing.def?.defName ?? null;
                        if (!string.IsNullOrEmpty(sd))
                        {
                            var keys = new List<string>(_cache.Keys);
                            for (int i = 0; i < keys.Count; i++)
                            {
                                var kk = keys[i];
                                if (kk.EndsWith("|" + sd))
                                    _cache.Remove(kk);
                            }
                        }
                    }
                    // When a thing (tool or stuff) changes, the no-tool penalty for affected stats may change
                    try
                    {
                        _noToolPenaltyCache.Clear();
                    }
                    catch { }
                }
                catch { }
            }

            /// <summary>
            /// Removes everything from the cache (map reset/load).
            /// </summary>
            internal static void ClearAll()
            {
                _cache.Clear();
                _noToolPenaltyCache.Clear();
            }

            /// <summary>
            /// Compute or retrieve the cached no-tool penalty multiplier for the provided stat.
            /// This implements the tiered penalty logic described in settings: normal, hardcore (+25%), extraHardcore (+25%) stacking multiplicatively.
            /// Always returns a non-zero value (never blocks jobs here).
            /// </summary>
            internal static float GetOrComputeNoToolPenalty(StatDef stat)
            {
                if (stat == null) return 1f;

                string key = stat.defName;
                if (Initialized && _noToolPenaltyCache.TryGetValue(key, out var cached))
                    return cached;

                // Compute
                float baseNormal = SurvivalTools.Settings?.noToolStatFactorNormal ?? 0.4f; // default fallback

                // If stat is optional in normal mode, treat as 1
                if (!SurvivalTools.Settings?.enableNormalModePenalties ?? false)
                {
                    // Normal mode penalties globally disabled
                    baseNormal = 1f;
                }

                // For WorkSpeedGlobal: if no gating jobs enabled, don't penalize
                if (stat == ST_StatDefOf.WorkSpeedGlobal)
                {
                    var settings = SurvivalTools.Settings;
                    if (settings != null)
                    {
                        var jobDict = settings.workSpeedGlobalJobGating;
                        if (jobDict != null)
                        {
                            bool anyGateEligibleJobEnabled = false;
                            foreach (var kvp in jobDict)
                            {
                                var jobDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail(kvp.Key);
                                if (jobDef != null && SurvivalToolUtility.ShouldGateByDefault(jobDef) && kvp.Value)
                                {
                                    anyGateEligibleJobEnabled = true;
                                    break;
                                }
                            }
                            if (!anyGateEligibleJobEnabled)
                                baseNormal = 1f;
                        }
                    }
                }

                float result;
                var settingsRef = SurvivalTools.Settings;
                // Start with normal base
                result = baseNormal;

                // Stack additional penalties additively on the remaining deficit so they feel multiplicative
                // Penalty stacking formula:
                // penalty = normal;
                // if hardcore: penalty += 0.4f * (1f - penalty);
                // if extraHardcore: penalty += 0.3f * (1f - penalty);
                // This ensures each mode increases the penalty towards 1 by the specified share of the remaining gap.
                if (settingsRef?.hardcoreMode ?? false)
                {
                    result += 0.4f * (1f - result);
                }
                if (settingsRef?.extraHardcoreMode ?? false)
                {
                    result += 0.3f * (1f - result);
                }

                // Ensure never zero
                if (result <= 0f) result = 0.0001f;

                try
                {
                    if (Initialized) _noToolPenaltyCache[key] = result;
                }
                catch { }

                return result;
            }

            // -----------------------
            // Internal helpers
            // -----------------------
            private static string MakeKey(ThingDef tool, ThingDef stuff)
            {
                var s = stuff?.defName ?? "<null>";
                return tool.defName + "|" + s;
            }

            private static List<StatModifier> ComputeFactors(ThingDef toolDef, ThingDef stuff, SurvivalTool instance)
            {
                var result = new List<StatModifier>();
                if (toolDef == null) return result;

                try
                {
                    // 1) From tool def mod extension
                    var defExt = toolDef.GetModExtension<SurvivalToolProperties>();
                    if (defExt?.baseWorkStatFactors != null)
                    {
                        for (int i = 0; i < defExt.baseWorkStatFactors.Count; i++)
                        {
                            var m = defExt.baseWorkStatFactors[i];
                            if (m != null && m.stat != null && m.value != 0f)
                                result.Add(new StatModifier { stat = m.stat, value = m.value });
                        }
                    }

                    // 2) From tool def statBases
                    if (toolDef.statBases != null)
                    {
                        for (int i = 0; i < toolDef.statBases.Count; i++)
                        {
                            var m = toolDef.statBases[i];
                            if (m != null && m.stat != null && m.value != 0f)
                                result.Add(new StatModifier { stat = m.stat, value = m.value });
                        }
                    }

                    // 3) From stuff extension
                    if (toolDef.MadeFromStuff && stuff != null)
                    {
                        var stuffExt = stuff.GetModExtension<SurvivalToolProperties>();
                        if (stuffExt?.baseWorkStatFactors != null)
                        {
                            for (int i = 0; i < stuffExt.baseWorkStatFactors.Count; i++)
                            {
                                var m = stuffExt.baseWorkStatFactors[i];
                                if (m != null && m.stat != null && m.value != 0f)
                                    result.Add(new StatModifier { stat = m.stat, value = m.value });
                            }
                        }

                        // StuffPropsTool multipliers are applied later when the instance exists.
                        var stuffPropsExt = stuff.GetModExtension<StuffPropsTool>();
                        if (stuffPropsExt != null && stuffPropsExt.toolStatFactors != null)
                        {
                            // We don't apply multipliers to virtual-only cached factors here; the
                            // SurvivalTool instance accessor may apply instance-specific multipliers
                            // when it wraps the cached base.
                        }
                    }

                    // Dedupe by stat keeping the max value
                    var dedup = new Dictionary<StatDef, float>();
                    for (int i = 0; i < result.Count; i++)
                    {
                        var mm = result[i];
                        if (mm == null || mm.stat == null) continue;
                        if (!dedup.TryGetValue(mm.stat, out var cur) || mm.value > cur)
                            dedup[mm.stat] = mm.value;
                    }

                    var final = new List<StatModifier>(dedup.Count);
                    foreach (var kv in dedup)
                        final.Add(new StatModifier { stat = kv.Key, value = kv.Value });

                    // If we have an instance and it has effectiveness multipliers, apply them
                    if (instance != null)
                    {
                        // Base effectiveness value (can come from instance wear/HP or other mechanics)
                        float effectiveness = instance.GetStatValue(ST_StatDefOf.ToolEffectivenessFactor);

                        // Determine quality factor: if enabled in settings, read CompQuality on the backing thing
                        float qualityFactor = 1f;
                        try
                        {
                            if (SurvivalTools.Settings?.useQualityToolScaling ?? false)
                            {
                                var comp = (instance as ThingWithComps)?.TryGetComp<CompQuality>();
                                QualityCategory qc = comp != null ? comp.Quality : QualityCategory.Normal;
                                qualityFactor = ToolQualityCurve.Evaluate((int)qc);
                            }
                        }
                        catch { /* best-effort quality scaling */ }

                        float combinedMult = effectiveness * qualityFactor;
                        if (combinedMult != 1f)
                        {
                            for (int i = 0; i < final.Count; i++)
                                final[i] = new StatModifier { stat = final[i].stat, value = final[i].value * combinedMult };
                        }

                        // Apply StuffPropsTool multipliers if applicable (material multipliers)
                        if (instance.Stuff != null)
                        {
                            var sprops = instance.Stuff.GetModExtension<StuffPropsTool>();
                            if (sprops?.toolStatFactors != null)
                            {
                                for (int i = 0; i < final.Count; i++)
                                {
                                    var stat = final[i].stat;
                                    float mult = sprops.toolStatFactors.GetStatFactorFromList(stat);
                                    final[i] = new StatModifier { stat = stat, value = final[i].value * mult };
                                }
                            }
                        }
                    }

                    return final;
                }
                catch
                {
                    return new List<StatModifier>();
                }
            }

            private static string FormatFactors(List<StatModifier> list)
            {
                if (list == null || list.Count == 0) return "(none)";
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < list.Count; i++)
                {
                    var m = list[i];
                    if (m?.stat != null)
                    {
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(m.stat.defName).Append("=").Append(m.value.ToString("F2"));
                    }
                }
                return sb.ToString();
            }
        }

        #endregion

        #region Settings toggles (fast access)

        public static bool IsHardcoreModeEnabled => SurvivalTools.Settings?.hardcoreMode ?? false;

        public static bool IsToolDegradationEnabled =>
            (SurvivalTools.Settings?.EffectiveToolDegradationFactor ?? 0f) > 0.001f;

        public static bool IsToolMapGenEnabled => SurvivalTools.Settings?.toolMapGen ?? false;

        #endregion

        #region Backing resolution & virtual/tool-stuff support
        /// <summary>
        /// Returns true if the given WorkGiverDef is eligible for survival tool gating by default.
        /// Uses keyword lists to filter jobs (never gate vs gate-eligible).
        /// </summary>
        public static bool ShouldGateByDefault(WorkGiverDef wgDef)
        {
            if (wgDef == null) return false;
            var name = wgDef.defName.ToLower();
            var label = !string.IsNullOrEmpty(wgDef.label) ? wgDef.label.ToLower() : "";

            // Never gate keywords
            var neverGateKeywords = new[]
            {
                    /*"repair", "buildroofs", "deconstruct", */ "deliver", "haul", "clean", "rescue", "tend", "handling", "feed", "cookfillhopper", "paint", "remove", "train", "childcarer"
                };
            if (neverGateKeywords.Any(keyword => name.Contains(keyword) || label.Contains(keyword)))
            {
                // Targeted debug log for deconstruct decisions
                if (ST_Logging.IsDebugLoggingEnabled && (name.Contains("deconstruct") || label.Contains("deconstruct")))
                {
                    ST_Logging.LogDebug($"[SurvivalTools.ShouldGateByDefault] WorkGiver '{wgDef.defName}' labeled '{wgDef.label}' matched never-gate keywords -> NOT gate-eligible.", $"ShouldGate_Deconstruct_{wgDef.defName}");
                }
                return false;
            }

            // Gate-eligible keywords
            var gateKeywords = new[]
            {
                    "repair", "buildroofs","craft", "smith", "tailor", "art", "sculpt", "fabricate", "produce", "drug", "butcher", "cook", "medical", "surgery", "research", "analyse", "deconstruct"
                };
            if (gateKeywords.Any(keyword => name.Contains(keyword) || label.Contains(keyword)))
            {
                // Targeted debug log for deconstruct decisions
                if (ST_Logging.IsDebugLoggingEnabled && (name.Contains("deconstruct") || label.Contains("deconstruct")))
                {
                    ST_Logging.LogDebug($"[SurvivalTools.ShouldGateByDefault] WorkGiver '{wgDef.defName}' labeled '{wgDef.label}' matched gate keywords -> gate-eligible.", $"ShouldGate_Deconstruct_{wgDef.defName}");
                }
                return true;
            }

            // Default: not gate-eligible
            return false;
        }
        // In SurvivalToolUtility.cs
        public static Thing FindBestToolForStats(Pawn pawn, List<StatDef> stats)
        {
            if (pawn == null || stats.NullOrEmpty())
                return null;

            Thing bestTool = null;
            float bestScore = 0f;

            IEnumerable<Thing> candidates = pawn.GetAllUsableSurvivalTools();
            foreach (var thing in candidates)
            {
                var st = thing as SurvivalTool;
                if (st == null) continue;

                float score = 0f;
                int matches = 0;

                foreach (var stat in stats)
                {
                    var factor = st.WorkStatFactors?.FirstOrDefault(m => m.stat == stat);
                    if (factor != null)
                    {
                        score += factor.value;
                        matches++;
                    }
                }

                if (matches > 1)
                    score *= 1.2f;

                if (thing.HitPoints < thing.MaxHitPoints)
                    score *= (float)thing.HitPoints / thing.MaxHitPoints;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTool = thing;
                }
            }

            return bestTool;
        }

        /// <summary>
        /// Return the physical backing <see cref="Thing"/> for a <see cref="SurvivalTool"/> (real or virtual).
        /// For virtuals, prefers SourceThing if available, then pawn inventory, then closest reachable on pawn.Map, then any spawned.
        /// </summary>
        public static Thing BackingThing(SurvivalTool tool, Pawn pawn = null)
        {
            if (tool == null) return null;

            // Real tool: it's already a Thing (exclude virtual wrappers here)
            if (tool is Thing thingTool && !(tool is VirtualTool))
                return thingTool;

            // Virtual wrapper (tool-stuff)
            if (tool is VirtualTool vtool)
            {
                // 0) Direct SourceThing if available
                if (vtool.SourceThing != null)
                {
                    if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"BackingThing_Source_{vtool.SourceThing.ThingID}"))
                        LogDebug($"[BackingThing] Using direct SourceThing for {vtool.LabelNoCount}: {vtool.SourceThing.LabelCap}", $"BackingThing_Source_{vtool.SourceThing.ThingID}");
                    return vtool.SourceThing;
                }

                // 1) Pawn inventory (try exact match first, then by def)
                if (pawn?.inventory?.innerContainer != null)
                {
                    try
                    {
                        var exact = pawn.inventory.innerContainer.FirstOrDefault(t => ReferenceEquals(t, vtool.SourceThing));
                        if (exact != null)
                        {
                            if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"BackingThing_InvExact_{exact.ThingID}"))
                                LogDebug($"[BackingThing] Found exact SourceThing in inventory for {vtool.LabelNoCount}: {exact.LabelCap}", $"BackingThing_InvExact_{exact.ThingID}");
                            return exact;
                        }
                    }
                    catch { /* safe fail */ }

                    var invThing = pawn.inventory.innerContainer.FirstOrDefault(t => t.def == vtool.SourceDef);
                    if (invThing != null)
                    {
                        if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"BackingThing_InvDef_{invThing.ThingID}"))
                            LogDebug($"[BackingThing] Found by def in inventory for {vtool.LabelNoCount}: {invThing.LabelCap}", $"BackingThing_InvDef_{invThing.ThingID}");
                        return invThing;
                    }
                }

                // 2) Closest reachable on pawn's map
                if (pawn?.Map != null)
                {
                    Predicate<Thing> validator = t =>
                        t.Spawned &&
                        !t.IsForbidden(pawn) &&
                        pawn.CanReserveAndReach(t, PathEndMode.OnCell, pawn.NormalMaxDanger());

                    var found = GenClosest.ClosestThingReachable(
                        pawn.Position,
                        pawn.Map,
                        ThingRequest.ForDef(vtool.SourceDef),
                        PathEndMode.OnCell,
                        TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false),
                        maxDistance: 9999f,
                        validator: validator);

                    if (found != null)
                    {
                        if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"BackingThing_Map_{found.ThingID}"))
                            LogDebug($"[BackingThing] Found closest reachable on map for {vtool.LabelNoCount}: {found.LabelCap}", $"BackingThing_Map_{found.ThingID}");
                        return found;
                    }
                }

                // 3) Any spawned instance (fallback)
                foreach (var map in Find.Maps)
                {
                    var any = map.listerThings.ThingsOfDef(vtool.SourceDef).FirstOrDefault(t => t.Spawned);
                    if (any != null)
                    {
                        if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"BackingThing_Global_{any.ThingID}"))
                            LogDebug($"[BackingThing] Found fallback spawned instance for {vtool.LabelNoCount}: {any.LabelCap}", $"BackingThing_Global_{any.ThingID}");
                        return any;
                    }
                }

                if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"BackingThing_None_{vtool.GetHashCode()}"))
                    LogDebug($"[BackingThing] No backing Thing found for {vtool.LabelNoCount}", $"BackingThing_None_{vtool.GetHashCode()}");

                return null;
            }

            // Unknown subclass
            return null;
        }


        public static bool HasBackingThing(SurvivalTool tool, Pawn pawn = null) => BackingThing(tool, pawn) != null;

        public static bool IsToolStuff(this ThingDef def) =>
            def.IsStuff && (
                def.GetModExtension<SurvivalToolProperties>()?.baseWorkStatFactors?.Any() == true
                || def.statBases?.Any(sb => sb != null && sb.stat != null) == true);

        #endregion

        #region Def caches

        public static List<StatDef> SurvivalToolStats { get; } =
            DefDatabase<StatDef>.AllDefsListForReading.Where(s => s.RequiresSurvivalTool()).ToList();

        public static List<WorkGiverDef> SurvivalToolWorkGivers { get; } =
            DefDatabase<WorkGiverDef>.AllDefsListForReading.Where(w => w.HasModExtension<WorkGiverExtension>()).ToList();

        #endregion

        #region Relevant stat selection (WorkGiver/Job)

        public static List<StatDef> RelevantStatsFor(WorkGiverDef wg, Job job)
        {
            // Prefer job-level detection first (some mods inject workgivers that reuse a different
            // WorkGiverDef for a job — we must honor the JobDef's semantics). If job-based
            // detection yields stats, return them. Otherwise fall back to WorkGiver mod-extensions
            // or heuristics.

            // 1) Job-level detection
            // Handle explicit common null-workgiver jobs first (defensive)
            if (job?.def == JobDefOf.Clean)
                return new List<StatDef> { ST_StatDefOf.CleaningSpeed };

            var jobStats = StatsForJob(job?.def);
            if (jobStats != null && jobStats.Count > 0)
                return jobStats;

            // 2) WorkGiver mod extension
            var fromWg = wg?.GetModExtension<WorkGiverExtension>()?.requiredStats;
            if (fromWg != null && fromWg.Any())
                return fromWg.Where(s => s != null).Distinct().ToList();

            // Pattern matching for job def name (fallback)
            var defName = job?.def?.defName?.ToLowerInvariant() ?? string.Empty;
            var stats = new List<StatDef>();
            if (defName.Contains("clean") || defName.Contains("sweep") || defName.Contains("mop"))
                stats.Add(ST_StatDefOf.CleaningSpeed);
            if (defName.Contains("butcher") || defName.Contains("slaughter"))
                stats.Add(ST_StatDefOf.ButcheryFleshSpeed);
            if (defName.Contains("medical") || defName.Contains("surgery") || defName.Contains("operate"))
                stats.Add(ST_StatDefOf.MedicalOperationSpeed);
            if (defName.Contains("harvest") && defName.Contains("plant"))
                stats.Add(ST_StatDefOf.PlantHarvestingSpeed);
            if (defName.Contains("fell") && defName.Contains("tree"))
                stats.Add(ST_StatDefOf.TreeFellingSpeed);

            // Additional context-based detection for injected jobs:
            // If job target is filth, cleaning is required
            if (job?.targetA.Thing is Filth || job?.targetB.Thing is Filth)
                stats.Add(ST_StatDefOf.CleaningSpeed);
            // If job target is a corpse or butcherable, butchery is required
            if (job?.targetA.Thing is Corpse || job?.targetB.Thing is Corpse)
                stats.Add(ST_StatDefOf.ButcheryFleshSpeed);
            // If job target is a medical bed or pawn needing tending, medical is required
            if ((job?.targetA.Thing is Building_Bed bed && bed.Medical) || (job?.targetA.Thing is Pawn p && p.Downed))
                stats.Add(ST_StatDefOf.MedicalOperationSpeed);

            // Only return distinct, non-null stats
            stats = stats.Where(s => s != null).Distinct().ToList();
            if (stats.Count > 0)
                return stats;

            // Fallback to StatsForJob for other cases
            return StatsForJob(job);
        }

        public static List<StatDef> RelevantStatsFor(WorkGiverDef wg, JobDef jobDef)
        {
            // Prefer job-def specific mapping first (covers cases where a workgiver is re-used
            // for an injected job). Only fall back to WorkGiverDef mod-extensions if the
            // job-def mapping yields no stats.
            if (jobDef == JobDefOf.CutPlant)
                return new List<StatDef> { ST_StatDefOf.PlantHarvestingSpeed };

            // Explicit mapping for cleaning job when workGiver may be null
            if (jobDef == JobDefOf.Clean)
                return new List<StatDef> { ST_StatDefOf.CleaningSpeed };
            // Quick pattern: crafting/bill-like jobs -> WorkSpeedGlobal
            var jobNameLower = jobDef?.defName?.ToLowerInvariant() ?? string.Empty;
            if (jobNameLower.Contains("dobill") || jobNameLower.Contains("do_bill") || jobNameLower.Contains("bill") || jobNameLower.Contains("craft") || jobNameLower.Contains("fabricate") || jobNameLower.Contains("produce") || jobNameLower.Contains("manufacture") || jobNameLower.Contains("smith") || jobNameLower.Contains("tailor") || jobNameLower.Contains("sculpt"))
            {
                if (IsDebugLoggingEnabled)
                {
                    string key = $"JobDef_CraftingFallback_{jobDef?.defName ?? "null"}";
                    if (!loggedJobDefStatKeys.Contains(key))
                    {
                        loggedJobDefStatKeys.Add(key);
                        LogDebug($"[SurvivalTools] JobDef '{jobDef?.defName ?? "null"}' matched crafting fallback -> WorkSpeedGlobal", key);
                    }
                }
                return new List<StatDef> { ST_StatDefOf.WorkSpeedGlobal };
            }

            var fallback = StatsForJob(jobDef);
            if (fallback != null && fallback.Count > 0)
                return fallback;

            var fromWg = wg?.GetModExtension<WorkGiverExtension>()?.requiredStats;
            if (fromWg != null && fromWg.Any())
                return fromWg.Where(s => s != null).Distinct().ToList();
            // Use class-level loggedJobDefStatKeys
            if (IsToolRelevantJob(jobDef) && IsDebugLoggingEnabled)
            {
                string key = $"JobDefFallback_{wg?.defName ?? "null"}_{jobDef?.defName ?? "null"}";
                if (!loggedJobDefStatKeys.Contains(key))
                {
                    loggedJobDefStatKeys.Add(key);
                    LogDebug($"[SurvivalTools] Using job fallback stats for WGD='{wg?.defName ?? "null"}' Job='{jobDef?.defName ?? "null"}': {string.Join(", ", fallback.Select(s => s.defName))}", key);
                }
            }
            return fallback;
        }

        public static List<StatDef> StatsForJob(Job job) => StatsForJob(job?.def);

        private static bool IsToolRelevantJob(JobDef jobDef)
        {
            if (jobDef == null) return false;

            if (jobDef == JobDefOf.Mine ||
                jobDef == ST_JobDefOf.FellTree || jobDef == ST_JobDefOf.FellTreeDesignated ||
                jobDef == ST_JobDefOf.HarvestTree || jobDef == ST_JobDefOf.HarvestTreeDesignated)
                return true;

            var s = jobDef.defName.ToLowerInvariant();
            return s.Contains("construct") || s.Contains("build") || s.Contains("frame") ||
                   s.Contains("smooth") || s.Contains("install") || s.Contains("roof") ||
                   s.Contains("repair") || s.Contains("uninstall") || s.Contains("deconstruct") ||
                   s.Contains("fell") || s.Contains("tree") || s.Contains("harvest") ||
                   s.Contains("sow") || s.Contains("plant") || s.Contains("grow") ||
                   s.Contains("research") || s.Contains("study") ||
                   s.Contains("clean") || s.Contains("sweep") || s.Contains("mop") ||
                   s.Contains("medical") || s.Contains("surgery") || s.Contains("operate") ||
                   s.Contains("butcher") || s.Contains("slaughter");
        }

        public static List<StatDef> StatsForJob(JobDef jobDef) => StatsForJob(jobDef, null);

        public static List<StatDef> StatsForJob(JobDef jobDef, Pawn pawn)
        {
            var list = new List<StatDef>(2);
            if (jobDef == null) return list;

            bool relevant = IsToolRelevantJob(jobDef);
            // Quiet debug/info logs unless author attention is needed
            if (IsDebugLoggingEnabled && relevant && ShouldLogJobForPawn(pawn, jobDef))
                LogDebug($"[SurvivalTools.Debug] StatsForJob called for: {jobDef.defName}", $"StatsForJob_{jobDef.defName}");

            if (jobDef == JobDefOf.Mine)
            {
                list.Add(ST_StatDefOf.DiggingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> DiggingSpeed", $"DiggingSpeed_{jobDef.defName}");
                return list;
            }

            // Explicit mapping for vanilla Research job (precedes generic substring heuristic)
            if (jobDef == JobDefOf.Research)
            {
                list.Add(ST_StatDefOf.ResearchSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug("[SurvivalTools.Debug] Research -> ResearchSpeed", "ResearchSpeed_JobDefOfResearch");
                return list;
            }

            if (jobDef == ST_JobDefOf.FellTree || jobDef == ST_JobDefOf.FellTreeDesignated ||
                jobDef == ST_JobDefOf.HarvestTree || jobDef == ST_JobDefOf.HarvestTreeDesignated)
            {
                list.Add(ST_StatDefOf.TreeFellingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> TreeFellingSpeed", $"TreeFellingSpeed_{jobDef.defName}");
                return list;
            }

            if (jobDef == JobDefOf.Clean)
            {
                list.Add(ST_StatDefOf.CleaningSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> CleaningSpeed (direct JobDefOf.Clean mapping)", $"CleaningSpeed_{jobDef.defName}");
                return list;
            }


            var s = jobDef.defName.ToLowerInvariant();
            if (IsDebugLoggingEnabled && relevant && ShouldLogJobForPawn(pawn, jobDef))
                LogDebug($"[SurvivalTools.Debug] Checking job: '{jobDef.defName}' (lowercase: '{s}')", $"CheckingJob_{jobDef.defName}_{s}");

            // Crafting / bills / production tasks -> WorkSpeedGlobal
            // Many mods and vanilla use 'DoBill' or 'bill' in job names for production; catch those.
            if (s.Contains("dobill") || s.Contains("do_bill") || s.Contains("bill") || s.Contains("craft") || s.Contains("fabricate") || s.Contains("produce") || s.Contains("manufacture") || s.Contains("smith") || s.Contains("tailor") || s.Contains("art") || s.Contains("sculpt"))
            {
                list.Add(ST_StatDefOf.WorkSpeedGlobal);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> WorkSpeedGlobal (crafting/production)", $"WorkSpeedGlobal_{jobDef.defName}");
                return list;
            }

            // Cooking / food production -> WorkSpeedGlobal (catch cook/cookfillhopper)
            if (s.Contains("cook") || s.Contains("cookfillhopper") || s.Contains("preparefood") || s.Contains("food"))
            {
                list.Add(ST_StatDefOf.WorkSpeedGlobal);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> WorkSpeedGlobal (cooking)", $"WorkSpeedGlobal_Cook_{jobDef.defName}");
                return list;
            }

            if (s == "cutplant")
            {
                list.Add(ST_StatDefOf.PlantHarvestingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> PlantHarvestingSpeed", $"PlantHarvestingSpeed_{jobDef.defName}");
                return list;
            }

            if (s.Contains("felltree"))
            {
                list.Add(ST_StatDefOf.TreeFellingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> TreeFellingSpeed (contains felltree)", $"TreeFellingSpeedFelltree_{jobDef.defName}");
                return list;
            }

            if (s == "wait_maintainposture")
                return list; // exclude false positive

            if (s.Contains("construct") || s.Contains("build") || s.Contains("frame") ||
                s.Contains("smooth") || s.Contains("install") || s.Contains("buildroof") ||
                s.Contains("removeroof"))
            {
                list.Add(StatDefOf.ConstructionSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> ConstructionSpeed", $"ConstructionSpeed_{jobDef.defName}");
                return list;
            }

            if (s.Contains("repair") || s.Contains("maintain") || s.Contains("maintenance") ||
                s.Contains("fixbroken") || s.Contains("tendmachine") || s.Contains("fix"))
            {
                list.Add(ST_StatDefOf.MaintenanceSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> MaintenanceSpeed", $"MaintenanceSpeed_{jobDef.defName}");
                return list;
            }

            if (s.Contains("uninstall") || s.Contains("deconstruct") || s.Contains("teardown"))
            {
                list.Add(ST_StatDefOf.DeconstructionSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> DeconstructionSpeed", $"DeconstructionSpeed_{jobDef.defName}");
                return list;
            }

            // Salvage / dismantle synonyms -> DeconstructionSpeed
            if (s.Contains("salvage") || s.Contains("dismantle") || s.Contains("scrap"))
            {
                list.Add(ST_StatDefOf.DeconstructionSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> DeconstructionSpeed (salvage/dismantle)", $"DeconstructionSpeed_Salvage_{jobDef.defName}");
                return list;
            }

            if (s.Contains("sow") || s.Contains("plantsow") || s.Contains("plantgrow"))
            {
                list.Add(ST_StatDefOf.SowingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> SowingSpeed", $"SowingSpeed_{jobDef.defName}");
                return list;
            }

            if (s.Contains("plant") || s.Contains("harvest") || s.Contains("cut"))
            {
                list.Add(ST_StatDefOf.PlantHarvestingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> PlantHarvestingSpeed", $"PlantHarvestingSpeed_{jobDef.defName}");
                return list;
            }

            if (s.Contains("research") || s.Contains("experiment") || s.Contains("study"))
            {
                list.Add(ST_StatDefOf.ResearchSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> ResearchSpeed", $"ResearchSpeed_{jobDef.defName}");
                return list;
            }

            if (s.Contains("clean") || s.Contains("sweep") || s.Contains("mop"))
            {
                list.Add(ST_StatDefOf.CleaningSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> CleaningSpeed", $"CleaningSpeed_{jobDef.defName}");
                return list;
            }

            if (s.Contains("medical") || s.Contains("surgery") || s.Contains("operate") || s.Contains("tend") ||
                s.Contains("doctor") || (s.Contains("install") && s.Contains("bionic")))
            {
                list.Add(ST_StatDefOf.MedicalOperationSpeed);
                list.Add(ST_StatDefOf.MedicalSurgerySuccessChance);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> MedicalOperationSpeed + MedicalSurgerySuccessChance", $"Medical_{jobDef.defName}");
                return list;
            }

            if (s.Contains("butcher") || s.Contains("slaughter") || s.Contains("skin") || s.Contains("carve"))
            {
                list.Add(ST_StatDefOf.ButcheryFleshSpeed);
                list.Add(ST_StatDefOf.ButcheryFleshEfficiency);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> ButcheryFleshSpeed + ButcheryFleshEfficiency", $"Butchery_{jobDef.defName}");
                return list;
            }

            if (IsDebugLoggingEnabled && relevant && ShouldLogJobForPawn(pawn, jobDef))
                LogDebug($"[SurvivalTools.Debug] {jobDef.defName} -> No stats (no patterns matched)", $"NoStats_{jobDef.defName}");

            // If this jobDef appears tool-relevant but we didn't match any known patterns,
            // log once per jobDef to help mod authors diagnose unmapped jobs.
            if (IsToolRelevantJob(jobDef) && IsDebugLoggingEnabled)
            {
                string key = $"StatsForJob_Fallback_{jobDef.defName}";
                if (!loggedJobStatKeys.Contains(key))
                {
                    loggedJobStatKeys.Add(key);
                    LogDebug($"[SurvivalTools] StatsForJob fallback: jobDef='{jobDef.defName}' did not match patterns. Consider adding mapping.", key);
                }
            }

            return list;
        }

        #endregion

        #region Tool/type checks

        public static bool RequiresSurvivalTool(this StatDef stat)
        {
            if (stat?.parts.SafeAny() != true) return false;
            for (int i = 0; i < stat.parts.Count; i++)
                if (stat.parts[i] is Stats.StatPart_SurvivalTools)
                    return true;
            return false;
        }

        public static bool IsSurvivalTool(this BuildableDef def, out SurvivalToolProperties toolProps)
        {
            toolProps = def?.GetModExtension<SurvivalToolProperties>();
            return def.IsSurvivalTool();
        }

        public static bool IsSurvivalTool(this BuildableDef def)
        {
            if (!(def is ThingDef tDef)) return false;

            // Actual SurvivalTool class?
            if (typeof(SurvivalTool).IsAssignableFrom(tDef.thingClass))
                return true;

            // Or "enhanced" item with our extension & factors
            var ext = tDef.SafeGetModExtension<SurvivalToolProperties>();
            // Also consider defs that provide relevant stats via statBases (eg WorkSpeedGlobal)
            bool hasStatBases = tDef.statBases?.Any(sb => sb != null && sb.stat != null) == true;
            return ext != null && ext != SurvivalToolProperties.defaultValues && (ext.baseWorkStatFactors.SafeAny() || hasStatBases);
        }

        #endregion

        #region Pawn inventory & tool access

        public static bool CanUseSurvivalTools(this Pawn pawn) =>
            pawn.RaceProps.intelligence >= Intelligence.ToolUser &&
            pawn.Faction == Faction.OfPlayer &&
            (pawn.equipment != null || pawn.inventory != null) &&
            pawn.TraderKind == null;

        public static bool IsUnderSurvivalToolCarryLimitFor(this int count, Pawn pawn) =>
            !SurvivalTools.Settings.toolLimit || count < pawn.GetStatValue(ST_StatDefOf.SurvivalToolCarryCapacity);

        public static IEnumerable<Thing> GetHeldSurvivalTools(this Pawn pawn)
        {
            if (pawn?.inventory?.innerContainer == null)
                return Enumerable.Empty<Thing>();

            // Real tools
            var normalTools = pawn.inventory.innerContainer.Where(t => t.def.IsSurvivalTool());

            // Tool-stuffs => wrap into virtual tool objects (still Things)
            var virtualTools = pawn.inventory.innerContainer
                .Where(t => t.def.IsToolStuff())
                .Select(t => (Thing)VirtualTool.FromThing(t))
                .Where(vt => vt != null);

            return normalTools.Concat(virtualTools);
        }

        public static int HeldSurvivalToolCount(this Pawn pawn) =>
            pawn.inventory?.innerContainer?.Count(t => t.def.IsSurvivalTool()) ?? 0;

        public static bool CanCarryAnyMoreSurvivalTools(this Pawn pawn, int heldToolOffset = 0) =>
            (pawn.RaceProps.Humanlike && (pawn.HeldSurvivalToolCount() + heldToolOffset).IsUnderSurvivalToolCarryLimitFor(pawn))
            || pawn.IsFormingCaravan() || pawn.IsCaravanMember();

        public static IEnumerable<Thing> GetUsableHeldSurvivalTools(this Pawn pawn)
        {
            // Historical behavior: we truncated the list to the pawn's carry limit so that
            // excess tools (beyond limit) were ignored by optimizers and drawing logic.
            // This caused a correctness bug: if the only matching/expected tool (e.g. an axe
            // for TreeFellingSpeed) happened to be positioned beyond the truncated index range
            // (e.g. third tool while carry limit stat allowed only two), best-tool selection
            // and renderer passed over it, yielding null despite the pawn physically holding
            // a valid improving tool. We now return the full set of held tools for selection
            // purposes. Other systems that decide to drop excess tools still use the carry
            // limit stat explicitly; they are unaffected by this change.
            return pawn.GetHeldSurvivalTools();
        }

        public static IEnumerable<Thing> GetAllUsableSurvivalTools(this Pawn pawn)
        {
            if (pawn == null) return Enumerable.Empty<Thing>();

            var eqTools = pawn.equipment?.GetDirectlyHeldThings().Where(t => t.def.IsSurvivalTool()) ?? Enumerable.Empty<Thing>();

            // Inventory tools (include all; no carry-limit truncation here).
            // Wrap tool-stuff into virtual tools so downstream code can treat them uniformly.
            IEnumerable<Thing> invRaw = Enumerable.Empty<Thing>();
            if (pawn.inventory?.innerContainer != null)
                invRaw = pawn.inventory.innerContainer.InnerListForReading; // use underlying list for perf

            var invTools = invRaw
                .Where(t => t != null && t.def != null && (t.def.IsSurvivalTool() || t.def.IsToolStuff()))
                .Select(t => t.def.IsToolStuff() ? (Thing)VirtualTool.FromThing(t) : t)
                .Where(t => t != null);

            return eqTools.Concat(invTools);
        }

        public static bool CanUseSurvivalTool(this Pawn pawn, ThingDef def)
        {
            var props = def?.GetModExtension<SurvivalToolProperties>();
            // Consider statBases as a valid source of tool stats (e.g., global work speed)
            bool hasStatBases = def?.statBases?.Any(sb => sb != null && sb.stat != null) == true;
            if (props?.baseWorkStatFactors == null && !hasStatBases)
            {
                if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"CanUseTool_NullProps_{def?.defName ?? "null"}"))
                    LogDecision($"CanUseTool_NullProps_{def?.defName ?? "null"}", $"[SurvivalTools] Tried to check if {def} is a usable tool but has null tool properties or work stat factors.");
                return false;
            }

            // Check explicit baseWorkStatFactors
            if (props?.baseWorkStatFactors != null)
            {
                foreach (var modifier in props.baseWorkStatFactors)
                    if (modifier?.stat?.Worker?.IsDisabledFor(pawn) == false)
                        return true;
            }

            // Fallback: check statBases on the def itself
            if (hasStatBases)
            {
                foreach (var sb in def.statBases)
                    if (sb?.stat?.Worker?.IsDisabledFor(pawn) == false)
                        return true;
            }

            return false;
        }

        #endregion

        #region Best tool selection / scoring

        public static IEnumerable<SurvivalTool> BestSurvivalToolsFor(Pawn pawn) =>
            SurvivalToolStats.Select(stat => pawn.GetBestSurvivalTool(stat)).Where(t => t != null);

        /// <summary>
        /// True if this tool is actively "used" by its holder for the current job's required stats.
        /// Handles both real tools and virtual wrappers (tool-stuff).
        /// </summary>
        public static bool IsToolInUse(SurvivalTool tool)
        {
            var holder = tool?.HoldingPawn;
            if (holder == null || !holder.CanUseSurvivalTools() || !holder.CanUseSurvivalTool(tool.def)) return false;
            var job = holder.CurJob?.def;
            if (job == null) return false;
            var req = StatsForJob(job, holder);
            if (req.NullOrEmpty()) return false;
            var toolStats = tool.WorkStatFactors.Select(m => m.stat).ToList();
            var relevant = req.Where(s => toolStats.Contains(s)).ToList();
            if (relevant.NullOrEmpty()) return false;

            // Compare the canonical backing Thing for equality so virtual wrappers match their
            // underlying physical item. This avoids false negatives where VirtualTool
            // instances are ephemeral and differ by reference.
            Thing toolBacking = BackingThing(tool, holder);
            foreach (var s in relevant)
            {
                var best = holder.GetBestSurvivalTool(s);
                if (best == null) continue;
                Thing bestBacking = BackingThing(best, holder);
                if (bestBacking != null && toolBacking != null)
                {
                    if (ReferenceEquals(bestBacking, toolBacking)) return true;
                }
                else
                {
                    // Fallback: compare by reference to handle real SurvivalTool instances
                    if (ReferenceEquals(best, tool)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Compute final work stat factors for a real SurvivalTool (def + stuff + effectiveness).
        /// </summary>
        public static IEnumerable<StatModifier> CalculateWorkStatFactors(SurvivalTool tool)
        {
            if (tool?.def == null) yield break;

            var tProps = SurvivalToolProperties.For(tool.def);
            var sProps = StuffPropsTool.For(tool.Stuff);
            float effectiveness = tool.GetStatValue(ST_StatDefOf.ToolEffectivenessFactor);
            // Apply quality-based multiplier if enabled in settings. We read CompQuality from the physical tool.
            if (SurvivalTools.Settings?.useQualityToolScaling ?? false)
            {
                try
                {
                    var comp = (tool as ThingWithComps)?.TryGetComp<CompQuality>();
                    QualityCategory qc = comp != null ? comp.Quality : QualityCategory.Normal;
                    float qualityFactor = ToolQualityCurve.Evaluate((int)qc);
                    effectiveness *= qualityFactor;
                }
                catch { /* best-effort */ }
            }

            if (tProps.baseWorkStatFactors == null) yield break;

            foreach (var baseModifier in tProps.baseWorkStatFactors)
            {
                if (baseModifier?.stat == null) continue;

                float finalFactor = CalculateFinalStatFactor(baseModifier, effectiveness, sProps, tProps, tool.Stuff);
                yield return new StatModifier { stat = baseModifier.stat, value = finalFactor };
            }
        }

        // (kept for possible legacy callers; not used by the function above)
        private static float CalculateFinalStatFactor(StatModifier baseModifier, float effectiveness, StuffPropsTool stuffProps)
        {
            float factor = baseModifier.value * effectiveness;
            if (stuffProps?.toolStatFactors != null)
            {
                var m = stuffProps.toolStatFactors.FirstOrDefault(x => x?.stat == baseModifier.stat);
                if (m != null) factor *= m.value;
            }
            return factor;
        }

        private static float CalculateFinalStatFactor(StatModifier baseModifier, float effectiveness, StuffPropsTool stuffProps, SurvivalToolProperties toolProps, ThingDef stuff)
        {
            float factor = baseModifier.value * effectiveness;

            // Stuff factors
            if (stuffProps?.toolStatFactors != null)
            {
                var m = stuffProps.toolStatFactors.FirstOrDefault(x => x?.stat == baseModifier.stat);
                if (m != null) factor *= m.value;
            }

            // Stuff power multiplier (if defined)
            if (toolProps?.stuffPowerMultiplier != null && stuff != null)
            {
                var mult = toolProps.stuffPowerMultiplier.FirstOrDefault(x => x?.stat == baseModifier.stat);
                if (mult != null)
                {
                    // Use an existing "power" stat as a proxy to scale
                    float stuffPower = stuff.GetStatValueAbstract(StatDefOf.StuffPower_Armor_Sharp);
                    factor *= (1f + (mult.value * (stuffPower - 1f)));
                }
            }

            return factor;
        }

        public static bool HasSurvivalTool(this Pawn pawn, ThingDef tool) =>
            pawn.GetHeldSurvivalTools().Any(t => t.def == tool);


        public static SurvivalTool GetBestSurvivalTool(this Pawn pawn, List<StatDef> stats)
        {
            if (!pawn.CanUseSurvivalTools() || stats.NullOrEmpty()) return null;

            var expectedKind = ToolUtility.ToolKindForStats(stats);
            SurvivalTool best = null;
            float bestScore = 0f;

            foreach (var thing in pawn.GetAllUsableSurvivalTools())
            {
                SurvivalTool cand = thing as SurvivalTool;
                if (cand == null)
                {
                    // Wrap tool-stuff or virtualizable items
                    if (thing.def != null && thing.def.IsToolStuff())
                        cand = VirtualTool.FromThing(thing);
                    else
                        cand = ToolUtility.TryWrapVirtual(thing);
                }
                if (cand == null) continue;

                if (expectedKind != STToolKind.None && ToolUtility.ToolKindOf(thing) != expectedKind) continue;
                if (!ToolImprovesAny(cand, stats)) continue;

#pragma warning disable CS0618 // Phase 3: Legacy method still used internally
                float score = ScoreToolForStats(cand, pawn, stats);
#pragma warning restore CS0618
                if (score > bestScore)
                {
                    bestScore = score;
                    best = cand;
                }
            }
            // Fallback: if expected kind filtering eliminated all candidates (best==null),
            // re-run without expectedKind restriction so we still display/recognize an
            // improving tool of a different kind (e.g. hammer improving ConstructionSpeed
            // when axe is beyond prior carry-limit ordering).
            if (best == null && expectedKind != STToolKind.None)
            {
                if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"BestTool_FallbackMulti_{pawn.ThingID}_{expectedKind}"))
                    LogDebug($"[SurvivalTools.Debug] Fallback (multi-stat) re-scan without expectedKind for {pawn.LabelShort} (expected {expectedKind}) stats=[{string.Join(",", stats.Select(s => s.defName))}]", $"Fallback_Multi_{pawn?.ThingID}_{expectedKind}");
                foreach (var thing in pawn.GetAllUsableSurvivalTools())
                {
                    SurvivalTool cand = thing as SurvivalTool;
                    if (cand == null)
                    {
                        if (thing.def != null && thing.def.IsToolStuff())
                            cand = VirtualTool.FromThing(thing);
                        else
                            cand = ToolUtility.TryWrapVirtual(thing);
                    }
                    if (cand == null) continue;
                    if (!ToolImprovesAny(cand, stats)) continue;
#pragma warning disable CS0618 // Phase 3: Legacy method still used internally
                    float score = ScoreToolForStats(cand, pawn, stats);
#pragma warning restore CS0618
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = cand;
                    }
                }
            }

            return best;
        }


        public static float GetStatFactorFromList(this SurvivalTool tool, StatDef stat) =>
            tool.WorkStatFactors.GetStatFactorFromList(stat);

        #endregion

        #region Tool availability / gating & degrade

        public static bool HasSurvivalToolFor(this Pawn pawn, StatDef stat)
        {
            if (pawn == null || stat == null || !stat.RequiresSurvivalTool()) return false;
            // Scan usable tools (inventory + worn + eq) once, check factor improvement.
            var tools = pawn.GetAllUsableSurvivalTools();
            if (tools == null) return false;
            float baseline = GetNoToolBaseline(stat);
            foreach (var thing in tools)
            {
                SurvivalTool st = thing as SurvivalTool;
                if (st == null && thing.def != null && thing.def.IsToolStuff()) st = VirtualTool.FromThing(thing);
                if (st == null) continue;
                if (GetToolProvidedFactor(st, stat) > baseline + 0.001f) return true;
            }
            return false;
        }

        /// <summary>
        /// Attempt to find a useful tool on the map/storage and enqueue a TakeInventory pickup job for the pawn.
        /// Returns true if a pickup job was enqueued.
        /// </summary>
        public static bool TryEnqueuePickupForMissingTool(Pawn pawn, List<StatDef> requiredStats)
        {
            try
            {
                if (pawn == null || requiredStats == null || requiredStats.Count == 0) return false;
                if (!(SurvivalTools.Settings?.autoTool ?? false)) return false;
                if (!pawn.CanUseSurvivalTools()) return false;

                // If pawn already has a helpful tool, nothing to do
                if (pawn.GetAllUsableSurvivalTools().OfType<SurvivalTool>().Any(st => ToolImprovesAny(st, requiredStats)))
                    return false;

                var map = pawn.Map;
                if (map == null) return false;

                SurvivalTool bestTool = null;
                float bestScore = 0f;
                var seen = new HashSet<int>();

                // 1) Radial scan around pawn (fast, preferred)
                const int SearchRadius = 28;
                foreach (var thing in GenRadial.RadialDistinctThingsAround(pawn.Position, map, SearchRadius, true))
                {
                    if (thing == null) continue;

                    SurvivalTool candidate = null;
                    if (thing is SurvivalTool st) candidate = st;
                    else if (thing.def != null && thing.def.IsToolStuff()) candidate = VirtualTool.FromThing(thing);
                    if (candidate == null) continue;

                    var backing = BackingThing(candidate, pawn) ?? (candidate as Thing);
                    if (backing == null) continue;
                    if (seen.Contains(backing.thingIDNumber)) continue;
                    seen.Add(backing.thingIDNumber);
                    if (!backing.Spawned || backing.Map != map) continue;
                    if (backing.IsForbidden(pawn)) continue;
                    if (!pawn.CanReserveAndReach(backing, PathEndMode.OnCell, pawn.NormalMaxDanger())) continue;
                    if (!ToolImprovesAny(candidate, requiredStats)) continue;

#pragma warning disable CS0618 // Phase 3: Legacy method still used internally
                    float score = ScoreToolForStats(candidate, pawn, requiredStats);
#pragma warning restore CS0618
                    score -= 0.01f * backing.Position.DistanceTo(pawn.Position);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTool = candidate;
                    }
                }

                // 2) Storage / haulable scans: storage-first, nearby, then wider fallback
                var candidates = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
                const int StorageSearchRadius = 80;
                const int FallbackSearchRadius = 200;

                // Storage items within StorageSearchRadius
                foreach (var item in candidates)
                {
                    if (item == null) continue;
                    if (!item.IsInAnyStorage()) continue;
                    if (item.Map != map) continue;
                    if (item.Position.DistanceTo(pawn.Position) > StorageSearchRadius) continue;
                    var backing = item;
                    if (seen.Contains(backing.thingIDNumber)) continue;
                    seen.Add(backing.thingIDNumber);

                    SurvivalTool candidate = null;
                    if (item is SurvivalTool st2) candidate = st2;
                    else if (item.def != null && item.def.IsToolStuff()) candidate = VirtualTool.FromThing(item);
                    if (candidate == null) continue;
                    if (!ToolImprovesAny(candidate, requiredStats)) continue;
                    if (backing.IsForbidden(pawn)) continue;
                    if (!pawn.CanReserveAndReach(backing, PathEndMode.OnCell, pawn.NormalMaxDanger())) continue;
#pragma warning disable CS0618 // Phase 3: Legacy method still used internally
                    float score = ScoreToolForStats(candidate, pawn, requiredStats);
#pragma warning restore CS0618
                    score -= 0.01f * backing.Position.DistanceTo(pawn.Position);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTool = candidate;
                    }
                }

                // Nearby haulable (within StorageSearchRadius)
                foreach (var item in candidates)
                {
                    if (item == null) continue;
                    if (item.IsInAnyStorage()) continue;
                    if (item.Map != map) continue;
                    if (item.Position.DistanceTo(pawn.Position) > StorageSearchRadius) continue;
                    var backing = item;
                    if (seen.Contains(backing.thingIDNumber)) continue;
                    seen.Add(backing.thingIDNumber);

                    SurvivalTool candidate = null;
                    if (item is SurvivalTool st2) candidate = st2;
                    else if (item.def != null && item.def.IsToolStuff()) candidate = VirtualTool.FromThing(item);
                    if (candidate == null) continue;
                    if (!ToolImprovesAny(candidate, requiredStats)) continue;
                    if (backing.IsForbidden(pawn)) continue;
                    if (!pawn.CanReserveAndReach(backing, PathEndMode.OnCell, pawn.NormalMaxDanger())) continue;
#pragma warning disable CS0618 // Phase 3: Legacy method still used internally
                    float score = ScoreToolForStats(candidate, pawn, requiredStats);
#pragma warning restore CS0618
                    score -= 0.01f * backing.Position.DistanceTo(pawn.Position);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTool = candidate;
                    }
                }

                // Wider-area fallback
                foreach (var item in candidates)
                {
                    if (item == null) continue;
                    if (item.Map != map) continue;
                    if (item.Position.DistanceTo(pawn.Position) > FallbackSearchRadius) continue;
                    var backing = item;
                    if (seen.Contains(backing.thingIDNumber)) continue;
                    seen.Add(backing.thingIDNumber);

                    SurvivalTool candidate = null;
                    if (item is SurvivalTool st2) candidate = st2;
                    else if (item.def != null && item.def.IsToolStuff()) candidate = VirtualTool.FromThing(item);
                    if (candidate == null) continue;
                    if (!ToolImprovesAny(candidate, requiredStats)) continue;
                    if (backing.IsForbidden(pawn)) continue;
                    if (!pawn.CanReserveAndReach(backing, PathEndMode.OnCell, pawn.NormalMaxDanger())) continue;
#pragma warning disable CS0618 // Phase 3: Legacy method still used internally
                    float score = ScoreToolForStats(candidate, pawn, requiredStats);
#pragma warning restore CS0618
                    score -= 0.01f * backing.Position.DistanceTo(pawn.Position);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTool = candidate;
                    }
                }

                if (bestTool == null) return false;

                var backingThing = BackingThing(bestTool, pawn) ?? (bestTool as Thing);
                if (backingThing == null || !backingThing.Spawned) return false;

                var pickupJob = JobMaker.MakeJob(JobDefOf.TakeInventory, backingThing);
                pickupJob.count = 1;
                try
                {
                    pawn.jobs.jobQueue.EnqueueFirst(pickupJob);
                    if (IsDebugLoggingEnabled)
                        LogDebug($"[SurvivalTools] Enqueued pickup for missing tool for {pawn.LabelShort}: target={backingThing.LabelCap}", $"AutoTool_Enqueue_{pawn.ThingID}");
                    return true;
                }
                catch { return false; }
            }
            catch { return false; }
        }

        public static bool HasSurvivalToolFor(this Pawn pawn, StatDef stat, out SurvivalTool tool, out float statFactor)
        {
            tool = null; statFactor = -1f;
            if (pawn == null || stat == null || !stat.RequiresSurvivalTool()) return false;
            float baseline = GetNoToolBaseline(stat);
            var tools = pawn.GetAllUsableSurvivalTools(); if (tools == null) return false;
            SurvivalTool best = null; float bestFactor = baseline;
            foreach (var thing in tools)
            {
                SurvivalTool st = thing as SurvivalTool;
                if (st == null && thing.def != null && thing.def.IsToolStuff()) st = VirtualTool.FromThing(thing);
                if (st == null) continue;
                float f = GetToolProvidedFactor(st, stat);
                if (f > bestFactor + 0.001f) { bestFactor = f; best = st; }
            }
            tool = best; statFactor = bestFactor;
            return best != null;
        }


        public static SurvivalTool GetBestSurvivalTool(this Pawn pawn, StatDef stat)
        {
            if (!pawn.CanUseSurvivalTools() || stat == null || !stat.RequiresSurvivalTool()) return null;
            var part = stat.GetStatPart<Stats.StatPart_SurvivalTools>();
            if (part == null) return null;

            // Unified baseline & scoring path using existing helpers so behavior matches multi-stat overload
            float baseline = GetNoToolBaseline(stat);
            var expectedKind = ToolUtility.ToolKindForStats(new[] { stat });
            SurvivalTool best = null; float bestDelta = 0f; // store delta above baseline not absolute factor

            foreach (var thing in pawn.GetAllUsableSurvivalTools())
            {
                SurvivalTool candidate = thing as SurvivalTool;
                if (candidate == null)
                {
                    // Wrap tool-stuff or other virtualizable items
                    if (thing.def != null && thing.def.IsToolStuff())
                        candidate = VirtualTool.FromThing(thing);
                    else
                        candidate = ToolUtility.TryWrapVirtual(thing);
                }
                if (candidate == null) continue;
                if (expectedKind != STToolKind.None && ToolUtility.ToolKindOf(thing) != expectedKind) continue;

                float provided = GetToolProvidedFactor(candidate, stat);
                float delta = provided - baseline;
                if (delta > bestDelta + 0.001f)
                {
                    bestDelta = delta;
                    best = candidate;
                }
            }

            // Fallback pass without expectedKind filtering if nothing selected.
            if (best == null && expectedKind != STToolKind.None)
            {
                if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"BestTool_FallbackSingle_{pawn.ThingID}_{expectedKind}_{stat.defName}"))
                    LogDebug($"[SurvivalTools.Debug] Fallback (single-stat) re-scan without expectedKind for {pawn.LabelShort} stat={stat.defName} expected={expectedKind}", $"Fallback_Single_{pawn?.ThingID}_{stat?.defName}_{expectedKind}");
                foreach (var thing in pawn.GetAllUsableSurvivalTools())
                {
                    SurvivalTool candidate = thing as SurvivalTool;
                    if (candidate == null)
                    {
                        if (thing.def != null && thing.def.IsToolStuff())
                            candidate = VirtualTool.FromThing(thing);
                        else
                            candidate = ToolUtility.TryWrapVirtual(thing);
                    }
                    if (candidate == null) continue;
                    float provided = GetToolProvidedFactor(candidate, stat);
                    float delta = provided - baseline;
                    if (delta > bestDelta + 0.001f)
                    {
                        bestDelta = delta;
                        best = candidate;
                    }
                }
            }

            if (best != null)
            {
                try { LessonAutoActivator.TeachOpportunity(ST_ConceptDefOf.UsingSurvivalTools, OpportunityType.Important); } catch { }
            }
            return best;
        }

        public static string GetSurvivalToolOverrideReportText(SurvivalTool tool, StatDef stat)
        {
            var statFactorList = tool.WorkStatFactors;
            var stuffPropsTool = tool.Stuff?.GetModExtension<StuffPropsTool>();

            var b = new StringBuilder();
            b.AppendLine(stat.description);
            b.AppendLine();

            b.AppendLine($"{tool.def.LabelCap}: {tool.def.GetModExtension<SurvivalToolProperties>().baseWorkStatFactors.GetStatFactorFromList(stat).ToStringByStyle(ToStringStyle.Integer, ToStringNumberSense.Factor)}");
            b.AppendLine();

            b.AppendLine($"{ST_StatDefOf.ToolEffectivenessFactor.LabelCap}: {tool.GetStatValue(ST_StatDefOf.ToolEffectivenessFactor).ToStringByStyle(ToStringStyle.Integer, ToStringNumberSense.Factor)}");

            if (stuffPropsTool != null && stuffPropsTool.toolStatFactors.GetStatFactorFromList(stat) != 1f)
            {
                b.AppendLine();
                b.AppendLine($"{"StatsReport_Material".Translate()} ({tool.Stuff.LabelCap}): {stuffPropsTool.toolStatFactors.GetStatFactorFromList(stat).ToStringByStyle(ToStringStyle.Integer, ToStringNumberSense.Factor)}");
            }

            b.AppendLine();
            b.AppendLine($"{"StatsReport_FinalValue".Translate()}: {statFactorList.ToList().GetStatFactorFromList(stat).ToStringByStyle(ToStringStyle.Integer, ToStringNumberSense.Factor)}");

            return b.ToString();
        }

        public static void TryDegradeTool(Pawn pawn, StatDef stat)
        {
            if (pawn == null || stat == null) return;

            var tool = pawn.GetBestSurvivalTool(stat);
            float toolFactor = tool != null ? tool.WorkStatFactors.ToList().GetStatFactorFromList(stat) : -1f;
            LogDebug($"TryDegradeTool: pawn={pawn?.LabelShort ?? "null"} stat={stat.defName} bestTool={(tool != null ? tool.LabelCapNoCount : "null")} bestFactor={toolFactor}", $"TryDegrade_{pawn?.ThingID ?? "null"}_{stat.defName}");
            if (tool == null || !IsToolDegradationEnabled) return;

            try
            {
                var backing = BackingThing(tool, pawn) ?? (tool as Thing);
                if (tool is VirtualTool vt && vt.SourceThing != null)
                    backing = vt.SourceThing;

                LessonAutoActivator.TeachOpportunity(ST_ConceptDefOf.SurvivalToolDegradation, OpportunityType.GoodToKnow);

                if (backing is SurvivalTool realTool && realTool.def.useHitPoints)
                {
                    realTool.workTicksDone++;
                    if (realTool.workTicksDone >= realTool.WorkTicksToDegrade)
                    {
                        realTool.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, 1));
                        realTool.workTicksDone = 0;
                    }
                    return;
                }

                if (backing is ThingWithComps twc && backing.def.useHitPoints)
                {
                    int key = backing.thingIDNumber;
                    if (!_virtualToolDegradeCounters.TryGetValue(key, out var counter)) counter = 0;
                    counter++;
                    _virtualToolDegradeCounters[key] = counter;
                    float lifespanDays = tool.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan);
                    int hp = Math.Max(1, twc.MaxHitPoints);
                    int threshold = Math.Max(1, (int)Math.Floor((lifespanDays * GenDate.TicksPerDay) / hp));
                    if (counter >= threshold)
                    {
                        twc.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, 1));
                        _virtualToolDegradeCounters[key] = 0;
                    }
                    return;
                }

                if (backing != null && backing.stackCount > 0)
                {
                    int key = backing.thingIDNumber;
                    if (!_virtualToolDegradeCounters.TryGetValue(key, out var counter)) counter = 0;
                    counter++;
                    float factor = SurvivalTools.Settings?.EffectiveToolDegradationFactor ?? 1f;
                    int threshold = Math.Max(1, (int)Math.Floor(100f / Math.Max(0.001f, factor)));
                    _virtualToolDegradeCounters[key] = counter;
                    if (counter >= threshold)
                    {
                        var one = backing.SplitOff(1);
                        one.Destroy(DestroyMode.Vanish);
                        _virtualToolDegradeCounters[key] = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                if (IsDebugLoggingEnabled)
                {
                    try { LogDebug($"TryDegradeTool exception: {ex}", $"TryDegrade_{pawn?.ThingID ?? "null"}_{stat?.defName ?? "null"}"); } catch { }
                }
            }
        }

        #endregion

        #region Work & job logic (gating / alerts)

        public static bool MeetsWorkGiverStatRequirements(this Pawn pawn, List<StatDef> requiredStats) =>
    pawn.MeetsWorkGiverStatRequirements(requiredStats, null, null);

        public static bool MeetsWorkGiverStatRequirements(this Pawn pawn, List<StatDef> requiredStats, WorkGiverDef workGiver = null, JobDef jobDef = null)
        {
            if (requiredStats.NullOrEmpty()) return true;

            var s = SurvivalTools.Settings;
            if (s != null && s.hardcoreMode)
            {
                var toolStats = requiredStats.Where(st => st != null && st.RequiresSurvivalTool()).ToList();
                if (toolStats.NullOrEmpty()) return true;

                foreach (var stat in toolStats)
                {
                    // Unified gating check (uses StatGatingHelper)
                    if (StatGatingHelper.ShouldBlockJobForStat(stat, s, pawn))
                    {
                        string logKey = $"Missing_Tool_{pawn.ThingID}_{stat.defName}";
                        if (ShouldLog(logKey))
                        {
                            string statCategory = GetStatCategoryDescription(stat);
                            string ctx = GetJobContextDescription(workGiver, jobDef);
                            LogInfoOnce($"[SurvivalTools] {pawn.LabelShort} cannot start job: missing required tool for {statCategory} stat {stat.defName}{ctx}", $"NoTool_StartJob_{pawn?.ThingID}_{stat?.defName}");
                        }
                        return false;
                    }
                }
                return true;
            }

            // Normal mode fallback — still reject if stat value is zero or less
            foreach (var stat in requiredStats)
            {
                if (stat != null)
                {
                    float v = pawn.GetStatValue(stat);
                    if (v <= 0f)
                    {
                        LogDebug(
                            $"MeetsWorkGiverStatRequirements: pawn={pawn?.LabelShort ?? "null"} stat={stat.defName} value={v} -> FAIL (<=0)",
                            $"MeetsWG_{pawn?.ThingID ?? "null"}_{stat?.defName ?? "null"}"
                        );
                        return false;
                    }
                }
            }

            return true;
        }
        public static bool CanFellTrees(this Pawn pawn)
        {
            var fellWG = ST_WorkGiverDefOf.FellTrees;
            var req = fellWG?.GetModExtension<WorkGiverExtension>()?.requiredStats;
            if (req == null || req.Count == 0) return true;
            return pawn.MeetsWorkGiverStatRequirements(req);
        }

        public static IEnumerable<WorkGiver> AssignedToolRelevantWorkGivers(this Pawn pawn)
        {
            if (pawn.workSettings == null)
            {
                if (IsDebugLoggingEnabled)
                    Log.ErrorOnce($"Tried to get tool-relevant work givers for {pawn} but has null workSettings", 11227);
                yield break;
            }

            foreach (var giver in pawn.workSettings.WorkGiversInOrderNormal)
            {
                var ext = giver.def.GetModExtension<WorkGiverExtension>();
                if (ext?.requiredStats?.Any(s => s.RequiresSurvivalTool()) == true)
                    yield return giver;
            }
        }

        public static List<StatDef> AssignedToolRelevantWorkGiversStatDefs(this Pawn pawn)
        {
            var all = pawn.AssignedToolRelevantWorkGivers()
                .SelectMany(g => g.def.GetModExtension<WorkGiverExtension>().requiredStats)
                .Where(s => s != null)
                .Distinct()
                .ToList();

            // Consider WorkSpeedGlobal: if pawn has any assigned WorkGiver that maps to WorkSpeedGlobal
            // and the per-job gating setting is enabled, include WorkSpeedGlobal in the relevant stats.
            try
            {
                var settings = SurvivalTools.Settings;
                if (settings != null && pawn?.workSettings != null)
                {
                    foreach (var wg in WorkSpeedGlobalHelper.GetWorkSpeedGlobalJobs())
                    {
                        // Check pawn has this workgiver (in normal order list) and gating enabled for that job
                        bool pawnHasWg = pawn.workSettings.WorkGiversInOrderNormal.Any(wgInst => wgInst?.def == wg);
                        if (pawnHasWg && settings.workSpeedGlobalJobGating.GetValueOrDefault(wg.defName, true))
                        {
                            if (!all.Contains(ST_StatDefOf.WorkSpeedGlobal))
                                all.Add(ST_StatDefOf.WorkSpeedGlobal);
                            break;
                        }
                    }
                }
            }
            catch { /* best-effort: do not break callers on error */ }

            // Only those we can actually satisfy with tools available in this game
            return FilterStatsWithAvailableTools(all);
        }

        /// <summary>
        /// For alerts: include research/cleaning/butchery/etc., but still ensure tools exist in this run.
        /// </summary>
        public static List<StatDef> AssignedToolRelevantWorkGiversStatDefsForAlerts(this Pawn pawn)
        {
            var all = pawn.AssignedToolRelevantWorkGivers()
                .SelectMany(g => g.def.GetModExtension<WorkGiverExtension>().requiredStats)
                .Where(s => s != null)
                .Distinct()
                .ToList();

            return all.Where(ToolsExistForStat).ToList();
        }

        public static bool NeedsSurvivalTool(this Pawn pawn, SurvivalTool tool)
        {
            var relevant = pawn.AssignedToolRelevantWorkGiversStatDefs();
            return tool.WorkStatFactors.Any(f => relevant.Contains(f.stat));
        }

        public static bool BetterThanWorkingToollessFor(this SurvivalTool tool, StatDef stat)
        {
            var part = stat.GetStatPart<Stats.StatPart_SurvivalTools>();
            if (part == null)
            {
                if (IsDebugLoggingEnabled)
                    Log.ErrorOnce($"Tried to check if {tool} is better than working toolless for {stat} which has no StatPart_SurvivalTools", 8120196);
                return false;
            }
            float toolFactor = tool.WorkStatFactors.ToList().GetStatFactorFromList(stat);
            float noToolPenalty = ToolFactorCache.GetOrComputeNoToolPenalty(stat);
            return toolFactor > noToolPenalty;
        }

        #endregion

        #region Extension utils

        /// <summary>
        /// Get the factor from a modifier list or 1.0 if absent (neutral).
        /// </summary>
        public static float GetStatFactorFromList(this IEnumerable<StatModifier> modifiers, StatDef stat)
        {
            if (modifiers == null || stat == null) return 1.0f;
            var m = modifiers.FirstOrDefault(x => x.stat == stat);
            return m?.value ?? 1.0f;
        }

        #endregion

        #region Storage & hauling

        public static Job DequipAndTryStoreSurvivalTool(this Pawn pawn, Thing tool, bool enqueueCurrent = true)
        {
            if (pawn.CurJob != null && enqueueCurrent)
                pawn.jobs.jobQueue.EnqueueFirst(pawn.CurJob);

            // If we got a virtual wrapper, target the real thing for vanilla hauling/storage.
            Thing realThing = tool;
            if (tool is SurvivalTool st)
                realThing = BackingThing(st, pawn) ?? tool;

            if (StoreUtility.TryFindBestBetterStoreCellFor(realThing, pawn, pawn.MapHeld, StoreUtility.CurrentStoragePriorityOf(realThing), pawn.Faction, out IntVec3 c))
            {
                var haulJob = new Job(JobDefOf.HaulToCell, realThing, c) { count = 1 };
                // Invalidate cache for the moved tool so any precomputed factors that depend on
                // ownership/location/stuff are refreshed when needed.
                try { ToolFactorCache.InvalidateForThing(realThing); } catch { }
                pawn.jobs.jobQueue.EnqueueFirst(haulJob);
            }

            // Drop job targets the physical thing.
            return new Job(ST_JobDefOf.DropSurvivalTool, realThing);
        }

        public static bool CanRemoveExcessSurvivalTools(this Pawn pawn) =>
            !pawn.Drafted && !pawn.IsWashing() && !pawn.IsFormingCaravan() && !pawn.IsCaravanMember() &&
            pawn.CurJobDef?.casualInterruptible != false &&
            !pawn.IsBurning() && !(pawn.carryTracker?.CarriedThing is SurvivalTool);

        private static bool IsWashing(this Pawn pawn) =>
            ModCompatibilityCheck.DubsBadHygiene && pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("Washing"));

        public static string GetStatCategoryDescription(StatDef stat)
        {
            if (stat == ST_StatDefOf.MedicalOperationSpeed || stat == ST_StatDefOf.MedicalSurgerySuccessChance) return "medical";
            if (stat == ST_StatDefOf.ButcheryFleshSpeed || stat == ST_StatDefOf.ButcheryFleshEfficiency) return "butchery";
            if (stat == ST_StatDefOf.CleaningSpeed) return "cleaning";
            if (stat == ST_StatDefOf.ResearchSpeed) return "research";
            if (stat == StatDefOf.ConstructionSpeed) return "construction";
            if (stat == StatDefOf.MiningSpeed) return "mining";
            if (stat == StatDefOf.PlantWorkSpeed) return "plant work";
            if (stat == ST_StatDefOf.TreeFellingSpeed) return "tree felling";
            return "tool";
        }

        private static string GetJobContextDescription(WorkGiverDef workGiver, JobDef jobDef)
        {
            if (workGiver != null && jobDef != null) return $" (workGiver: {workGiver.defName}, job: {jobDef.defName})";
            if (workGiver != null) return $" (workGiver: {workGiver.defName})";
            if (jobDef != null) return $" (job: {jobDef.defName})";
            return "";
        }

        /// <summary>
        /// True if at least one loaded thing provides the specified stat via SurvivalToolProperties.
        /// </summary>
        public static bool ToolsExistForStat(StatDef stat)
        {
            if (stat == null) return false;

            foreach (var thingDef in DefDatabase<ThingDef>.AllDefs)
            {
                var toolProps = thingDef.GetModExtension<SurvivalToolProperties>();
                // Check mod extension baseWorkStatFactors
                if (toolProps?.baseWorkStatFactors?.Any(f => f?.stat == stat) == true)
                    return true;
                // Also check statBases on the def (for stats like WorkSpeedGlobal)
                if (thingDef.statBases?.Any(sb => sb != null && sb.stat == stat) == true)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Filters out stats that have no available tools in this run (prevents pointless alerts).
        /// </summary>
        public static List<StatDef> FilterStatsWithAvailableTools(IEnumerable<StatDef> stats) =>
            stats?.Where(ToolsExistForStat).ToList() ?? new List<StatDef>();

        #endregion

    }
}
