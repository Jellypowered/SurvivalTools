// RimWorld 1.6 / C# 7.3
// Source/Helpers/ToolScoring.cs
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Helpers
{
    /// <summary>
    /// Helper methods for scoring and comparing survival tools for AI decision-making.
    /// Centralized from AutoToolPickup_UtilityIntegrated and JobGiver_OptimizeSurvivalTools.
    /// </summary>
    public static class ToolScoring
    {
        /// <summary>
        /// Calculate a comprehensive score for a survival tool based on pawn needs and work stats.
        /// Higher score = better tool for the pawn's current work.
        /// </summary>
        public static float CalculateToolScore(SurvivalTool tool, Pawn pawn, List<StatDef> workRelevantStats)
        {
            if (tool?.def == null || pawn == null || workRelevantStats == null)
                return 0f;

            var factors = GetEffectiveWorkStatFactors(tool);
            if (!factors.Any()) return 0f;

            float totalScore = 0f;
            int matchedStats = 0;

            foreach (var stat in workRelevantStats.Where(s => s != null))
            {
                var factor = factors.GetStatFactorFromList(stat);
                var baseline = GetNoToolBaseline(stat);

                if (factor > baseline + 0.001f) // Only count actual improvements over no-tool baseline
                {
                    totalScore += factor - baseline; // Score is the improvement amount
                    matchedStats++;
                }
            }

            // Bonus for tools that improve multiple relevant stats (multitool value)
            if (matchedStats > 1)
                totalScore *= 1f + (matchedStats - 1) * 0.1f;

            // Quality and hit points influence
            if (tool.TryGetQuality(out var quality))
                totalScore *= QualityMultiplier(quality);

            var hitPointsPercent = tool.HitPoints / (float)tool.MaxHitPoints;
            totalScore *= 0.5f + hitPointsPercent * 0.5f; // 50-100% based on condition

            return totalScore;
        }

        /// <summary>
        /// Compare two tools to determine which is better for the pawn's needs.
        /// Returns: positive if tool1 is better, negative if tool2 is better, zero if equal.
        /// </summary>
        public static float CompareTools(SurvivalTool tool1, SurvivalTool tool2, Pawn pawn, List<StatDef> workStats)
        {
            if (tool1 == null && tool2 == null) return 0f;
            if (tool1 == null) return -1f;
            if (tool2 == null) return 1f;

            return CalculateToolScore(tool1, pawn, workStats) - CalculateToolScore(tool2, pawn, workStats);
        }

        /// <summary>
        /// Check if two tools are considered the same "type" for replacement purposes.
        /// Tools are same type if they improve overlapping stats or have similar purposes.
        /// </summary>
        public static bool AreSameToolType(SurvivalTool tool1, SurvivalTool tool2)
        {
            if (tool1?.def == null || tool2?.def == null) return false;
            if (tool1.def == tool2.def) return true;

            // Check if they improve the same stats
            var factors1 = GetEffectiveWorkStatFactors(tool1).Where(f => f.value > GetNoToolBaseline(f.stat) + 0.001f);
            var factors2 = GetEffectiveWorkStatFactors(tool2).Where(f => f.value > GetNoToolBaseline(f.stat) + 0.001f);

            var stats1 = new HashSet<StatDef>(factors1.Select(f => f.stat));
            var stats2 = new HashSet<StatDef>(factors2.Select(f => f.stat));

            return stats1.Overlaps(stats2);
        }

        /// <summary>
        /// Determine if a tool improves any of the specified required stats.
        /// </summary>
        public static bool ToolImprovesAnyRequiredStat(SurvivalTool tool, List<StatDef> requiredStats)
        {
            if (tool?.def == null || requiredStats?.Any() != true) return false;

            var factors = GetEffectiveWorkStatFactors(tool);
            return requiredStats.Any(stat => factors.GetStatFactorFromList(stat) > GetNoToolBaseline(stat) + 0.001f);
        }

        /// <summary>
        /// Check if a tool is considered a "multitool" (improves 3+ different stats).
        /// </summary>
        public static bool IsMultitool(SurvivalTool tool)
        {
            if (tool?.def == null) return false;

            var factors = GetEffectiveWorkStatFactors(tool);
            return factors.Count(f => f.value > GetNoToolBaseline(f.stat) + 0.001f) >= 3;
        }

        #region Private helpers

        /// <summary>
        /// Get the effective work stat factors for a tool, accounting for stuff and degradation.
        /// </summary>
        private static IEnumerable<StatModifier> GetEffectiveWorkStatFactors(SurvivalTool tool)
        {
            if (tool?.def == null) return Enumerable.Empty<StatModifier>();

            try
            {
                return SurvivalToolUtility.CalculateWorkStatFactors(tool) ?? Enumerable.Empty<StatModifier>();
            }
            catch
            {
                // Fail gracefully if calculation fails
                return Enumerable.Empty<StatModifier>();
            }
        }

        /// <summary>
        /// Get quality multiplier for tool scoring (better quality = higher score).
        /// </summary>
        private static float QualityMultiplier(QualityCategory quality)
        {
            switch (quality)
            {
                case QualityCategory.Awful: return 0.8f;
                case QualityCategory.Poor: return 0.9f;
                case QualityCategory.Normal: return 1.0f;
                case QualityCategory.Good: return 1.1f;
                case QualityCategory.Excellent: return 1.2f;
                case QualityCategory.Masterwork: return 1.35f;
                case QualityCategory.Legendary: return 1.5f;
                default: return 1.0f;
            }
        }

        /// <summary>
        /// Get the baseline factor for a stat when no tools are equipped.
        /// This is the comparison point for determining if a tool provides improvement.
        /// Accounts for user-configurable penalties and stat-specific exemptions.
        /// </summary>
        private static float GetNoToolBaseline(StatDef stat)
        {
            if (stat?.parts == null) return 1f; // Default if no stat parts

            // Find the StatPart_SurvivalTool for this stat
            foreach (var part in stat.parts)
            {
                if (part is StatPart_SurvivalTool survivalPart)
                {
                    // Use the public NoToolStatFactor property which handles user settings
                    return survivalPart.NoToolStatFactor;
                }
            }

            return 1f; // Default if no SurvivalTool stat part found
        }

        #endregion
    }
}
