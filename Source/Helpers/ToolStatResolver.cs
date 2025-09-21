// RimWorld 1.6 / C# 7.3
// Source/Helpers/ToolStatResolver.cs
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SurvivalTools.Helpers
{
    /// <summary>
    /// Phase 2: Centralized tool stat resolver with hardened cataloging and caching.
    /// Replaces scattered stat inference logic with a single source of truth.
    /// </summary>
    public static class ToolStatResolver
    {
        // Cache per (toolDef, stuffDef, stat) factors for O(1) lookups
        private static readonly Dictionary<string, float> _factorCache = new Dictionary<string, float>();
        private static readonly Dictionary<string, ToolStatInfo> _toolStatCache = new Dictionary<string, ToolStatInfo>();

        // Work stats registry for intersection detection
        private static HashSet<StatDef> _registeredWorkStats;
        private static bool _initialized = false;

        /// <summary>
        /// Get the set of registered work stats (lazy initialized)
        /// </summary>
        private static HashSet<StatDef> RegisteredWorkStats
        {
            get
            {
                if (_registeredWorkStats == null)
                {
                    _registeredWorkStats = new HashSet<StatDef>
                    {
                        ST_StatDefOf.DiggingSpeed,
                        ST_StatDefOf.MiningYieldDigging,
                        ST_StatDefOf.PlantHarvestingSpeed,
                        ST_StatDefOf.SowingSpeed,
                        ST_StatDefOf.TreeFellingSpeed,
                        ST_StatDefOf.MaintenanceSpeed,
                        ST_StatDefOf.DeconstructionSpeed,
                        ST_StatDefOf.ResearchSpeed,
                        ST_StatDefOf.CleaningSpeed,
                        ST_StatDefOf.MedicalOperationSpeed,
                        ST_StatDefOf.MedicalSurgerySuccessChance,
                        ST_StatDefOf.ButcheryFleshSpeed,
                        ST_StatDefOf.ButcheryFleshEfficiency,
                        StatDefOf.ConstructionSpeed
                    };

                    // Add work speed global if available
                    if (ST_StatDefOf.WorkSpeedGlobal != null)
                        _registeredWorkStats.Add(ST_StatDefOf.WorkSpeedGlobal);
                }
                return _registeredWorkStats;
            }
        }

        // Tool quirks registry with deterministic ordering
        private static readonly List<ToolQuirk> _toolQuirks = new List<ToolQuirk>();
        private static int _quirkSequence = 0;

        // Resolver version stamp - increments on catalog/quirk changes for cache invalidation
        private static int _version = 0;

        /// <summary>
        /// Tool quirk definition with deterministic ordering
        /// </summary>
        private struct ToolQuirk
        {
            public int Sequence;
            public Func<ThingDef, bool> Predicate;
            public Action<ToolQuirkApplier> Action;
        }

        /// <summary>
        /// Tool stat information for caching and display
        /// </summary>
        public class ToolStatInfo
        {
            public ThingDef ToolDef { get; set; }
            public ThingDef StuffDef { get; set; }
            public StatDef Stat { get; set; }
            public float Factor { get; set; }
            public string Source { get; set; } // "Explicit", "StatBases", "NameHint", "Default"
            public bool IsClamped { get; set; }
            public string QuirkSummary { get; set; } // Applied quirk modifications (built once)

            // Pre-allocated lightweight tags list for quirk tracking
            internal List<string> QuirkTags { get; set; }

            public ToolStatInfo()
            {
                QuirkTags = new List<string>();
            }
        }

        /// <summary>
        /// Current resolver version stamp. Increments when catalog or quirks change.
        /// Used for cache invalidation to prevent stale data.
        /// 
        /// Integration note: Future ScoreCache systems should include this version
        /// in cache keys alongside (pawn, tool, difficultySeed) to ensure scores
        /// are invalidated when tool stat resolution changes.
        /// </summary>
        public static int Version => _version;

        /// <summary>
        /// Register a tool quirk with predicate and action (INTERNAL USE ONLY).
        /// Quirks are processed in registration order for deterministic behavior.
        /// </summary>
        /// <param name="predicate">Test if this quirk applies to a tool def</param>
        /// <param name="action">Apply quirk modifications</param>
        internal static void RegisterQuirk(Func<ThingDef, bool> predicate, Action<ToolQuirkApplier> action)
        {
            if (predicate == null || action == null) return;
            _toolQuirks.Add(new ToolQuirk
            {
                Sequence = ++_quirkSequence,
                Predicate = predicate,
                Action = action
            });

            // Bump version to invalidate dependent caches
            ++_version;
        }

        /// <summary>
        /// Initialize the resolver with registered work stats
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            // Build set of registered work stats for intersection detection (lazy init)
            // _registeredWorkStats will be initialized on first access

            _initialized = true;

            // Bump version on (re)build to invalidate dependent caches
            ++_version;
        }

        /// <summary>
        /// Get tool stat factor using hardened hierarchy:
        /// 1. Explicit tool tags/properties
        /// 2. Intersect statBases with registered work stats  
        /// 3. Name/verb hints fallback
        /// 4. Safe defaults
        /// </summary>
        public static float GetToolStatFactor(ThingDef toolDef, ThingDef stuffDef, StatDef stat)
        {
            if (!_initialized) Initialize();
            if (toolDef == null || stat == null) return GetNoToolBaseline();

            string cacheKey = $"{toolDef.defName}|{stuffDef?.defName ?? "null"}|{stat.defName}";
            if (_factorCache.TryGetValue(cacheKey, out float cachedFactor))
                return cachedFactor;

            var info = ResolveToolStatInfo(toolDef, stuffDef, stat);
            float factor = info.Factor;

            // Clamp: material beats "no tool" baseline on Normal
            var settings = SurvivalTools.Settings;
            if (settings != null && !settings.hardcoreMode && !settings.extraHardcoreMode)
            {
                float baseline = GetNoToolBaseline();
                if (factor < baseline)
                {
                    factor = baseline;
                    info.IsClamped = true;
                }
            }

            _factorCache[cacheKey] = factor;
            _toolStatCache[cacheKey] = info;
            return factor;
        }

        /// <summary>
        /// Get detailed tool stat information for SpecialDisplayStats
        /// </summary>
        public static ToolStatInfo GetToolStatInfo(ThingDef toolDef, ThingDef stuffDef, StatDef stat)
        {
            if (!_initialized) Initialize();

            string cacheKey = $"{toolDef.defName}|{stuffDef?.defName ?? "null"}|{stat.defName}";
            if (_toolStatCache.TryGetValue(cacheKey, out var cached))
                return cached;

            // Trigger factor calculation which populates cache
            GetToolStatFactor(toolDef, stuffDef, stat);
            return _toolStatCache.TryGetValue(cacheKey, out var info) ? info : CreateDefaultInfo(toolDef, stuffDef, stat);
        }

        /// <summary>
        /// Resolve tool stat info using the hierarchy
        /// </summary>
        private static ToolStatInfo ResolveToolStatInfo(ThingDef toolDef, ThingDef stuffDef, StatDef stat)
        {
            ToolStatInfo info;

            // 1. Explicit tool tags/properties (highest priority)
            var explicitFactor = TryGetExplicitFactor(toolDef, stuffDef, stat);
            if (explicitFactor.HasValue)
            {
                info = new ToolStatInfo
                {
                    ToolDef = toolDef,
                    StuffDef = stuffDef,
                    Stat = stat,
                    Factor = explicitFactor.Value,
                    Source = "Explicit",
                    IsClamped = false
                };
            }
            // 2. Intersect statBases with registered work stats
            else
            {
                var statBasesFactor = TryGetStatBasesFactor(toolDef, stuffDef, stat);
                if (statBasesFactor.HasValue)
                {
                    info = new ToolStatInfo
                    {
                        ToolDef = toolDef,
                        StuffDef = stuffDef,
                        Stat = stat,
                        Factor = statBasesFactor.Value,
                        Source = "StatBases",
                        IsClamped = false
                    };
                }
                // 3. Name/verb hints fallback
                else
                {
                    var hintFactor = TryGetNameHintFactor(toolDef, stat);
                    if (hintFactor.HasValue)
                    {
                        info = new ToolStatInfo
                        {
                            ToolDef = toolDef,
                            StuffDef = stuffDef,
                            Stat = stat,
                            Factor = hintFactor.Value,
                            Source = "NameHint",
                            IsClamped = false
                        };
                    }
                    // 4. Safe default
                    else
                    {
                        info = CreateDefaultInfo(toolDef, stuffDef, stat);
                    }
                }
            }

            // Apply tool quirks after inference but before clamping
            ApplyToolQuirks(info);

            return info;
        }

        /// <summary>
        /// Apply registered tool quirks to the tool stat info
        /// </summary>
        private static void ApplyToolQuirks(ToolStatInfo info)
        {
            if (info == null || info.ToolDef == null || _toolQuirks.Count == 0)
                return;

            var applier = new ToolQuirkApplier(info);

            foreach (var quirk in _toolQuirks)
            {
                try
                {
                    if (quirk.Predicate(info.ToolDef))
                    {
                        quirk.Action(applier);
                    }
                }
                catch (Exception e)
                {
                    Log.Warning($"[SurvivalTools] Tool quirk failed for {info.ToolDef.defName}: {e.Message}");
                }
            }

            // Build quirk summary once after all quirks are applied
            info.QuirkSummary = info.QuirkTags.Count > 0 ? string.Join(", ", info.QuirkTags) : null;
        }

        /// <summary>
        /// Try to get explicit factor from SurvivalToolProperties or StuffPropsTool
        /// </summary>
        private static float? TryGetExplicitFactor(ThingDef toolDef, ThingDef stuffDef, StatDef stat)
        {
            // Check tool's SurvivalToolProperties first (highest priority)
            var toolProps = toolDef.GetModExtension<SurvivalToolProperties>();
            if (toolProps?.baseWorkStatFactors != null)
            {
                foreach (var modifier in toolProps.baseWorkStatFactors)
                {
                    if (modifier?.stat == stat)
                        return modifier.value;
                }
            }

            // Check if tool naturally affects this stat through tool properties or name hints
            bool toolNaturallyAffectsStat = DoesToolNaturallyAffectStat(toolDef, stat);

            // Check stuff's StuffPropsTool - only apply if tool naturally affects this stat
            if (stuffDef != null && toolNaturallyAffectsStat)
            {
                var stuffProps = stuffDef.GetModExtension<StuffPropsTool>();
                if (stuffProps?.toolStatFactors != null)
                {
                    foreach (var modifier in stuffProps.toolStatFactors)
                    {
                        if (modifier?.stat == stat)
                        {
                            // Apply stuff factor as multiplier to tool's natural capability
                            var baseFactor = GetToolNaturalFactor(toolDef, stat) ?? 1.0f;
                            return baseFactor * modifier.value;
                        }
                    }
                }

                // Check stuff's SurvivalToolProperties (for cloth/materials)
                var stuffToolProps = stuffDef.GetModExtension<SurvivalToolProperties>();
                if (stuffToolProps?.baseWorkStatFactors != null)
                {
                    foreach (var modifier in stuffToolProps.baseWorkStatFactors)
                    {
                        if (modifier?.stat == stat)
                        {
                            var baseFactor = GetToolNaturalFactor(toolDef, stat) ?? 1.0f;
                            return baseFactor * modifier.value;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Check if a tool naturally affects a stat (through tool properties or name hints)
        /// </summary>
        private static bool DoesToolNaturallyAffectStat(ThingDef toolDef, StatDef stat)
        {
            // Check tool's explicit properties
            var toolProps = toolDef.GetModExtension<SurvivalToolProperties>();
            if (toolProps?.baseWorkStatFactors != null)
            {
                foreach (var modifier in toolProps.baseWorkStatFactors)
                {
                    if (modifier?.stat == stat)
                        return true;
                }
            }

            // Check statBases
            if (toolDef.statBases != null)
            {
                foreach (var modifier in toolDef.statBases)
                {
                    if (modifier?.stat == stat)
                        return true;
                }
            }

            // Check name hints
            return TryGetNameHintFactor(toolDef, stat).HasValue;
        }

        /// <summary>
        /// Get the tool's natural factor for a stat (before stuff multipliers)
        /// </summary>
        private static float? GetToolNaturalFactor(ThingDef toolDef, StatDef stat)
        {
            // Check tool's explicit properties
            var toolProps = toolDef.GetModExtension<SurvivalToolProperties>();
            if (toolProps?.baseWorkStatFactors != null)
            {
                foreach (var modifier in toolProps.baseWorkStatFactors)
                {
                    if (modifier?.stat == stat)
                        return modifier.value;
                }
            }

            // Check statBases
            if (toolDef.statBases != null)
            {
                foreach (var modifier in toolDef.statBases)
                {
                    if (modifier?.stat == stat)
                        return modifier.value;
                }
            }

            // Check name hints
            return TryGetNameHintFactor(toolDef, stat);
        }

        /// <summary>
        /// Try to get factor from statBases intersection with registered work stats
        /// </summary>
        private static float? TryGetStatBasesFactor(ThingDef toolDef, ThingDef stuffDef, StatDef stat)
        {
            if (!RegisteredWorkStats.Contains(stat))
                return null;

            // Check tool's statBases
            if (toolDef.statBases != null)
            {
                foreach (var modifier in toolDef.statBases)
                {
                    if (modifier?.stat == stat)
                        return modifier.value;
                }
            }

            // Check stuff's statBases (if applicable)
            if (stuffDef?.statBases != null)
            {
                foreach (var modifier in stuffDef.statBases)
                {
                    if (modifier?.stat == stat)
                        return modifier.value;
                }
            }

            return null;
        }

        /// <summary>
        /// Try to get factor from name/verb hints (fallback)
        /// </summary>
        private static float? TryGetNameHintFactor(ThingDef toolDef, StatDef stat)
        {
            if (string.IsNullOrEmpty(toolDef.label))
                return null;

            string label = toolDef.label.ToLowerInvariant();
            float techMultiplier = GetTechLevelMultiplier(toolDef.techLevel);

            // Mining tools
            if (stat == ST_StatDefOf.DiggingSpeed || stat == ST_StatDefOf.MiningYieldDigging)
            {
                if (label.Contains("pickaxe") || label.Contains("pick") || label.Contains("mattock"))
                    return techMultiplier;
            }

            // Tree felling
            if (stat == ST_StatDefOf.TreeFellingSpeed)
            {
                if (label.Contains("axe") || label.Contains("hatchet"))
                    return techMultiplier;
            }

            // Plant work
            if (stat == ST_StatDefOf.PlantHarvestingSpeed)
            {
                if (label.Contains("sickle") || label.Contains("scythe"))
                    return techMultiplier;
            }

            if (stat == ST_StatDefOf.SowingSpeed)
            {
                if (label.Contains("hoe") || label.Contains("cultivator"))
                    return techMultiplier;
            }

            // Construction/maintenance
            if (stat == StatDefOf.ConstructionSpeed || stat == ST_StatDefOf.MaintenanceSpeed)
            {
                if (label.Contains("hammer") || label.Contains("mallet"))
                    return techMultiplier;
            }

            // Deconstruction
            if (stat == ST_StatDefOf.DeconstructionSpeed)
            {
                if (label.Contains("wrench") || label.Contains("crowbar") || label.Contains("prybar"))
                    return techMultiplier;
            }

            // Butchery
            if (stat == ST_StatDefOf.ButcheryFleshSpeed || stat == ST_StatDefOf.ButcheryFleshEfficiency)
            {
                if (label.Contains("knife") || label.Contains("blade"))
                    return techMultiplier;
            }

            return null;
        }

        /// <summary>
        /// Get tech level multiplier for name hint fallbacks
        /// </summary>
        private static float GetTechLevelMultiplier(TechLevel techLevel)
        {
            switch (techLevel)
            {
                case TechLevel.Neolithic: return 0.75f;
                case TechLevel.Medieval: return 0.85f;
                case TechLevel.Industrial: return 1.0f;
                case TechLevel.Spacer: return 1.15f;
                case TechLevel.Ultra: return 1.3f;
                default: return 0.85f;
            }
        }

        /// <summary>
        /// Get the "no tool" baseline factor
        /// </summary>
        private static float GetNoToolBaseline()
        {
            var settings = SurvivalTools.Settings;
            return settings?.noToolStatFactorNormal ?? 0.4f;
        }

        /// <summary>
        /// Create default info when no other resolution works.
        /// Tools that don't explicitly affect a stat should be neutral (1.0f), not penalized.
        /// The "no tool baseline" only applies when there's literally no tool equipped.
        /// </summary>
        private static ToolStatInfo CreateDefaultInfo(ThingDef toolDef, ThingDef stuffDef, StatDef stat)
        {
            return new ToolStatInfo
            {
                ToolDef = toolDef,
                StuffDef = stuffDef,
                Stat = stat,
                Factor = 1.0f, // Neutral - tool doesn't affect this stat
                Source = "Default",
                IsClamped = false
            };
        }

        /// <summary>
        /// Clear caches (for debug/reload scenarios)
        /// </summary>
        public static void ClearCaches()
        {
            _factorCache.Clear();
            _toolStatCache.Clear();
        }

        /// <summary>
        /// Clear all registered quirks (for debug/reload scenarios)
        /// </summary>
        public static void ClearQuirks()
        {
            _toolQuirks.Clear();
            _quirkSequence = 0;

            // Bump version to invalidate dependent caches
            ++_version;
        }

        /// <summary>
        /// Get all cached tool stat infos for debugging
        /// </summary>
        public static IEnumerable<ToolStatInfo> GetAllCachedInfos()
        {
            return _toolStatCache.Values;
        }

        /// <summary>
        /// Get number of registered quirks for debugging
        /// </summary>
        public static int GetQuirkCount()
        {
            return _toolQuirks.Count;
        }

        /// <summary>
        /// Get filtered tool candidates (real tools + virtual textiles/wood).
        /// Excludes buildings/turrets/mortars that were incorrectly evaluated as tools.
        /// Used for debug dumps and future Phase 3 scoring enumeration.
        /// 
        /// Includes:
        /// - Real tools: equipment, weapons, anything with tools/CompEquippable
        /// - Virtual materials: textiles/leather/wood that can become virtual tools
        /// 
        /// Excludes:
        /// - Buildings: turrets, mortars, workbenches, etc.
        /// - Non-tool items: food, apparel, etc.
        /// </summary>
        public static IEnumerable<ThingDef> GetToolCandidates()
        {
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def == null) continue;

                // Exclude buildings (turrets, mortars, foam sprayers, etc.)
                if (def.category == ThingCategory.Building || def.building != null)
                    continue;

                // Include real tools if ANY condition is true
                bool isRealTool = def.equipmentType != EquipmentType.None ||
                                  def.HasComp(typeof(CompEquippable)) ||
                                  def.IsWeapon ||
                                  def.IsMeleeWeapon ||
                                  (def.tools != null && def.tools.Count > 0);

                if (isRealTool)
                {
                    yield return def;
                    continue;
                }

                // Include virtual-only resources (textiles, wood)
                // These can be crafted into virtual tools via SurvivalToolUtility
                if (def.stuffProps?.categories != null)
                {
                    bool isVirtualCandidate = def.stuffProps.categories.Contains(StuffCategoryDefOf.Fabric) ||
                                              def.stuffProps.categories.Contains(StuffCategoryDefOf.Leathery) ||
                                              def.stuffProps.categories.Contains(StuffCategoryDefOf.Woody);

                    if (isVirtualCandidate)
                    {
                        yield return def;
                    }
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Kill List (Phase 3 Preparation):
    // - Replace scattered tool stat inference across multiple files:
    //   - SurvivalToolUtility.GetStatFactor()
    //   - ToolUtility.GetStatFactors() 
    //   - StatWorker_* overrides
    //   - Duplicated stat calculations in various classes
    // - Consolidate all tool stat queries through ToolStatResolver
    // - Remove obsolete helper methods once all callers updated
    // - Use GetToolCandidates() for all tool enumeration instead of raw DefDatabase queries
    // - Ensure all scoring systems use filtered candidates (excludes buildings/turrets)
    // -------------------------------------------------------------------------
}