using Verse;
using RimWorld;

namespace SurvivalTools
{
    public class StatPart_SurvivalTool : StatPart
    {
        public override string ExplanationPart(StatRequest req)
        {
            // AI "cheats" until tool generation is finalized
            if (req.Thing is Pawn pawn && pawn.CanUseSurvivalTools())
            {
                if (pawn.HasSurvivalToolFor(parentStat, out SurvivalTool tool, out float statFactor))
                    return $"{tool.LabelCapNoCount}: x{statFactor.ToStringPercent()}";

                return $"{"NoTool".Translate()}: x{NoToolStatFactor.ToStringPercent()}";
            }
            return null;
        }

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (req.Thing is Pawn pawn && pawn.CanUseSurvivalTools())
            {
                if (pawn.HasSurvivalToolFor(parentStat, out SurvivalTool tool, out float statFactor))
                    val *= statFactor;
                else
                    val *= NoToolStatFactor;
            }
        }

        // Use your settings object, not the old helper
        public float NoToolStatFactor =>
            (SurvivalTools.Settings != null && SurvivalTools.Settings.hardcoreMode)
                ? NoToolStatFactorHardcore
                : noToolStatFactor;

        // Defaults (can be tuned in code; make public if you want XML tuning)
        private float noToolStatFactor = 0.3f;

        private float noToolStatFactorHardcore = -1f;
        private float NoToolStatFactorHardcore =>
            (noToolStatFactorHardcore != -1f) ? noToolStatFactorHardcore : noToolStatFactor;
    }
}
