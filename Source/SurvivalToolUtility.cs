// RW 1.6 / C# 7.3
// Source/SurvivalToolUtility.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace SurvivalTools
{
    public static class SurvivalToolUtility
    {
        #region Constants & fields

        public static readonly FloatRange MapGenToolHitPointsRange = new FloatRange(0.3f, 0.7f);
        public const float MapGenToolMaxStuffMarketValue = 3f;

        private static bool? _debugLoggingCache;

        // Per-pawn, per-job debug log gating
        private static readonly Dictionary<Pawn, HashSet<JobDef>> _loggedJobsPerPawn = new Dictionary<Pawn, HashSet<JobDef>>();

        // General cooldown keys (to suppress spam)
        private static readonly Dictionary<string, int> _lastLoggedTick = new Dictionary<string, int>();
        private const int LOG_COOLDOWN_TICKS = 2500; // ~1 in-game hour

        #endregion

        #region Settings toggles (fast access)

        public static bool IsDebugLoggingEnabled
        {
            get
            {
                if (_debugLoggingCache == null)
                    _debugLoggingCache = SurvivalTools.Settings?.debugLogging ?? false;
                return _debugLoggingCache.Value;
            }
        }

        public static bool IsHardcoreModeEnabled => SurvivalTools.Settings?.hardcoreMode ?? false;

        public static bool IsToolDegradationEnabled =>
            (SurvivalTools.Settings?.EffectiveToolDegradationFactor ?? 0f) > 0.001f;

        public static bool IsToolMapGenEnabled => SurvivalTools.Settings?.toolMapGen ?? false;

        public static void InvalidateDebugLoggingCache() => _debugLoggingCache = null;

        #endregion

        #region Logging helpers

        private static bool ShouldLog(string logKey, bool respectCooldown = true)
        {
            if (!IsDebugLoggingEnabled) return false;
            if (!respectCooldown) return true;

            int now = Find.TickManager.TicksGame;
            if (_lastLoggedTick.TryGetValue(logKey, out var last) && now - last < LOG_COOLDOWN_TICKS)
                return false;

            _lastLoggedTick[logKey] = now;
            return true;
        }

        public static bool ShouldLogWithCooldown(string logKey) => ShouldLog(logKey, true);

        private static bool ShouldLogJobForPawn(Pawn pawn, JobDef jobDef)
        {
            if (pawn == null || jobDef == null) return false;

            CleanupJobLoggingCache(pawn);

            if (!_loggedJobsPerPawn.TryGetValue(pawn, out var set))
            {
                set = new HashSet<JobDef>();
                _loggedJobsPerPawn[pawn] = set;
            }

            if (set.Contains(jobDef)) return false;
            set.Add(jobDef);
            return true;
        }

        private static void CleanupJobLoggingCache(Pawn pawn)
        {
            if (pawn?.CurJob?.def == null) return;
            if (_loggedJobsPerPawn.TryGetValue(pawn, out var set) && !set.Contains(pawn.CurJob.def))
                set.Clear();
        }

        #endregion

        #region Backing resolution & virtual/tool-stuff support

        /// <summary>
        /// Return the physical backing <see cref="Thing"/> for a <see cref="SurvivalTool"/> (real or virtual).
        /// For virtuals, prefers an instance in pawn inventory, then closest reachable on pawn.Map, then any spawned.
        /// </summary>
        public static Thing BackingThing(SurvivalTool tool, Pawn pawn = null)
        {
            if (tool == null) return null;

            // Real tool: it's already a Thing
            if (tool is Thing thingTool)
                return thingTool;

            // Virtual wrapper (tool-stuff)
            if (tool is VirtualSurvivalTool vtool)
            {
                // 1) Pawn inventory first (most relevant for pick/drop logic)
                if (pawn?.inventory?.innerContainer != null)
                {
                    var invThing = pawn.inventory.innerContainer.FirstOrDefault(t => t.def == vtool.SourceDef);
                    if (invThing != null) return invThing;
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

                    if (found != null) return found;
                }

                // 3) Any spawned instance (fallback)
                foreach (var map in Find.Maps)
                {
                    var any = map.listerThings.ThingsOfDef(vtool.SourceDef).FirstOrDefault(t => t.Spawned);
                    if (any != null) return any;
                }

                return null;
            }

            // Unknown subclass
            return null;
        }

        public static bool HasBackingThing(SurvivalTool tool, Pawn pawn = null) => BackingThing(tool, pawn) != null;

        public static bool IsToolStuff(this ThingDef def) =>
            def.IsStuff && def.GetModExtension<SurvivalToolProperties>()?.baseWorkStatFactors?.Any() == true;

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
            // FellTree always -> TreeFellingSpeed
            if (job?.def == ST_JobDefOf.FellTree || job?.def == ST_JobDefOf.FellTreeDesignated ||
                (job?.def == JobDefOf.CutPlant && job.targetA.Thing?.def?.plant?.IsTree == true))
            {
                var list = new List<StatDef> { ST_StatDefOf.TreeFellingSpeed };
                if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"FellTreeSpecialCase_{wg?.defName ?? "null"}"))
                    Log.Message($"[SurvivalTools.Debug] Tree felling job from {wg?.defName ?? "null"} -> TreeFellingSpeed");
                return list;
            }

            // CutPlant always -> PlantHarvestingSpeed (sickle)
            if (job?.def == JobDefOf.CutPlant)
            {
                var list = new List<StatDef> { ST_StatDefOf.PlantHarvestingSpeed };
                if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"CutPlantSpecialCase_{wg?.defName ?? "null"}"))
                    Log.Message($"[SurvivalTools.Debug] CutPlant from {wg?.defName ?? "null"} -> PlantHarvestingSpeed");
                return list;
            }

            var fromWg = wg?.GetModExtension<WorkGiverExtension>()?.requiredStats;
            if (fromWg != null && fromWg.Any())
                return fromWg.Where(s => s != null).Distinct().ToList();

            var fallback = StatsForJob(job);
            if (IsDebugLoggingEnabled && job?.def != null && fallback.Any() && ShouldLog($"Fallback_Job_{job.def.defName}_{wg?.defName ?? "null"}"))
                Log.Message($"[SurvivalTools] Using job fallback stats for WGD='{wg?.defName ?? "null"}' Job='{job.def.defName}': {string.Join(", ", fallback.Select(s => s.defName))}");
            return fallback;
        }

        public static List<StatDef> RelevantStatsFor(WorkGiverDef wg, JobDef jobDef)
        {
            if (jobDef == JobDefOf.CutPlant)
                return new List<StatDef> { ST_StatDefOf.PlantHarvestingSpeed };

            var fromWg = wg?.GetModExtension<WorkGiverExtension>()?.requiredStats;
            if (fromWg != null && fromWg.Any())
                return fromWg.Where(s => s != null).Distinct().ToList();

            var fallback = StatsForJob(jobDef);
            if (IsDebugLoggingEnabled && jobDef != null && fallback.Any() && ShouldLog($"Fallback_JobDef_{jobDef.defName}_{wg?.defName ?? "null"}"))
                Log.Message($"[SurvivalTools] Using job fallback stats for WGD='{wg?.defName ?? "null"}' Job='{jobDef.defName}': {string.Join(", ", fallback.Select(s => s.defName))}");
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
            if (IsDebugLoggingEnabled && relevant && ShouldLogJobForPawn(pawn, jobDef))
                Log.Message($"[SurvivalTools.Debug] StatsForJob called for: {jobDef.defName}");

            if (jobDef == JobDefOf.Mine)
            {
                list.Add(ST_StatDefOf.DiggingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> DiggingSpeed");
                return list;
            }

            if (jobDef == ST_JobDefOf.FellTree || jobDef == ST_JobDefOf.FellTreeDesignated ||
                jobDef == ST_JobDefOf.HarvestTree || jobDef == ST_JobDefOf.HarvestTreeDesignated)
            {
                list.Add(ST_StatDefOf.TreeFellingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> TreeFellingSpeed");
                return list;
            }

            var s = jobDef.defName.ToLowerInvariant();
            if (IsDebugLoggingEnabled && relevant && ShouldLogJobForPawn(pawn, jobDef))
                Log.Message($"[SurvivalTools.Debug] Checking job: '{jobDef.defName}' (lowercase: '{s}')");

            if (s == "cutplant")
            {
                list.Add(ST_StatDefOf.PlantHarvestingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> PlantHarvestingSpeed");
                return list;
            }

            if (s.Contains("felltree"))
            {
                list.Add(ST_StatDefOf.TreeFellingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> TreeFellingSpeed (contains felltree)");
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
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> ConstructionSpeed");
                return list;
            }

            if (s.Contains("repair") || s.Contains("maintain") || s.Contains("maintenance") ||
                s.Contains("fixbroken") || s.Contains("tendmachine") || s.Contains("fix"))
            {
                list.Add(ST_StatDefOf.MaintenanceSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> MaintenanceSpeed");
                return list;
            }

            if (s.Contains("uninstall") || s.Contains("deconstruct") || s.Contains("teardown"))
            {
                list.Add(ST_StatDefOf.DeconstructionSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> DeconstructionSpeed");
                return list;
            }

            if (s.Contains("sow") || s.Contains("plantsow") || s.Contains("plantgrow"))
            {
                list.Add(ST_StatDefOf.SowingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> SowingSpeed");
                return list;
            }

            if (s.Contains("plant") || s.Contains("harvest") || s.Contains("cut"))
            {
                list.Add(ST_StatDefOf.PlantHarvestingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> PlantHarvestingSpeed");
                return list;
            }

            if (s.Contains("research") || s.Contains("experiment") || s.Contains("study"))
            {
                list.Add(ST_StatDefOf.ResearchSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> ResearchSpeed");
                return list;
            }

            if (s.Contains("clean") || s.Contains("sweep") || s.Contains("mop"))
            {
                list.Add(ST_StatDefOf.CleaningSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> CleaningSpeed");
                return list;
            }

            if (s.Contains("medical") || s.Contains("surgery") || s.Contains("operate") || s.Contains("tend") ||
                s.Contains("doctor") || (s.Contains("install") && s.Contains("bionic")))
            {
                list.Add(ST_StatDefOf.MedicalOperationSpeed);
                list.Add(ST_StatDefOf.MedicalSurgerySuccessChance);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> MedicalOperationSpeed + MedicalSurgerySuccessChance");
                return list;
            }

            if (s.Contains("butcher") || s.Contains("slaughter") || s.Contains("skin") || s.Contains("carve"))
            {
                list.Add(ST_StatDefOf.ButcheryFleshSpeed);
                list.Add(ST_StatDefOf.ButcheryFleshEfficiency);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> ButcheryFleshSpeed + ButcheryFleshEfficiency");
                return list;
            }

            if (IsDebugLoggingEnabled && relevant && ShouldLogJobForPawn(pawn, jobDef))
                Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> No stats (no patterns matched)");
            return list;
        }

        #endregion

        #region Tool/type checks

        public static bool RequiresSurvivalTool(this StatDef stat)
        {
            if (stat?.parts == null) return false;
            for (int i = 0; i < stat.parts.Count; i++)
                if (stat.parts[i] is StatPart_SurvivalTool)
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
            var ext = tDef.GetModExtension<SurvivalToolProperties>();
            return ext != null && ext != SurvivalToolProperties.defaultValues && ext.baseWorkStatFactors?.Any() == true;
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
                .Select(t => (Thing)VirtualSurvivalTool.FromThing(t))
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
            var held = pawn.GetHeldSurvivalTools().ToList();
            return held.Where(t => held.IndexOf(t).IsUnderSurvivalToolCarryLimitFor(pawn));
        }

        public static IEnumerable<Thing> GetAllUsableSurvivalTools(this Pawn pawn)
        {
            if (pawn == null) return Enumerable.Empty<Thing>();

            var eqTools = pawn.equipment?.GetDirectlyHeldThings().Where(t => t.def.IsSurvivalTool()) ?? Enumerable.Empty<Thing>();
            var invTools = pawn.GetUsableHeldSurvivalTools();

            return eqTools.Concat(invTools);
        }

        public static bool CanUseSurvivalTool(this Pawn pawn, ThingDef def)
        {
            var props = def?.GetModExtension<SurvivalToolProperties>();
            if (props?.baseWorkStatFactors == null)
            {
                if (IsDebugLoggingEnabled)
                    Log.Error($"Tried to check if {def} is a usable tool but has null tool properties or work stat factors.");
                return false;
            }

            foreach (var modifier in props.baseWorkStatFactors)
                if (modifier?.stat?.Worker?.IsDisabledFor(pawn) == false)
                    return true;
            return false;
        }

        #endregion

        #region Best tool selection / scoring

        public static IEnumerable<SurvivalTool> BestSurvivalToolsFor(Pawn pawn) =>
            SurvivalToolStats.Select(stat => pawn.GetBestSurvivalTool(stat)).Where(t => t != null);

        /// <summary>
        /// True if this tool is actively "used" by its holder for the current job's required stats.
        /// </summary>
        public static bool IsToolInUse(SurvivalTool tool)
        {
            var holder = tool?.HoldingPawn;
            if (holder == null || !holder.CanUseSurvivalTools() || !holder.CanUseSurvivalTool(tool.def))
                return false;

            var job = holder.CurJob?.def;
            if (job == null) return false;

            var req = StatsForJob(job, holder);
            if (req.NullOrEmpty()) return false;

            var toolStats = tool.WorkStatFactors.Select(m => m.stat).ToList();
            var relevant = req.Where(s => toolStats.Contains(s)).ToList();
            if (relevant.NullOrEmpty()) return false;

            foreach (var s in relevant)
                if (holder.GetBestSurvivalTool(s) == tool)
                    return true;

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

            SurvivalTool bestTool = null;
            float bestScore = 0f;

            // Baseline (no tool factors)
            float baseline = 0f;
            foreach (var s in stats)
            {
                var part = s.GetStatPart<StatPart_SurvivalTool>();
                if (part != null) baseline += part.NoToolStatFactor;
            }

            foreach (var thing in pawn.GetAllUsableSurvivalTools())
            {
                float current = 0f;
                List<StatModifier> factors;
                SurvivalTool candidate = null;

                if (thing is SurvivalTool real)
                {
                    factors = real.WorkStatFactors.ToList();
                    candidate = real;
                }
                else if (thing.def.IsToolStuff())
                {
                    var ext = thing.def.GetModExtension<SurvivalToolProperties>();
                    factors = ext?.baseWorkStatFactors?.ToList() ?? new List<StatModifier>();

                    // Wrap held stuff into a virtual tool so we can return a SurvivalTool
                    candidate = VirtualSurvivalTool.FromThing(thing);
                }
                else continue;

                foreach (var s in stats)
                {
                    var mod = factors.FirstOrDefault(m => m.stat == s);
                    if (mod != null) current += mod.value;
                    else
                    {
                        var part = s.GetStatPart<StatPart_SurvivalTool>();
                        if (part != null) current += part.NoToolStatFactor;
                    }
                }

                if (current > baseline && current > bestScore)
                {
                    bestScore = current;
                    bestTool = candidate;
                }
            }

            return bestTool;
        }

        public static float GetStatFactorFromList(this SurvivalTool tool, StatDef stat) =>
            tool.WorkStatFactors.GetStatFactorFromList(stat);

        #endregion

        #region Tool availability / gating & degrade

        public static bool HasSurvivalToolFor(this Pawn pawn, StatDef stat)
        {
            if (stat == null || !stat.RequiresSurvivalTool()) return false;

            if (pawn.GetBestSurvivalTool(stat) != null) return true;

            // Virtual/tool-stuff fallback
            return pawn.GetAllUsableSurvivalTools()
                .Any(t => t.def.IsToolStuff() &&
                          t.def.GetModExtension<SurvivalToolProperties>()?.baseWorkStatFactors?.Any(m => m.stat == stat) == true);
        }

        public static bool HasSurvivalToolFor(this Pawn pawn, StatDef stat, out SurvivalTool tool, out float statFactor)
        {
            tool = pawn.GetBestSurvivalTool(new List<StatDef> { stat });
            statFactor = tool?.WorkStatFactors.ToList().GetStatFactorFromList(stat) ?? -1f;
            return tool != null;
        }

        public static SurvivalTool GetBestSurvivalTool(this Pawn pawn, StatDef stat)
        {
            if (!pawn.CanUseSurvivalTools() || stat == null || !stat.RequiresSurvivalTool()) return null;

            var part = stat.GetStatPart<StatPart_SurvivalTool>();
            if (part == null) return null;

            SurvivalTool best = null;
            float bestFactor = part.NoToolStatFactor;

            foreach (var thing in pawn.GetAllUsableSurvivalTools())
            {
                if (thing is SurvivalTool cur)
                {
                    foreach (var mod in cur.WorkStatFactors)
                    {
                        if (mod?.stat == stat && mod.value > bestFactor)
                        {
                            best = cur;
                            bestFactor = mod.value;
                        }
                    }
                }
                else if (thing.def.IsToolStuff())
                {
                    var props = thing.def.GetModExtension<SurvivalToolProperties>();
                    if (props?.baseWorkStatFactors == null) continue;

                    foreach (var mod in props.baseWorkStatFactors)
                    {
                        if (mod?.stat == stat && mod.value > bestFactor)
                        {
                            // Note: do not return the stuff itself as SurvivalTool. We just track the factor.
                            best = null;
                            bestFactor = mod.value;
                        }
                    }
                }
            }

            if (best != null)
                LessonAutoActivator.TeachOpportunity(ST_ConceptDefOf.UsingSurvivalTools, OpportunityType.Important);

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
            var tool = pawn.GetBestSurvivalTool(stat);
            if (tool == null) return;

            var backing = BackingThing(tool, pawn);

            if (backing is SurvivalTool realTool && realTool.def.useHitPoints && IsToolDegradationEnabled)
            {
                LessonAutoActivator.TeachOpportunity(ST_ConceptDefOf.SurvivalToolDegradation, OpportunityType.GoodToKnow);
                realTool.workTicksDone++;
                if (realTool.workTicksDone >= realTool.WorkTicksToDegrade)
                {
                    realTool.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, 1));
                    realTool.workTicksDone = 0;
                }
            }
            else if (backing is ThingWithComps twc && IsToolDegradationEnabled)
            {
                LessonAutoActivator.TeachOpportunity(ST_ConceptDefOf.SurvivalToolDegradation, OpportunityType.GoodToKnow);
                if (Rand.Chance(0.01f))
                    twc.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, 1));
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
                    if (!pawn.HasSurvivalToolFor(stat))
                    {
                        if (s.autoTool)
                        {
                            string logKey = $"AutoTool_Missing_{pawn.ThingID}_{stat.defName}";
                            if (ShouldLog(logKey))
                            {
                                string statCategory = GetStatCategoryDescription(stat);
                                string ctx = GetJobContextDescription(workGiver, jobDef);
                                Log.Message($"[SurvivalTools] {pawn.LabelShort} missing required tool for {statCategory} stat {stat.defName}{ctx}, but AutoTool will attempt acquisition.");
                            }
                            continue;
                        }

                        string logKey2 = $"Missing_Tool_{pawn.ThingID}_{stat.defName}";
                        if (ShouldLog(logKey2))
                        {
                            string statCategory = GetStatCategoryDescription(stat);
                            string ctx = GetJobContextDescription(workGiver, jobDef);
                            Log.Message($"[SurvivalTools] {pawn.LabelShort} cannot start job: missing required tool for {statCategory} stat {stat.defName}{ctx}");
                        }
                        return false;
                    }
                }
                return true;
            }

            foreach (var stat in requiredStats)
                if (stat != null && pawn.GetStatValue(stat) <= 0f)
                    return false;

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
            var part = stat.GetStatPart<StatPart_SurvivalTool>();
            if (part == null)
            {
                if (IsDebugLoggingEnabled)
                    Log.ErrorOnce($"Tried to check if {tool} is better than working toolless for {stat} which has no StatPart_SurvivalTool", 8120196);
                return false;
            }
            return tool.WorkStatFactors.ToList().GetStatFactorFromList(stat) > part.NoToolStatFactor;
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

        private static string GetStatCategoryDescription(StatDef stat)
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
                if (toolProps?.baseWorkStatFactors?.Any(f => f?.stat == stat) == true)
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
