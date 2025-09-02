using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using SurvivalTools.Compat;

namespace SurvivalTools
{
    public class Alert_ColonistNeedsSurvivalTool : Alert
    {
        private List<Pawn> _culprits;
        private int _lastCulpritCalcTick = -9999;
        private const int CulpritRecalcIntervalTicks = 60; // how often the list is refreshed (ticks)

        private List<Pawn> Culprits
        {
            get
            {
                int now = Find.TickManager.TicksGame;
                if (_culprits == null || now - _lastCulpritCalcTick >= CulpritRecalcIntervalTicks)
                {
                    _lastCulpritCalcTick = now;
                    _culprits = PawnsFinder.AllMaps_FreeColonistsSpawned
                        .Where(p => p.Spawned && p.CanUseSurvivalTools() && IsWorkingToolless(p))
                        .ToList();
                }
                return _culprits;
            }
        }

        private static bool IsWorkingToolless(Pawn pawn)
        {
            var settings = SurvivalTools.Settings;
            if (settings == null) return false;

            // In hardcore mode, show alerts for all missing tools including optional ones (cleaning, butchery)
            // In normal mode, only show alerts for stats that would actually block work (skip purely optional cleaning/butchery)
            var stats = settings.hardcoreMode
                ? pawn.AssignedToolRelevantWorkGiversStatDefsForAlerts()
                : pawn.AssignedToolRelevantWorkGiversStatDefs();

            if (stats.NullOrEmpty()) return false;

            foreach (var stat in stats)
            {
                // For normal (non-hardcore) mode: skip purely optional stats like cleaning/butchery.
                if (!settings.hardcoreMode)
                {
                    if (stat == ST_StatDefOf.CleaningSpeed ||
                        stat == ST_StatDefOf.ButcheryFleshSpeed ||
                        stat == ST_StatDefOf.ButcheryFleshEfficiency)
                    {
                        continue;
                    }
                }

                // If there are no tools in the game that provide this stat, skip it (avoid impossible alerts)
                if (!SurvivalToolUtility.ToolsExistForStat(stat))
                    continue;

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
                        // Use a descriptive label based on stat category (more user-friendly)
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
            if (requiredStats == null) return workGiverDef?.label ?? "work";

            // Category checks
            var hasButcheryStats = requiredStats.Any(s => s == ST_StatDefOf.ButcheryFleshSpeed || s == ST_StatDefOf.ButcheryFleshEfficiency);
            var hasMedicalStats = requiredStats.Any(s => s == ST_StatDefOf.MedicalOperationSpeed || s == ST_StatDefOf.MedicalSurgerySuccessChance);
            var hasCleaningStats = requiredStats.Any(s => s == ST_StatDefOf.CleaningSpeed);

            // Research Reinvented compatibility stats
            var hasResearchStats = requiredStats.Any(s => s?.defName == "ResearchSpeed");
            var hasFieldResearchStats = requiredStats.Any(s => s?.defName == "FieldResearchSpeedMultiplier");

            if (hasButcheryStats)
                return "butchering";
            if (hasMedicalStats)
                return "medical work";
            if (hasCleaningStats)
                return "cleaning";

            // Research Reinvented compatibility
            if (hasFieldResearchStats && hasResearchStats)
                return "field research";
            if (hasFieldResearchStats)
                return "field research";
            if (hasResearchStats)
                return "research";

            // Specific stat fallbacks
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

            // Safe fallback for possible null workGiverDef
            if (workGiverDef == null)
                return "work";

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
            var culprits = Culprits;
            int count = culprits.Count;

            // Check if any culprits need Research Reinvented tools specifically
            bool hasRRNeeds = false;
            if (CompatAPI.IsResearchReinventedActive)
            {
                hasRRNeeds = culprits.Any(p =>
                {
                    var givers = p.AssignedToolRelevantWorkGivers();
                    if (givers == null) return false;

                    return givers.Any(g =>
                    {
                        var ext = g.def.GetModExtension<WorkGiverExtension>();
                        return ext?.requiredStats?.Any(s =>
                            s?.defName == "ResearchSpeed" ||
                            s?.defName == "FieldResearchSpeedMultiplier") == true;
                    });
                });
            }

            // Use special title for Research Reinvented needs
            if (hasRRNeeds)
            {
                return (count <= 1 ? "ColonistNeedsSurvivalTool" : "ColonistsNeedSurvivalTool").Translate(count) + " (" + "Compat_ReinventedResearchAlert".Translate() + ")";
            }

            return (count <= 1 ? "ColonistNeedsSurvivalTool" : "ColonistsNeedSurvivalTool").Translate(count);
        }

        public override AlertReport GetReport()
        {
            return Culprits.Any() ? AlertReport.CulpritsAre(Culprits) : AlertReport.Inactive;
        }
    }
}
