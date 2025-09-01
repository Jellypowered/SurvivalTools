using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace SurvivalTools
{
    public class Alert_ColonistNeedsSurvivalTool : Alert
    {
        private List<Pawn> _culprits;

        private List<Pawn> Culprits
        {
            get
            {
                if (_culprits == null || Find.TickManager.TicksGame % 60 == 0)
                {
                    _culprits = PawnsFinder.AllMaps_FreeColonistsSpawned
                        .Where(p => p.Spawned && p.CanUseSurvivalTools() && IsWorkingToolless(p))
                        .ToList();
                }
                return _culprits;
            }
        }

        private static bool IsWorkingToolless(Pawn pawn)
        {
            // In hardcore mode, show alerts for all missing tools including optional ones (cleaning, butchery)
            // In normal mode, only show alerts for tools that would actually block work
            var stats = SurvivalTools.Settings?.hardcoreMode == true
                ? pawn.AssignedToolRelevantWorkGiversStatDefsForAlerts()
                : pawn.AssignedToolRelevantWorkGiversStatDefs();

            if (stats.NullOrEmpty()) return false;

            foreach (var stat in stats)
            {
                if (!pawn.HasSurvivalToolFor(stat))
                    return true;
            }
            return false;
        }

        private static string GetToollessWorkTypesString(Pawn pawn)
        {
            var types = new HashSet<string>();
            var givers = pawn.AssignedToolRelevantWorkGivers();
            if (givers == null) return string.Empty;

            foreach (var giver in givers)
            {
                var ext = giver.def.GetModExtension<WorkGiverExtension>();
                if (ext?.requiredStats == null) continue;

                foreach (var stat in ext.requiredStats)
                {
                    // Only include stats that have tools available and the pawn lacks them
                    if (SurvivalToolUtility.ToolsExistForStat(stat) && !pawn.HasSurvivalToolFor(stat))
                    {
                        // Use custom labels based on stat categories rather than work type
                        var label = GetWorkTypeDisplayLabel(giver.def, ext.requiredStats);
                        types.Add(label);
                        break;
                    }
                }
            }

            return GenText.ToCommaList(types).CapitalizeFirst();
        }

        private static string GetWorkTypeDisplayLabel(WorkGiverDef workGiverDef, List<StatDef> requiredStats)
        {
            // Check what kind of stats are required and return appropriate labels
            var hasButcheryStats = requiredStats.Any(s => s == ST_StatDefOf.ButcheryFleshSpeed || s == ST_StatDefOf.ButcheryFleshEfficiency);
            var hasMedicalStats = requiredStats.Any(s => s == ST_StatDefOf.MedicalOperationSpeed || s == ST_StatDefOf.MedicalSurgerySuccessChance);
            var hasCleaningStats = requiredStats.Any(s => s == ST_StatDefOf.CleaningSpeed);

            // Use descriptive labels based on the tool category rather than work type
            if (hasButcheryStats)
                return "butchering";
            if (hasMedicalStats)
                return "medical work";
            if (hasCleaningStats)
                return "cleaning";

            // For other stats, use a generic description or fall back to work type
            var stat = requiredStats.FirstOrDefault();
            if (stat == ST_StatDefOf.TreeFellingSpeed)
                return "tree felling";
            if (stat == ST_StatDefOf.PlantHarvestingSpeed)
                return "plant harvesting";
            if (stat == ST_StatDefOf.SowingSpeed)
                return "sowing";
            if (stat == ST_StatDefOf.DiggingSpeed)
                return "mining";
            if (stat == ST_StatDefOf.ResearchSpeed)
                return "research";
            if (stat == StatDefOf.ConstructionSpeed)
                return "construction";
            if (stat == ST_StatDefOf.MaintenanceSpeed)
                return "maintenance";

            // Fallback to work type label
            return workGiverDef.workType?.gerundLabel ?? workGiverDef.label;
        }

        public override TaggedString GetExplanation()
        {
            var culprits = Culprits;
            if (culprits.NullOrEmpty())
                return TaggedString.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("ColonistNeedsSurvivalToolDesc".Translate());
            foreach (var p in culprits)
            {
                sb.AppendLine($"    • {p.LabelShort} ({GetToollessWorkTypesString(p)})");
            }

            return sb.ToString();
        }

        public override string GetLabel()
        {
            int count = Culprits.Count;
            return (count <= 1 ? "ColonistNeedsSurvivalTool" : "ColonistsNeedSurvivalTool").Translate(count);
        }

        public override AlertReport GetReport()
        {
            return Culprits.Any() ? AlertReport.CulpritsAre(Culprits) : AlertReport.Inactive;
        }
    }
}
