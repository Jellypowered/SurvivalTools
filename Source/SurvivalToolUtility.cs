using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI;
using RimWorld;
using RimWorld.Planet;

namespace SurvivalTools
{
    public static class SurvivalToolUtility
    {
        public static readonly FloatRange MapGenToolHitPointsRange = new FloatRange(0.3f, 0.7f);
        public const float MapGenToolMaxStuffMarketValue = 3f;

        private static bool? _debugLoggingCache;

        // Track which jobs have been logged for each pawn to reduce spam
        private static readonly Dictionary<Pawn, HashSet<JobDef>> _loggedJobsPerPawn = new Dictionary<Pawn, HashSet<JobDef>>();

        // Track other types of logging to prevent spam
        private static readonly Dictionary<string, int> _lastLoggedTick = new Dictionary<string, int>();
        private const int LOG_COOLDOWN_TICKS = 2500; // ~1 in-game hour

        public static bool IsDebugLoggingEnabled
        {
            get
            {
                if (_debugLoggingCache == null)
                {
                    _debugLoggingCache = SurvivalTools.Settings?.debugLogging ?? false;
                }
                return _debugLoggingCache.Value;
            }
        }

        public static bool IsHardcoreModeEnabled => SurvivalTools.Settings?.hardcoreMode ?? false;

        public static bool IsToolDegradationEnabled => (SurvivalTools.Settings?.EffectiveToolDegradationFactor ?? 0f) > 0.001f;

        public static bool IsToolMapGenEnabled => SurvivalTools.Settings?.toolMapGen ?? false;

        public static void InvalidateDebugLoggingCache()
        {
            _debugLoggingCache = null;
        }

        /// <summary>
        /// Checks if we should log this message based on pawn and context to reduce spam
        /// </summary>
        private static bool ShouldLog(string logKey, bool respectCooldown = true)
        {
            if (!IsDebugLoggingEnabled) return false;

            if (!respectCooldown) return true;

            int currentTick = Find.TickManager.TicksGame;
            if (_lastLoggedTick.TryGetValue(logKey, out int lastTick))
            {
                if (currentTick - lastTick < LOG_COOLDOWN_TICKS)
                {
                    return false; // Still in cooldown
                }
            }

            _lastLoggedTick[logKey] = currentTick;
            return true;
        }

        /// <summary>
        /// Public method for other classes to use cooldown-based logging
        /// </summary>
        public static bool ShouldLogWithCooldown(string logKey)
        {
            return ShouldLog(logKey, true);
        }

        /// <summary>
        /// Checks if we should log this job for this pawn (only once per pawn per job to reduce spam)
        /// </summary>
        private static bool ShouldLogJobForPawn(Pawn pawn, JobDef jobDef)
        {
            if (pawn == null || jobDef == null) return false;

            // Clean up dictionary for pawns that no longer exist or have different jobs
            CleanupJobLoggingCache(pawn);

            if (!_loggedJobsPerPawn.ContainsKey(pawn))
            {
                _loggedJobsPerPawn[pawn] = new HashSet<JobDef>();
            }

            if (_loggedJobsPerPawn[pawn].Contains(jobDef))
            {
                return false; // Already logged this job for this pawn
            }

            _loggedJobsPerPawn[pawn].Add(jobDef);
            return true;
        }

        /// <summary>
        /// Cleans up the job logging cache for pawns with different current jobs
        /// </summary>
        private static void CleanupJobLoggingCache(Pawn pawn)
        {
            if (pawn?.CurJob?.def != null && _loggedJobsPerPawn.ContainsKey(pawn))
            {
                // If the pawn's current job is different from what we have logged, clear their cache
                var currentJobDef = pawn.CurJob.def;
                if (!_loggedJobsPerPawn[pawn].Contains(currentJobDef))
                {
                    _loggedJobsPerPawn[pawn].Clear();
                }
            }
        }

        public static List<StatDef> SurvivalToolStats { get; } =
            DefDatabase<StatDef>.AllDefsListForReading.Where(s => s.RequiresSurvivalTool()).ToList();

        public static List<WorkGiverDef> SurvivalToolWorkGivers { get; } =
            DefDatabase<WorkGiverDef>.AllDefsListForReading.Where(w => w.HasModExtension<WorkGiverExtension>()).ToList();

        #region Job & WorkGiver Stat Requirements

        public static List<StatDef> RelevantStatsFor(WorkGiverDef wg, Job job)
        {
            // Special case: FellTree jobs should always use TreeFellingSpeed regardless of WorkGiver
            // This handles cases where GrowerSow creates FellTree jobs to clear trees before sowing
            if (job?.def == ST_JobDefOf.FellTree || job?.def == ST_JobDefOf.FellTreeDesignated ||
                (job?.def == JobDefOf.CutPlant && job.targetA.Thing?.def?.plant?.IsTree == true))
            {
                var treeFellingStats = new List<StatDef> { ST_StatDefOf.TreeFellingSpeed };
                if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"FellTreeSpecialCase_{wg?.defName ?? "null"}"))
                    Log.Message($"[SurvivalTools.Debug] Tree felling job from {wg?.defName ?? "null"} -> TreeFellingSpeed");
                return treeFellingStats;
            }

            // Special case: CutPlant jobs should always use PlantHarvestingSpeed (sickle)
            // This includes both harvesting and GrowerSow clearing operations
            // In hardcore mode, only sickles can cut plants
            if (job?.def == JobDefOf.CutPlant)
            {
                var plantHarvestingStats = new List<StatDef> { ST_StatDefOf.PlantHarvestingSpeed };
                if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"CutPlantSpecialCase_{wg?.defName ?? "null"}"))
                    Log.Message($"[SurvivalTools.Debug] CutPlant from {wg?.defName ?? "null"} -> PlantHarvestingSpeed (always use sickle for plant cutting)");
                return plantHarvestingStats;
            }

            var fromWg = wg?.GetModExtension<WorkGiverExtension>()?.requiredStats;
            if (fromWg != null && fromWg.Any())
                return fromWg.Where(s => s != null).Distinct().ToList();

            var fallback = StatsForJob(job);
            if (IsDebugLoggingEnabled && job?.def != null && fallback.Any())
            {
                string logKey = $"Fallback_Job_{job.def.defName}_{wg?.defName ?? "null"}";
                if (ShouldLog(logKey))
                    Log.Message($"[SurvivalTools] Using job fallback stats for WGD='{wg?.defName ?? "null"}' Job='{job.def.defName}': {string.Join(", ", fallback.Select(s => s.defName))}");
            }
            return fallback;
        }

        public static List<StatDef> RelevantStatsFor(WorkGiverDef wg, JobDef jobDef)
        {
            // Special case: CutPlant jobs should use PlantHarvestingSpeed regardless of WorkGiver
            // This handles cases where GrowerSow creates CutPlant jobs but we want harvesting stats
            if (jobDef == JobDefOf.CutPlant)
            {
                var plantHarvestingStats = new List<StatDef> { ST_StatDefOf.PlantHarvestingSpeed };
                return plantHarvestingStats;
            }

            var fromWg = wg?.GetModExtension<WorkGiverExtension>()?.requiredStats;
            if (fromWg != null && fromWg.Any())
                return fromWg.Where(s => s != null).Distinct().ToList();

            var fallback = StatsForJob(jobDef);
            if (IsDebugLoggingEnabled && jobDef != null && fallback.Any())
            {
                string logKey = $"Fallback_JobDef_{jobDef.defName}_{wg?.defName ?? "null"}";
                if (ShouldLog(logKey))
                    Log.Message($"[SurvivalTools] Using job fallback stats for WGD='{wg?.defName ?? "null"}' Job='{jobDef.defName}': {string.Join(", ", fallback.Select(s => s.defName))}");
            }
            return fallback;
        }

        public static List<StatDef> StatsForJob(Job job) => StatsForJob(job?.def);

        /// <summary>
        /// Checks if a job is relevant for tool usage to reduce debug log spam
        /// </summary>
        private static bool IsToolRelevantJob(JobDef jobDef)
        {
            if (jobDef == null) return false;

            // Check specific survival tool jobs
            if (jobDef == JobDefOf.Mine ||
                jobDef == ST_JobDefOf.FellTree || jobDef == ST_JobDefOf.FellTreeDesignated ||
                jobDef == ST_JobDefOf.HarvestTree || jobDef == ST_JobDefOf.HarvestTreeDesignated)
                return true;

            var s = jobDef.defName.ToLowerInvariant();

            // Check job name patterns that might use tools
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

            // Only log relevant tool-related jobs to avoid spam
            bool isToolRelevantJob = IsToolRelevantJob(jobDef);
            if (IsDebugLoggingEnabled && isToolRelevantJob && ShouldLogJobForPawn(pawn, jobDef))
                Log.Message($"[SurvivalTools.Debug] StatsForJob called for: {jobDef.defName} (defName='{jobDef.defName}')");

            if (jobDef == JobDefOf.Mine)
            {
                list.Add(ST_StatDefOf.DiggingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> DiggingSpeed");
                return list;
            }

            // Handle specific survival tool jobs first
            if (jobDef == ST_JobDefOf.FellTree || jobDef == ST_JobDefOf.FellTreeDesignated
                || jobDef == ST_JobDefOf.HarvestTree || jobDef == ST_JobDefOf.HarvestTreeDesignated)
            {
                list.Add(ST_StatDefOf.TreeFellingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> TreeFellingSpeed");
                return list;
            }

            var s = jobDef.defName.ToLowerInvariant();

            // Debug: Log the job name we're checking
            if (IsDebugLoggingEnabled && isToolRelevantJob && ShouldLogJobForPawn(pawn, jobDef))
                Log.Message($"[SurvivalTools.Debug] Checking job: '{jobDef.defName}' (lowercase: '{s}')");

            // Handle specific plant jobs that might be misclassified by patterns
            if (s == "cutplant")
            {
                list.Add(ST_StatDefOf.PlantHarvestingSpeed);
                if (IsDebugLoggingEnabled && isToolRelevantJob && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> PlantHarvestingSpeed (specific plant cutting job)");
                return list;
            }

            // Handle specific tree/plant jobs
            if (s.Contains("felltree"))
            {
                list.Add(ST_StatDefOf.TreeFellingSpeed);
                if (IsDebugLoggingEnabled && isToolRelevantJob && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> TreeFellingSpeed (contains felltree)");
                return list;
            }

            // Exclude jobs that shouldn't match maintenance patterns
            if (s == "wait_maintainposture")
            {
                // This job contains "maintain" but isn't actually a maintenance job
                if (IsDebugLoggingEnabled && isToolRelevantJob && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> No stats (excluded from maintenance pattern)");
                return list;
            }

            if (s.Contains("construct") || s.Contains("build") || s.Contains("frame") ||
                s.Contains("smooth") || s.Contains("install") || s.Contains("buildroof") ||
                s.Contains("removeroof"))
            {
                list.Add(StatDefOf.ConstructionSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> ConstructionSpeed (pattern: construction)");
                return list;
            }

            if (s.Contains("repair") || s.Contains("maintain") || s.Contains("maintenance") ||
                s.Contains("fixbroken") || s.Contains("tendmachine") || s.Contains("fix"))
            {
                list.Add(ST_StatDefOf.MaintenanceSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> MaintenanceSpeed (pattern: repair)");
                return list;
            }

            if (s.Contains("uninstall") || s.Contains("deconstruct") || s.Contains("teardown"))
            {
                list.Add(ST_StatDefOf.DeconstructionSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> DeconstructionSpeed (pattern: deconstruct)");
                return list;
            }

            if (s.Contains("sow") || s.Contains("plantsow") || s.Contains("plantgrow"))
            {
                list.Add(ST_StatDefOf.SowingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> SowingSpeed (pattern: sow/plantsow/plantgrow)");
                return list;
            }

            if (s.Contains("plant") || s.Contains("harvest") || s.Contains("cut"))
            {
                list.Add(ST_StatDefOf.PlantHarvestingSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> PlantHarvestingSpeed (pattern: plant/harvest/cut)");
                return list;
            }

            if (s.Contains("research") || s.Contains("experiment") || s.Contains("study"))
            {
                list.Add(ST_StatDefOf.ResearchSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> ResearchSpeed (pattern: research)");
                return list;
            }

            // Cleaning - always optional (never required in hardcore mode)
            if (s.Contains("clean") || s.Contains("sweep") || s.Contains("mop"))
            {
                list.Add(ST_StatDefOf.CleaningSpeed);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> CleaningSpeed (pattern: clean)");
                return list;
            }

            // Medical operations - require at least one of the two stats in hardcore mode
            if (s.Contains("medical") || s.Contains("surgery") || s.Contains("operate") || s.Contains("tend") ||
                s.Contains("doctor") || s.Contains("amputat") || s.Contains("install") && s.Contains("bionic"))
            {
                list.Add(ST_StatDefOf.MedicalOperationSpeed);
                list.Add(ST_StatDefOf.MedicalSurgerySuccessChance);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> MedicalOperationSpeed + MedicalSurgerySuccessChance (pattern: medical)");
                return list;
            }

            // Butchery - require at least one of the two stats in hardcore mode
            if (s.Contains("butcher") || s.Contains("slaughter") || s.Contains("skin") || s.Contains("carve"))
            {
                list.Add(ST_StatDefOf.ButcheryFleshSpeed);
                list.Add(ST_StatDefOf.ButcheryFleshEfficiency);
                if (IsDebugLoggingEnabled && ShouldLogJobForPawn(pawn, jobDef))
                    Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> ButcheryFleshSpeed + ButcheryFleshEfficiency (pattern: butcher)");
                return list;
            }

            if (IsDebugLoggingEnabled && isToolRelevantJob && ShouldLogJobForPawn(pawn, jobDef))
                Log.Message($"[SurvivalTools.Debug] {jobDef.defName} -> No stats (no patterns matched)");
            return list;
        }

        #endregion

        #region Tool Properties & Checks

        public static bool RequiresSurvivalTool(this StatDef stat)
        {
            if (stat?.parts == null) return false;
            for (int i = 0; i < stat.parts.Count; i++)
            {
                if (stat.parts[i] is StatPart_SurvivalTool)
                    return true;
            }
            return false;
        }

        public static bool IsSurvivalTool(this BuildableDef def, out SurvivalToolProperties toolProps)
        {
            toolProps = def?.GetModExtension<SurvivalToolProperties>();
            return def.IsSurvivalTool();
        }

        public static bool IsSurvivalTool(this BuildableDef def) =>
            def is ThingDef tDef && tDef.thingClass == typeof(SurvivalTool) && tDef.HasModExtension<SurvivalToolProperties>();

        #endregion

        #region Pawn Tool Usage & Capacity

        public static bool CanUseSurvivalTools(this Pawn pawn) =>
            pawn.RaceProps.intelligence >= Intelligence.ToolUser &&
            pawn.RaceProps.Humanlike &&  // Exclude mechanoids - they have built-in tools
            !pawn.RaceProps.IsMechanoid &&
            pawn.Faction == Faction.OfPlayer &&
            (pawn.equipment != null || pawn.inventory != null) &&
            pawn.TraderKind == null;

        public static bool IsUnderSurvivalToolCarryLimitFor(this int count, Pawn pawn) =>
            !SurvivalTools.Settings.toolLimit || count < pawn.GetStatValue(ST_StatDefOf.SurvivalToolCarryCapacity);

        public static IEnumerable<Thing> GetHeldSurvivalTools(this Pawn pawn) =>
            pawn.inventory?.innerContainer?.Where(t => t.def.IsSurvivalTool()) ?? Enumerable.Empty<Thing>();

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
            var eqTools = pawn.equipment?.GetDirectlyHeldThings().Where(t => t.def.IsSurvivalTool()) ?? Enumerable.Empty<Thing>();
            return eqTools.Concat(pawn.GetUsableHeldSurvivalTools());
        }

        public static bool CanUseSurvivalTool(this Pawn pawn, ThingDef def)
        {
            var props = def?.GetModExtension<SurvivalToolProperties>();
            if (props?.baseWorkStatFactors == null)
            {
                if (IsDebugLoggingEnabled)
                {
                    Log.Error($"Tried to check if {def} is a usable tool but has null tool properties or work stat factors.");
                }
                return false;
            }

            foreach (var modifier in props.baseWorkStatFactors)
                if (modifier?.stat?.Worker?.IsDisabledFor(pawn) == false)
                    return true;
            return false;
        }

        #endregion

        #region Best Tool Selection

        public static IEnumerable<SurvivalTool> BestSurvivalToolsFor(Pawn pawn)
        {
            return SurvivalToolStats
                .Select(stat => pawn.GetBestSurvivalTool(stat))
                .Where(tool => tool != null);
        }

        /// <summary>
        /// Efficiently determines if a tool is currently in use by its holding pawn.
        /// A tool is considered "in use" if the pawn is actively doing a job that requires the stat this tool provides.
        /// </summary>
        public static bool IsToolInUse(SurvivalTool tool)
        {
            if (tool?.HoldingPawn == null) return false;

            var pawn = tool.HoldingPawn;
            if (!pawn.CanUseSurvivalTools() || !pawn.CanUseSurvivalTool(tool.def))
                return false;

            // Check if the pawn is currently doing a job that requires a stat this tool provides
            var currentJob = pawn.CurJob;
            if (currentJob?.def == null) return false;

            // Get the stats required for the current job
            var requiredStats = StatsForJob(currentJob.def, pawn);
            if (requiredStats.NullOrEmpty()) return false;

            // Check if this tool is the best for any of the stats required by the current job
            var toolStats = tool.WorkStatFactors.Select(m => m.stat).ToList();
            var relevantStats = requiredStats.Where(stat => toolStats.Contains(stat)).ToList();

            if (relevantStats.NullOrEmpty()) return false;

            // The tool is in use if it's the best tool for any stat required by the current job
            foreach (var stat in relevantStats)
            {
                if (pawn.GetBestSurvivalTool(stat) == tool)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Calculates the work stat factors for a survival tool based on its properties and material.
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

                float finalFactor = CalculateFinalStatFactor(baseModifier, effectiveness, sProps);

                yield return new StatModifier
                {
                    stat = baseModifier.stat,
                    value = finalFactor
                };
            }
        }

        /// <summary>
        /// Calculates the final stat factor by applying effectiveness and material modifiers.
        /// </summary>
        private static float CalculateFinalStatFactor(StatModifier baseModifier, float effectiveness, StuffPropsTool stuffProps)
        {
            float factor = baseModifier.value * effectiveness;

            // Apply material-specific modifiers
            if (stuffProps.toolStatFactors != null)
            {
                var materialModifier = stuffProps.toolStatFactors.FirstOrDefault(m => m?.stat == baseModifier.stat);
                if (materialModifier != null)
                {
                    factor *= materialModifier.value;
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

            // Calculate baseline score (no tool factors)
            float noToolScore = 0f;
            foreach (var stat in stats)
            {
                var statPart = stat.GetStatPart<StatPart_SurvivalTool>();
                if (statPart != null)
                {
                    noToolScore += statPart.NoToolStatFactor;
                }
            }

            foreach (var tool in pawn.GetAllUsableSurvivalTools().OfType<SurvivalTool>())
            {
                float currentScore = 0f;
                var workStatFactors = tool.WorkStatFactors.ToList();

                foreach (var stat in stats)
                {
                    // Only count stats that the tool actually has modifiers for
                    var modifier = workStatFactors.FirstOrDefault(m => m.stat == stat);
                    if (modifier != null)
                    {
                        currentScore += modifier.value;
                    }
                    else
                    {
                        // If tool doesn't have this stat, use the no-tool factor
                        var statPart = stat.GetStatPart<StatPart_SurvivalTool>();
                        if (statPart != null)
                        {
                            currentScore += statPart.NoToolStatFactor;
                        }
                    }
                }

                // Only consider tools that are better than no tool
                if (currentScore > noToolScore && currentScore > bestScore)
                {
                    bestScore = currentScore;
                    bestTool = tool;
                }
            }
            return bestTool;
        }

        public static float GetStatFactorFromList(this SurvivalTool tool, StatDef stat)
        {
            return tool.WorkStatFactors.GetStatFactorFromList(stat);
        }

        #endregion

        #region Tool Degradation & Reporting
        public static bool HasSurvivalToolFor(this Pawn pawn, StatDef stat)
        {
            if (stat == null || !stat.RequiresSurvivalTool())
                return false;
            return pawn.GetBestSurvivalTool(stat) != null;
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

            var statPart = stat.GetStatPart<StatPart_SurvivalTool>();
            if (statPart == null) return null;

            SurvivalTool best = null;
            float bestFactor = statPart.NoToolStatFactor;

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
            }

            if (best != null)
                LessonAutoActivator.TeachOpportunity(ST_ConceptDefOf.UsingSurvivalTools, OpportunityType.Important);

            return best;
        }

        #endregion

        #region Tool Degradation & Reporting

        public static string GetSurvivalToolOverrideReportText(SurvivalTool tool, StatDef stat)
        {
            var statFactorList = tool.WorkStatFactors;
            var stuffPropsTool = tool.Stuff?.GetModExtension<StuffPropsTool>();

            var builder = new StringBuilder();
            builder.AppendLine(stat.description);
            builder.AppendLine();

            builder.AppendLine($"{tool.def.LabelCap}: {tool.def.GetModExtension<SurvivalToolProperties>().baseWorkStatFactors.GetStatFactorFromList(stat).ToStringByStyle(ToStringStyle.Integer, ToStringNumberSense.Factor)}");
            builder.AppendLine();

            builder.AppendLine($"{ST_StatDefOf.ToolEffectivenessFactor.LabelCap}: {tool.GetStatValue(ST_StatDefOf.ToolEffectivenessFactor).ToStringByStyle(ToStringStyle.Integer, ToStringNumberSense.Factor)}");

            if (stuffPropsTool != null && stuffPropsTool.toolStatFactors.GetStatFactorFromList(stat) != 1f)
            {
                builder.AppendLine();
                builder.AppendLine($"{"StatsReport_Material".Translate()} ({tool.Stuff.LabelCap}): {stuffPropsTool.toolStatFactors.GetStatFactorFromList(stat).ToStringByStyle(ToStringStyle.Integer, ToStringNumberSense.Factor)}");
            }

            builder.AppendLine();
            builder.AppendLine($"{"StatsReport_FinalValue".Translate()}: {statFactorList.ToList().GetStatFactorFromList(stat).ToStringByStyle(ToStringStyle.Integer, ToStringNumberSense.Factor)}");

            return builder.ToString();
        }

        public static void TryDegradeTool(Pawn pawn, StatDef stat)
        {
            var tool = pawn.GetBestSurvivalTool(stat);
            if (tool != null && tool.def.useHitPoints && SurvivalTools.Settings.ToolDegradationEnabled)
            {
                LessonAutoActivator.TeachOpportunity(ST_ConceptDefOf.SurvivalToolDegradation, OpportunityType.GoodToKnow);
                tool.workTicksDone++;
                if (tool.workTicksDone >= tool.WorkTicksToDegrade)
                {
                    tool.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, 1));
                    tool.workTicksDone = 0;
                }
            }
        }

        #endregion

        #region Work & Job Logic

        public static bool MeetsWorkGiverStatRequirements(this Pawn pawn, List<StatDef> requiredStats)
        {
            return pawn.MeetsWorkGiverStatRequirements(requiredStats, null, null);
        }

        public static bool MeetsWorkGiverStatRequirements(this Pawn pawn, List<StatDef> requiredStats, WorkGiverDef workGiver = null, JobDef jobDef = null)
        {
            if (requiredStats.NullOrEmpty())
                return true;

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
                                string jobContext = GetJobContextDescription(workGiver, jobDef);
                                Log.Message($"[SurvivalTools] {pawn.LabelShort} missing required tool for {statCategory} stat {stat.defName}{jobContext}, but AutoTool will attempt acquisition.");
                            }
                            continue;
                        }

                        string logKey2 = $"Missing_Tool_{pawn.ThingID}_{stat.defName}";
                        if (ShouldLog(logKey2))
                        {
                            string statCategory = GetStatCategoryDescription(stat);
                            string jobContext = GetJobContextDescription(workGiver, jobDef);
                            Log.Message($"[SurvivalTools] {pawn.LabelShort} cannot start job: missing required tool for {statCategory} stat {stat.defName}{jobContext}");
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

        /// <summary>
        /// Checks if a pawn can fell trees based on tool requirements.
        /// </summary>
        public static bool CanFellTrees(this Pawn pawn)
        {
            var fellWG = ST_WorkGiverDefOf.FellTrees;
            var ext = fellWG?.GetModExtension<WorkGiverExtension>();
            var req = ext?.requiredStats;

            if (req == null || req.Count == 0) return true;

            return pawn.MeetsWorkGiverStatRequirements(req);
        }

        public static IEnumerable<WorkGiver> AssignedToolRelevantWorkGivers(this Pawn pawn)
        {
            if (pawn.workSettings == null)
            {
                if (IsDebugLoggingEnabled)
                {
                    Log.ErrorOnce($"Tried to get tool-relevant work givers for {pawn} but has null workSettings", 11227);
                }
                yield break;
            }

            foreach (var giver in pawn.workSettings.WorkGiversInOrderNormal)
            {
                var ext = giver.def.GetModExtension<WorkGiverExtension>();
                if (ext?.requiredStats?.Any(s => s.RequiresSurvivalTool()) == true)
                {
                    yield return giver;
                }
            }
        }

        public static List<StatDef> AssignedToolRelevantWorkGiversStatDefs(this Pawn pawn)
        {
            var allStats = pawn.AssignedToolRelevantWorkGivers()
                .SelectMany(g => g.def.GetModExtension<WorkGiverExtension>().requiredStats)
                .Where(s => s != null)
                .Distinct()
                .ToList();

            // Filter to only include stats that have tools available in the current game
            return FilterStatsWithAvailableTools(allStats);
        }

        /// <summary>
        /// Get all stats relevant to a pawn's assigned work givers, including those for which tools may not exist.
        /// This is used by the alert system to warn about missing tools even for optional jobs like cleaning/butchery.
        /// </summary>
        public static List<StatDef> AssignedToolRelevantWorkGiversStatDefsForAlerts(this Pawn pawn)
        {
            var allStats = pawn.AssignedToolRelevantWorkGivers()
                .SelectMany(g => g.def.GetModExtension<WorkGiverExtension>().requiredStats)
                .Where(s => s != null)
                .Distinct()
                .ToList();

            // For alerts, only include stats that have tools available (no point alerting about impossible requirements)
            // but don't filter based on job blocking - we want to alert about cleaning/butchery tools too
            return allStats.Where(ToolsExistForStat).ToList();
        }

        public static bool NeedsSurvivalTool(this Pawn pawn, SurvivalTool tool)
        {
            var relevantStats = pawn.AssignedToolRelevantWorkGiversStatDefs();
            return tool.WorkStatFactors.Any(factor => relevantStats.Contains(factor.stat));
        }

        public static bool BetterThanWorkingToollessFor(this SurvivalTool tool, StatDef stat)
        {
            var statPart = stat.GetStatPart<StatPart_SurvivalTool>();
            if (statPart == null)
            {
                if (IsDebugLoggingEnabled)
                {
                    Log.ErrorOnce($"Tried to check if {tool} is better than working toolless for {stat} which has no StatPart_SurvivalTool", 8120196);
                }
                return false;
            }
            return tool.WorkStatFactors.ToList().GetStatFactorFromList(stat) > statPart.NoToolStatFactor;
        }

        #endregion

        #region Extension Methods

        /// <summary>
        /// Gets the stat factor from a list of StatModifiers, or 1.0 if not found.
        /// Returns 1.0 as default since that represents "no modification" to a stat.
        /// </summary>
        public static float GetStatFactorFromList(this IEnumerable<StatModifier> modifiers, StatDef stat)
        {
            if (modifiers == null || stat == null) return 1.0f;

            var modifier = modifiers.FirstOrDefault(m => m.stat == stat);
            return modifier?.value ?? 1.0f;
        }

        #endregion

        #region Storage & Hauling

        public static Job DequipAndTryStoreSurvivalTool(this Pawn pawn, Thing tool, bool enqueueCurrent = true)
        {
            if (pawn.CurJob != null && enqueueCurrent)
                pawn.jobs.jobQueue.EnqueueFirst(pawn.CurJob);

            if (StoreUtility.TryFindBestBetterStoreCellFor(tool, pawn, pawn.MapHeld, StoreUtility.CurrentStoragePriorityOf(tool), pawn.Faction, out IntVec3 c))
            {
                var haulJob = new Job(JobDefOf.HaulToCell, tool, c) { count = 1 };
                pawn.jobs.jobQueue.EnqueueFirst(haulJob);
            }

            return new Job(ST_JobDefOf.DropSurvivalTool, tool);
        }

        public static bool CanRemoveExcessSurvivalTools(this Pawn pawn) =>
            !pawn.Drafted && !pawn.IsWashing() && !pawn.IsFormingCaravan() && !pawn.IsCaravanMember()
            && pawn.CurJobDef?.casualInterruptible != false
            && !pawn.IsBurning() && !(pawn.carryTracker?.CarriedThing is SurvivalTool);

        private static bool IsWashing(this Pawn pawn)
        {
            return ModCompatibilityCheck.DubsBadHygiene && pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("Washing"));
        }

        private static string GetStatCategoryDescription(StatDef stat)
        {
            if (stat == ST_StatDefOf.MedicalOperationSpeed || stat == ST_StatDefOf.MedicalSurgerySuccessChance)
                return "medical";
            if (stat == ST_StatDefOf.ButcheryFleshSpeed || stat == ST_StatDefOf.ButcheryFleshEfficiency)
                return "butchery";
            if (stat == ST_StatDefOf.CleaningSpeed)
                return "cleaning";
            if (stat == ST_StatDefOf.ResearchSpeed)
                return "research";
            if (stat == StatDefOf.ConstructionSpeed)
                return "construction";
            if (stat == StatDefOf.MiningSpeed)
                return "mining";
            if (stat == StatDefOf.PlantWorkSpeed)
                return "plant work";
            if (stat == ST_StatDefOf.TreeFellingSpeed)
                return "tree felling";

            return "tool";
        }

        private static string GetJobContextDescription(WorkGiverDef workGiver, JobDef jobDef)
        {
            if (workGiver != null && jobDef != null)
                return $" (workGiver: {workGiver.defName}, job: {jobDef.defName})";
            if (workGiver != null)
                return $" (workGiver: {workGiver.defName})";
            if (jobDef != null)
                return $" (job: {jobDef.defName})";
            return "";
        }

        /// <summary>
        /// Check if any tools exist in the current game that provide the specified stat.
        /// This helps prevent alerts for stats that have no available tools.
        /// </summary>
        public static bool ToolsExistForStat(StatDef stat)
        {
            if (stat == null) return false;

            // Check all loaded things to see if any provide this stat
            foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefs)
            {
                var toolProps = thingDef.GetModExtension<SurvivalToolProperties>();
                if (toolProps?.baseWorkStatFactors != null)
                {
                    if (toolProps.baseWorkStatFactors.Any(factor => factor.stat == stat))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Get only the stats that have tools available in the current game.
        /// This filters out stats from mods that add stat requirements but no tools.
        /// </summary>
        public static List<StatDef> FilterStatsWithAvailableTools(IEnumerable<StatDef> stats)
        {
            if (stats == null) return new List<StatDef>();

            return stats.Where(ToolsExistForStat).ToList();
        }

        #endregion
    }
}
