// RimWorld 1.6 / C# 7.3
// Source/Legacy/LegacyScoringForwarders.cs
// Phase 9: Obsolete scoring API forwarders to maintain backward compatibility for external mods.
// These provide stable symbols that redirect to the refactored Scoring.ToolScoring / ToolStatResolver pipeline.

using System;
using RimWorld;
using Verse;

namespace SurvivalTools
{
    // Unified forwarder; simplified per new spec. Provides minimal stable surface for external mods.
    [Obsolete("Use SurvivalTools.Scoring.ToolScoring instead.", false)]
    public static class ToolScoreUtility
    {
        public static float Score(Thing tool, Pawn pawn, StatDef workStat)
            => Scoring.ToolScoring.Score(tool, pawn, workStat);

        public static Thing GetBestTool(Pawn pawn, StatDef workStat, out float score)
            => Scoring.ToolScoring.GetBestTool(pawn, workStat, out score);

        // Legacy shim: some older callers expected a direct factor query by passing an actual tool.
        // We just reuse the scoring API (already normalized) to avoid duplicating resolver logic.
        public static float GetToolStatFactor(Thing tool, Pawn pawn, StatDef workStat)
            => Scoring.ToolScoring.Score(tool, pawn, workStat);

        // Legacy shim: best tool + factor (factor was historically the score / improvement metric).
        public static Thing GetBestToolWithFactor(Pawn pawn, StatDef workStat, out float factor)
        {
            var best = Scoring.ToolScoring.GetBestTool(pawn, workStat, out var score);
            factor = score;
            return best;
        }

        public static string[] TopContributors(Thing tool, Pawn pawn, StatDef workStat, int max = 2)
        {
            var pairs = Scoring.ToolScoring.TopContributors(tool, pawn, workStat, max);
            if (pairs == null || pairs.Length == 0) return new string[0];
            var arr = new string[pairs.Length];
            for (int i = 0; i < pairs.Length; i++)
            {
                var (thing, contribution) = pairs[i];
                arr[i] = (thing?.LabelCap ?? "<null>") + ":" + contribution.ToString("0.###");
            }
            return arr;
        }
    }
}

namespace SurvivalTools.Legacy
{
    // Keep alternate namespace alias with simplified surface.
    using Root = global::SurvivalTools.ToolScoreUtility;
    [Obsolete("Use SurvivalTools.Scoring.ToolScoring instead.", false)]
    public static class ToolScoreUtility
    {
        public static float Score(Thing tool, Pawn pawn, StatDef workStat) => Root.Score(tool, pawn, workStat);
        public static Thing GetBestTool(Pawn pawn, StatDef workStat, out float score) => Root.GetBestTool(pawn, workStat, out score);
        public static float GetToolStatFactor(Thing tool, Pawn pawn, StatDef workStat) => Root.GetToolStatFactor(tool, pawn, workStat);
        public static Thing GetBestToolWithFactor(Pawn pawn, StatDef workStat, out float factor) => Root.GetBestToolWithFactor(pawn, workStat, out factor);
        public static string[] TopContributors(Thing tool, Pawn pawn, StatDef workStat, int max = 2) => Root.TopContributors(tool, pawn, workStat, max);
    }
}
