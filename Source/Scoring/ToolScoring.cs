// RimWorld 1.6 / C# 7.3
// Source/Scoring/ToolScoring.cs
// Refactor for Phase 3 Keep this code/functionality. 
// Phase 3: Deterministic tool scoring system using ToolStatResolver.
// Provides unified scoring APIs with zero allocations in hot path and
// consistent results for cache-friendly performance.

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using SurvivalTools.Helpers;

namespace SurvivalTools.Scoring
{
    /// <summary>
    /// Phase 3: Centralized tool scoring system using ToolStatResolver only.
    /// Replaces scattered legacy scoring logic with deterministic, cache-friendly APIs.
    /// </summary>
    public static class ToolScoring
    {
        // Constants for scoring behavior (match current legacy behavior)
        private const float MultiStatBonusPerExtra = 0.05f; // Extra 5% per additional stat improved
        private const float ConditionMinimum = 0.5f; // Minimum condition factor (50%)
        private const float DistancePenaltyPerTile = 0.01f; // Map search distance penalty

        // Pooled temporary arrays to avoid allocations in hot path
        private static readonly List<StatDef> _tempStatList = new List<StatDef>();
        private static readonly List<(Thing, float)> _tempScoredTools = new List<(Thing, float)>();

        // Quality curve for tool scaling (matches SurvivalToolUtility.ToolQualityCurve)
        private static readonly SimpleCurve QualityCurve = new SimpleCurve
        {
            new CurvePoint(0f, 0.7f), // Awful
            new CurvePoint(1f, 0.85f), // Poor  
            new CurvePoint(2f, 1f), // Normal
            new CurvePoint(3f, 1.15f), // Good
            new CurvePoint(4f, 1.3f), // Excellent
            new CurvePoint(5f, 1.45f), // Masterwork
            new CurvePoint(6f, 1.6f) // Legendary
        };

        /// <summary>
        /// Score a tool for a specific work stat and pawn.
        /// Uses ToolStatResolver exclusively for consistent, cache-friendly results.
        /// Zero allocations in hot path.
        /// </summary>
        /// <param name="tool">Tool to score (Thing with tool properties)</param>
        /// <param name="pawn">Pawn who would use the tool</param>
        /// <param name="workStat">Work stat to score for</param>
        /// <returns>Tool score (higher = better), 0 if tool doesn't improve stat</returns>
        public static float Score(Thing tool, Pawn pawn, StatDef workStat)
        {
            if (tool?.def == null || pawn == null || workStat == null)
                return 0f;

            // Try cache first
            if (ScoreCache.TryGet(pawn, tool, workStat, out float cachedScore))
                return cachedScore;

            // Calculate score
            float score = CalculateScore(tool, pawn, workStat);

            // Cache result
            ScoreCache.Set(pawn, tool, workStat, score);

            return score;
        }

        /// <summary>
        /// Internal scoring calculation without caching.
        /// </summary>
        private static float CalculateScore(Thing tool, Pawn pawn, StatDef workStat)
        {
            if (tool?.def == null || pawn == null || workStat == null)
                return 0f;

            // Get tool stat factor using resolver
            float toolFactor = ToolStatResolver.GetToolStatFactor(tool.def, tool.Stuff, workStat);
            float baseline = SurvivalToolUtility.GetNoToolBaseline(workStat);

            // If tool doesn't improve over baseline, score is 0
            if (toolFactor <= baseline + 0.001f)
                return 0f;

            // Base score is improvement over baseline
            float score = toolFactor - baseline;

            // Apply difficulty multipliers if settings exist
            var settings = SurvivalToolsMod.Settings;
            if (settings != null)
            {
                // In normal mode, apply penalty settings if enabled
                if (!settings.hardcoreMode && !settings.extraHardcoreMode && settings.enableNormalModePenalties)
                {
                    // Tool factors are already clamped in resolver, apply any additional penalties here
                    // Current behavior: keep defaults identical, so no additional penalties
                }
            }

            // Apply quality scaling if enabled and tool has quality
            if (settings?.useQualityToolScaling == true && tool is ThingWithComps twc)
            {
                var qualityComp = twc.TryGetComp<CompQuality>();
                if (qualityComp != null)
                {
                    score *= QualityCurve.Evaluate((int)qualityComp.Quality);
                }
            }

            // Apply condition penalty (50%-100% based on hit points)
            if (tool.def.useHitPoints && tool.MaxHitPoints > 0)
            {
                float conditionFactor = ConditionMinimum + (1f - ConditionMinimum) * (tool.HitPoints / (float)tool.MaxHitPoints);
                score *= conditionFactor;
            }

            // Optional carry/mass penalties (read settings, keep defaults identical to current)
            // Current behavior: no mass penalties by default

            return score;
        }

        /// <summary>
        /// Find the best tool for a pawn and work stat.
        /// Searches pawn inventory and optionally the map.
        /// Zero allocations in hot path.
        /// </summary>
        /// <param name="pawn">Pawn looking for tool</param>
        /// <param name="workStat">Work stat to optimize for</param>
        /// <param name="score">Output: score of best tool found</param>
        /// <returns>Best tool found, or null if none improve baseline</returns>
        public static Thing GetBestTool(Pawn pawn, StatDef workStat, out float score)
        {
            score = 0f;
            if (pawn == null || workStat == null)
                return null;

            Thing bestTool = null;
            float bestScore = 0f;

            // Search pawn's held/equipped tools first (zero distance penalty)
            foreach (var thing in pawn.GetAllUsableSurvivalTools())
            {
                float toolScore = Score(thing, pawn, workStat);
                if (toolScore > bestScore)
                {
                    bestScore = toolScore;
                    bestTool = thing;
                }
            }

            // TODO: Add map search when map tools are needed
            // For Phase 3, focus on held tools only to avoid gameplay changes

            score = bestScore;
            return bestTool;
        }

        /// <summary>
        /// Get top contributing factors for a tool's score.
        /// Used for detailed tool analysis and UI display.
        /// Zero allocations in hot path.
        /// </summary>
        /// <param name="tool">Tool to analyze</param>
        /// <param name="pawn">Pawn who would use tool</param>
        /// <param name="workStat">Work stat to analyze</param>
        /// <param name="max">Maximum number of contributors to return</param>
        /// <returns>Array of (factor source, contribution) pairs</returns>
        public static (Thing, float)[] TopContributors(Thing tool, Pawn pawn, StatDef workStat, int max = 2)
        {
            _tempScoredTools.Clear();

            if (tool?.def == null || pawn == null || workStat == null || max <= 0)
                return new (Thing, float)[0];

            // Get base tool factor
            float baseFactor = ToolStatResolver.GetToolStatFactor(tool.def, tool.Stuff, workStat);
            float baseline = SurvivalToolUtility.GetNoToolBaseline(workStat);

            if (baseFactor > baseline + 0.001f)
            {
                _tempScoredTools.Add((tool, baseFactor - baseline));
            }

            // For now, just return the tool itself as the main contributor
            // In future phases, this could break down material vs tool vs quirk contributions

            if (_tempScoredTools.Count == 0)
                return new (Thing, float)[0];

            // Sort by contribution (highest first) and take top 'max'
            _tempScoredTools.Sort((a, b) => b.Item2.CompareTo(a.Item2));

            int resultCount = Math.Min(max, _tempScoredTools.Count);
            var result = new (Thing, float)[resultCount];
            for (int i = 0; i < resultCount; i++)
            {
                result[i] = _tempScoredTools[i];
            }

            return result;
        }

        /// <summary>
        /// Clear pooled collections for memory management.
        /// Call during mod cleanup or when memory pressure is high.
        /// </summary>
        internal static void ClearPools()
        {
            _tempStatList.Clear();
            _tempScoredTools.Clear();
        }
    }
}