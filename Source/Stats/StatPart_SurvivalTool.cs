using System.Linq;
using Verse;
using RimWorld;

namespace SurvivalTools
{
    public class StatPart_SurvivalTool : StatPart
    {
        public override string ExplanationPart(StatRequest req)
        {
            if (parentStat == null) return null;

            if (req.Thing is Pawn pawn && pawn.CanUseSurvivalTools())
            {
                // First try: actual SurvivalTool instance (best tool)
                var bestTool = pawn.GetBestSurvivalTool(parentStat);
                if (bestTool != null)
                {
                    float factor = bestTool.WorkStatFactors.ToList().GetStatFactorFromList(parentStat);
                    return $"{bestTool.LabelCapNoCount}: x{factor.ToStringPercent()}";
                }

                // Second try: tool-stuff (cloth/wool/etc.) residing on pawn (inventory/equipment)
                var toolStuff = pawn.GetAllUsableSurvivalTools()
                                    .FirstOrDefault(t => t.def.IsToolStuff() &&
                                                         t.def.GetModExtension<SurvivalToolProperties>()?
                                                           .baseWorkStatFactors?.Any(m => m.stat == parentStat) == true);
                if (toolStuff != null)
                {
                    // get factor from the def's baseWorkStatFactors
                    var props = toolStuff.def.GetModExtension<SurvivalToolProperties>();
                    float factor = props?.baseWorkStatFactors.GetStatFactorFromList(parentStat) ?? NoToolStatFactor;
                    return $"{toolStuff.LabelCapNoCount}: x{factor.ToStringPercent()}";
                }

                return $"{"NoTool".Translate()}: x{NoToolStatFactor.ToStringPercent()}";
            }
            return null;
        }

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (parentStat == null) return;

            if (req.Thing is Pawn pawn && pawn.CanUseSurvivalTools())
            {
                // If there's a real SurvivalTool, use it
                var bestTool = pawn.GetBestSurvivalTool(parentStat);
                if (bestTool != null)
                {
                    float factor = bestTool.WorkStatFactors.ToList().GetStatFactorFromList(parentStat);
                    val *= factor;
                    return;
                }

                // Otherwise, check for tool-stuff
                var toolStuff = pawn.GetAllUsableSurvivalTools()
                                    .FirstOrDefault(t => t.def.IsToolStuff() &&
                                                         t.def.GetModExtension<SurvivalToolProperties>()?
                                                           .baseWorkStatFactors?.Any(m => m.stat == parentStat) == true);
                if (toolStuff != null)
                {
                    var props = toolStuff.def.GetModExtension<SurvivalToolProperties>();
                    float factor = props?.baseWorkStatFactors.GetStatFactorFromList(parentStat) ?? NoToolStatFactor;
                    val *= factor;
                    return;
                }

                // No tool at all
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
