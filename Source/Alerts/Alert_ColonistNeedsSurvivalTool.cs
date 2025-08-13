using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace SurvivalTools
{
    public class Alert_ColonistNeedsSurvivalTool : Alert
    {
        public Alert_ColonistNeedsSurvivalTool()
        {
            defaultPriority = AlertPriority.High;
        }

        private IEnumerable<Pawn> ToollessWorkers
        {
            get
            {
                foreach (var pawn in PawnsFinder.AllMaps_FreeColonistsSpawned)
                {
                    if (pawn != null && pawn.Spawned && pawn.CanUseSurvivalTools() && WorkingToolless(pawn))
                        yield return pawn;
                }
            }
        }

        private static bool WorkingToolless(Pawn pawn)
        {
            var stats = pawn.AssignedToolRelevantWorkGiversStatDefs();
            if (stats == null) return false;

            foreach (var stat in stats)
            {
                if (!pawn.HasSurvivalToolFor(stat))
                    return true;
            }
            return false;
        }

        private static string ToollessWorkTypesString(Pawn pawn)
        {
            var types = new HashSet<string>();
            var givers = pawn.AssignedToolRelevantWorkGivers();
            if (givers != null)
            {
                foreach (var giver in givers)
                {
                    if (giver?.def == null) continue;

                    var ext = giver.def.GetModExtension<WorkGiverExtension>();
                    if (ext?.requiredStats == null) continue;

                    var label = giver.def.workType?.gerundLabel ?? giver.def.label;
                    foreach (var stat in ext.requiredStats)
                    {
                        if (!pawn.HasSurvivalToolFor(stat))
                            types.Add(label);
                    }
                }
            }

            return GenText.ToCommaList(types).CapitalizeFirst();
        }

        public override TaggedString GetExplanation()
        {
            var culprits = ToollessWorkers.ToList();
            if (culprits.Count == 0)
                return TaggedString.Empty;

            var sb = new StringBuilder();
            sb.Append("ColonistNeedsSurvivalToolDesc".Translate()).Append(":\n");
            for (int i = 0; i < culprits.Count; i++)
            {
                var p = culprits[i];
                sb.Append("\n    ")
                  .Append(p.LabelShort)
                  .Append(" (")
                  .Append(ToollessWorkTypesString(p))
                  .Append(")");
            }

            return sb.ToString();
        }

        public override string GetLabel()
        {
            int count = ToollessWorkers.Take(2).Count();
            return (count <= 1 ? "ColonistNeedsSurvivalTool" : "ColonistsNeedSurvivalTool").Translate();
        }

        public override AlertReport GetReport()
        {
            var culprits = ToollessWorkers.ToList();
            return culprits.Count == 0 ? AlertReport.Inactive : AlertReport.CulpritsAre(culprits);
        }
    }
}
