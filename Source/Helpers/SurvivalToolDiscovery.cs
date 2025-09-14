// RimWorld 1.6 / C# 7.3
// Source/Helpers/SurvivalToolDiscovery.cs
//
// Central helper for discovering survival tools, valid replacements, and
// research-gated tool availability. Used by alerts and optimizers.

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SurvivalTools.Helpers
{
    public static class SurvivalToolDiscovery
    {
        /// <summary>
        /// Returns all unlocked (researched) tool ThingDefs that contribute to the given stat.
        /// Includes modded tools that define SurvivalToolProperties with that stat.
        /// </summary>
        public static IEnumerable<ThingDef> GetToolsForStat(StatDef stat)
        {
            if (stat == null) yield break;

            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                var props = SurvivalToolProperties.For(def);
                if (!props.HasWorkStatFactors) continue;

                if (props.baseWorkStatFactors.Any(m => m?.stat == stat))
                {
                    // Skip if locked behind research
                    if (!IsUnlocked(def)) continue;
                    yield return def;
                }
            }
        }

        /// <summary>
        /// Given a pawn and a damaged tool, finds the best researched replacement available
        /// (higher stat factor for at least one stat the tool provides).
        /// Returns null if none found.
        /// </summary>
        public static ThingDef GetBestReplacement(Pawn pawn, SurvivalTool damagedTool)
        {
            if (pawn == null || damagedTool == null) return null;

            var stats = damagedTool.WorkStatFactors.Select(m => m.stat).Where(s => s != null).Distinct().ToList();
            if (stats.NullOrEmpty()) return null;

            // Evaluate score of the damaged tool
            float currentScore = ToolScore(damagedTool);

            // Scan all tools (real defs) that share at least one relevant stat
            var candidates = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => SurvivalToolProperties.For(d).HasWorkStatFactors &&
                            SurvivalToolProperties.For(d).baseWorkStatFactors.Any(m => stats.Contains(m.stat)) &&
                            IsUnlocked(d))
                .ToList();

            ThingDef best = null;
            float bestScore = currentScore;

            foreach (var def in candidates)
            {
                var props = SurvivalToolProperties.For(def);
                float score = props.baseWorkStatFactors.Where(m => stats.Contains(m.stat)).Sum(m => m.value);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = def;
                }
            }

            return best;
        }

        /// <summary>
        /// Returns true if the tool def is considered "researched/unlocked".
        /// Tools without research requirements are always unlocked.
        /// </summary>
        public static bool IsUnlocked(ThingDef def)
        {
            if (def?.recipeMaker == null) return true;

            var reqs = def.recipeMaker.researchPrerequisites;
            if (reqs.NullOrEmpty()) return true;

            return reqs.All(r => r != null && r.IsFinished);
        }

        /// <summary>
        /// Scores a tool (or virtual tool) based on its stat modifiers.
        /// Simple sum of values for its WorkStatFactors.
        /// </summary>
        private static float ToolScore(SurvivalTool tool)
        {
            if (tool == null) return 0f;
            return tool.WorkStatFactors?.Sum(m => m.value) ?? 0f;
        }
    }
}
