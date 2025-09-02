using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace SurvivalTools
{
    public class Alert_SurvivalToolNeedsReplacing : Alert
    {
        private const float DamagedToolRemainingLifespanThreshold = 0.5f;

        public Alert_SurvivalToolNeedsReplacing()
        {
            defaultPriority = AlertPriority.Medium;
        }

        private IEnumerable<Pawn> WorkersDamagedTools
        {
            get
            {
                foreach (var pawn in PawnsFinder.AllMaps_FreeColonistsSpawned)
                {
                    if (pawn != null && pawn.Spawned && pawn.CanUseSurvivalTools() && HasDamagedTools(pawn))
                        yield return pawn;
                }
            }
        }

        private static bool HasDamagedTools(Pawn pawn)
        {
            var toolsEnum = pawn.GetAllUsableSurvivalTools();
            if (toolsEnum == null) return false;

            foreach (var thing in toolsEnum)
            {
                // We only care about SurvivalTool wrappers (real or virtual)
                var tool = thing as SurvivalTool;
                if (tool == null) continue;

                // Only consider tools that are currently in use by the pawn
                if (!tool.InUse) continue;

                // Resolve the physical backing Thing when possible.
                // This handles virtual wrappers (e.g. cloth) and real SurvivalTool Things.
                Thing backingThing = SurvivalToolUtility.BackingThing(tool);

                // If no backing found, but the tool itself is a physical ThingWithComps, use that.
                ThingWithComps twc = null;
                if (backingThing is ThingWithComps btc)
                    twc = btc;
                else if (tool is ThingWithComps toolTwc)
                    twc = toolTwc;

                // If we don't have a ThingWithComps with HP, there's nothing to measure for replacement.
                if (twc == null) continue;

                // Only evaluate items that actually use hit points for degradation.
                if (!twc.def.useHitPoints) continue;

                float hpFrac = twc.MaxHitPoints > 0 ? (float)twc.HitPoints / twc.MaxHitPoints : 0f;
                float lifespanRemaining = twc.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan) * hpFrac;

                if (lifespanRemaining <= DamagedToolRemainingLifespanThreshold)
                    return true;
            }
            return false;
        }

        public override TaggedString GetExplanation()
        {
            var culprits = WorkersDamagedTools.ToList();
            if (culprits.Count == 0)
                return TaggedString.Empty;

            var sb = new StringBuilder();
            sb.Append("SurvivalToolNeedsReplacingDesc".Translate()).Append(":\n");
            for (int i = 0; i < culprits.Count; i++)
            {
                var p = culprits[i];
                sb.Append("\n    ").Append(p.LabelShort);
            }
            return sb.ToString();
        }

        public override string GetLabel() => "SurvivalToolsNeedReplacing".Translate();

        public override AlertReport GetReport()
        {
            if (!SurvivalToolUtility.IsToolDegradationEnabled)
                return AlertReport.Inactive;

            var culprits = WorkersDamagedTools.ToList();
            return culprits.Count == 0 ? AlertReport.Inactive : AlertReport.CulpritsAre(culprits);
        }
    }
}
