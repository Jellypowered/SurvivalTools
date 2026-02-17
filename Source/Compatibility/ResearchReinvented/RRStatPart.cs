// RimWorld 1.6 / C# 7.3
// Adds a Normal-mode penalty to ResearchSpeed when RR is active and pawn has no research tool.
// No effect in Hardcore (progress is zeroed) or Nightmare (hard-gated).

using System;
using RimWorld;
using Verse;


namespace SurvivalTools.Compat.RR
{
    public sealed class StatPart_RR_NoToolPenalty : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                var pawn = req.Thing as Pawn;
                if (pawn == null) return;
                if (!pawn.RaceProps.Humanlike) return;               // animals
                if (SurvivalTools.Helpers.PawnToolValidator.IsMechanoidOrInherited(pawn)) return; // mechs/colonist mechs (comprehensive check)
                if (!RRHelpers.IsActive()) return;                    // RR not present
                if (!RRHelpers.ShouldApplyNormalPenalty()) return;    // only Normal
                if (RRHelpers.PawnHasResearchTool(pawn)) return;      // has tool => no penalty

                val *= RRHelpers.NoToolPenalty();                     // default ~0.6f
            }
            catch { /* never break stat calc */ }
        }

        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                var pawn = req.Thing as Pawn;
                if (pawn == null) return null;
                if (!pawn.RaceProps.Humanlike) return null;
                if (SurvivalTools.Helpers.PawnToolValidator.IsMechanoidOrInherited(pawn)) return null; // comprehensive mech check
                if (!RRHelpers.IsActive()) return null;
                if (!RRHelpers.ShouldApplyNormalPenalty()) return null;
                if (RRHelpers.PawnHasResearchTool(pawn)) return null;

                var f = RRHelpers.NoToolPenalty();
                return $"No research tool (Research Reinvented): x{f:0.##}";
            }
            catch { return null; }
        }
    }
}
