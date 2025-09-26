// RimWorld 1.6 / C# 7.3
// Source/Helpers/StatFilters.cs

// Todo: Evaluate if this file should be kept, refactored, or removed.
// It contains various stat-related utilities, but some may be redundant or unused.
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Helpers
{
    /// <summary>
    /// Helper methods for filtering, analyzing, and categorizing StatDefs.
    /// Centralized from SurvivalToolUtility and various stat-related logic.
    /// 
    /// Future ideas:
    /// - Allow players to tweak which stats are optional/required via mod settings.
    /// - Replace hardcoded priority scores with a DefModExtension or settings map.
    /// - Add category split for "TreeFelling" vs "Mining" so they're displayed separately.
    /// </summary>
    public static class StatFilters
    {
        #region Stat Classification

        /// <summary>
        /// Check if a stat is considered "optional" and shouldn't block jobs in hardcore mode.
        /// Optional stats provide bonuses but aren't strictly required.
        /// 
        /// Note: ButcheryFleshEfficiency is optional, but ButcheryFleshSpeed is NOT.
        /// </summary>
        public static bool IsOptionalStat(StatDef stat)
        {
            if (stat == null) return true;

            return stat == ST_StatDefOf.CleaningSpeed ||
                      stat == ST_StatDefOf.MedicalOperationSpeed ||
                      stat == ST_StatDefOf.MedicalSurgerySuccessChance ||
                      stat == ST_StatDefOf.ButcheryFleshEfficiency ||
                      // Mining yield should be a bonus, not a gate
                      stat == ST_StatDefOf.MiningYieldDigging;
        }

        /// <summary>
        /// Check if a stat should block jobs when missing tools in hardcore mode.
        /// </summary>
        public static bool ShouldBlockJobForMissingStat(StatDef stat)
        {
            if (stat == null) return false;

            return stat == ST_StatDefOf.DiggingSpeed ||
                stat == ST_StatDefOf.TreeFellingSpeed ||
                stat == ST_StatDefOf.PlantHarvestingSpeed ||
                stat == ST_StatDefOf.SowingSpeed ||
                stat == StatDefOf.ConstructionSpeed ||
                stat == ST_StatDefOf.MaintenanceSpeed ||
                stat == ST_StatDefOf.DeconstructionSpeed ||
                stat == ST_StatDefOf.ResearchSpeed ||
                stat == ST_StatDefOf.ButcheryFleshSpeed;
        }

        /// <summary>
        /// Get stats that are relevant for tool-based work and have available tools in the game.
        /// </summary>
        public static List<StatDef> GetAvailableToolStats()
        {
            var allToolStats = SurvivalToolUtility.SurvivalToolStats;
            return FilterStatsWithAvailableTools(allToolStats);
        }

        /// <summary>
        /// Filter a list of stats to only include those that have available tools in the current game.
        /// </summary>
        public static List<StatDef> FilterStatsWithAvailableTools(IEnumerable<StatDef> stats)
        {
            if (stats == null) return new List<StatDef>();
            var availableStats = new List<StatDef>();

            foreach (var stat in stats.Where(s => s != null))
            {
                if (HasAvailableToolsForStat(stat))
                    availableStats.Add(stat);
            }

            return availableStats;
        }

        /// <summary>
        /// Check if there are any tools available in the game that improve the specified stat.
        /// </summary>
        public static bool HasAvailableToolsForStat(StatDef stat)
        {
            if (stat == null) return false;

            // Real tools
            foreach (var toolDef in DefDatabase<ThingDef>.AllDefs.Where(d => d.IsSurvivalTool()))
            {
                var props = toolDef.GetModExtension<SurvivalToolProperties>();
                if (props?.baseWorkStatFactors?.Any(f => f.stat == stat) == true)
                    return true;
            }

            // Tool-stuff (materials like Bioferrite, Obsidian, etc.)
            foreach (var stuffDef in DefDatabase<ThingDef>.AllDefs.Where(d => d.IsToolStuff()))
            {
                var props = stuffDef.GetModExtension<SurvivalToolProperties>();
                if (props?.baseWorkStatFactors?.Any(f => f.stat == stat) == true)
                    return true;
            }

            return false;
        }

        #endregion

        #region Stat Grouping

        /// <summary>
        /// Group stats by their functional category (mining, farming, etc.).
        /// 
        /// Future idea: break "Mining" and "TreeFelling" into separate groups for UI clarity.
        /// </summary>
        public static Dictionary<string, List<StatDef>> GroupStatsByCategory(IEnumerable<StatDef> stats)
        {
            var groups = new Dictionary<string, List<StatDef>>
            {
                ["Mining"] = new List<StatDef>(),
                ["Construction"] = new List<StatDef>(),
                ["Farming"] = new List<StatDef>(),
                ["Maintenance"] = new List<StatDef>(),
                ["Medical"] = new List<StatDef>(),
                ["Research"] = new List<StatDef>(),
                ["Cleaning"] = new List<StatDef>(),
                ["Cooking"] = new List<StatDef>(),
                ["Other"] = new List<StatDef>()
            };

            foreach (var stat in stats?.Where(s => s != null) ?? Enumerable.Empty<StatDef>())
            {
                var category = CategorizeStat(stat);
                if (groups.ContainsKey(category))
                    groups[category].Add(stat);
                else
                    groups["Other"].Add(stat);
            }

            return groups;
        }

        /// <summary>
        /// Get a human-readable category name for a stat.
        /// </summary>
        public static string CategorizeStat(StatDef stat)
        {
            if (stat == null) return "Other";

            if (stat == ST_StatDefOf.DiggingSpeed) return "Mining";
            if (stat == ST_StatDefOf.TreeFellingSpeed) return "TreeFelling";
            if (stat == StatDefOf.ConstructionSpeed) return "Construction";
            if (stat == ST_StatDefOf.MaintenanceSpeed || stat == ST_StatDefOf.DeconstructionSpeed) return "Maintenance";
            if (stat == ST_StatDefOf.SowingSpeed || stat == ST_StatDefOf.PlantHarvestingSpeed) return "Farming";
            if (stat == ST_StatDefOf.MedicalOperationSpeed || stat == ST_StatDefOf.MedicalSurgerySuccessChance) return "Medical";
            if (stat == ST_StatDefOf.ResearchSpeed) return "Research";
            if (stat == ST_StatDefOf.CleaningSpeed) return "Cleaning";
            if (stat == ST_StatDefOf.ButcheryFleshSpeed || stat == ST_StatDefOf.ButcheryFleshEfficiency) return "Cooking";
            if (stat == ST_StatDefOf.WorkSpeedGlobal) return "Crafting";

            return "Other";
        }

        #endregion

        #region Stat Validation

        public static bool IsValidSurvivalToolStat(StatDef stat)
        {
            return stat?.parts?.Any(p => p is Stats.StatPart_SurvivalTools) == true;
        }

        public static List<StatDef> GetAllSurvivalToolStats()
        {
            return DefDatabase<StatDef>.AllDefs
                .Where(IsValidSurvivalToolStat)
                .ToList();
        }

        public static bool ContainsSurvivalToolStats(IEnumerable<StatDef> stats)
        {
            return stats?.Any(IsValidSurvivalToolStat) == true;
        }

        public static List<StatDef> FilterToSurvivalToolStats(IEnumerable<StatDef> stats)
        {
            return stats?.Where(IsValidSurvivalToolStat).ToList() ?? new List<StatDef>();
        }

        #endregion

        #region Priority Scoring

        /// <summary>
        /// Get a priority score for a stat (higher = more important).
        /// 
        /// Future idea: externalize these values into mod settings or DefModExtensions
        /// so players/modders can rebalance priorities without code edits.
        /// </summary>
        public static int GetStatPriority(StatDef stat)
        {
            if (stat == null) return 0;

            if (stat == ST_StatDefOf.DiggingSpeed) return 100;
            if (stat == ST_StatDefOf.TreeFellingSpeed) return 95;
            if (stat == StatDefOf.ConstructionSpeed) return 90;
            if (stat == ST_StatDefOf.WorkSpeedGlobal) return 85;

            if (stat == ST_StatDefOf.PlantHarvestingSpeed) return 80;
            if (stat == ST_StatDefOf.SowingSpeed) return 75;
            if (stat == ST_StatDefOf.MaintenanceSpeed) return 70;
            if (stat == ST_StatDefOf.DeconstructionSpeed) return 65;

            if (stat == ST_StatDefOf.ButcheryFleshSpeed) return 50;
            if (stat == ST_StatDefOf.MedicalOperationSpeed) return 40;
            if (stat == ST_StatDefOf.ResearchSpeed) return 30;
            if (stat == ST_StatDefOf.CleaningSpeed) return 20;

            if (stat == ST_StatDefOf.ButcheryFleshEfficiency) return 15;
            if (stat == ST_StatDefOf.MedicalSurgerySuccessChance) return 15;

            return 10;
        }

        public static List<StatDef> SortByPriority(IEnumerable<StatDef> stats)
        {
            return stats?.OrderByDescending(GetStatPriority).ToList() ?? new List<StatDef>();
        }

        #endregion
    }
}
