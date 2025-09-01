using Verse;
using RimWorld;

namespace SurvivalTools
{
    public class StatPart_SurvivalTool : StatPart
    {
        public override string ExplanationPart(StatRequest req)
        {
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
                if (pawn.HasSurvivalToolFor(parentStat, out _, out float statFactor))
                    val *= statFactor;
                else
                    val *= NoToolStatFactor;
            }
        }

        public float NoToolStatFactor =>
            SurvivalToolUtility.IsHardcoreModeEnabled ? noToolStatFactorHardcore : noToolStatFactor;

        // Default factor when no tool is used (non-hardcore).
        private float noToolStatFactor = 0.3f;

        // Factor when no tool is used in hardcore mode (usually 0).
        private float noToolStatFactorHardcore = 0f;
    }
}
