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
                var tool = thing as SurvivalTool;
                if (tool == null) continue;
                if (!tool.InUse) continue;

                float hpFrac = tool.MaxHitPoints > 0 ? (float)tool.HitPoints / tool.MaxHitPoints : 0f;
                float lifespanRemaining = tool.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan) * hpFrac;

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
            if (SurvivalTools.Settings == null || SurvivalTools.Settings.toolDegradationFactor <= 0.001f)
                return AlertReport.Inactive;

            var culprits = WorkersDamagedTools.ToList();
            return culprits.Count == 0 ? AlertReport.Inactive : AlertReport.CulpritsAre(culprits);
        }
    }
}
