// RimWorld 1.6 / C# 7.3
// Alert_ColonistNeedsSurvivalTool.cs
//
// QoL: now lists which tools could satisfy missing stats, filtered by research unlocks.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using SurvivalTools.Compat;
using SurvivalTools.Helpers;

namespace SurvivalTools
{
    public class Alert_ColonistNeedsSurvivalTool : Alert
    {
        private List<Pawn> _culprits;
        private int _lastCulpritCalcTick = -9999;
        private const int CulpritRecalcIntervalTicks = 60;

        private List<Pawn> Culprits
        {
            get
            {
                int now = Find.TickManager.TicksGame;
                if (_culprits == null || now - _lastCulpritCalcTick >= CulpritRecalcIntervalTicks)
                {
                    _lastCulpritCalcTick = now;
                    _culprits = PawnsFinder.AllMaps_FreeColonistsSpawned
                        .Where(p => p.Spawned && PawnToolValidator.CanUseSurvivalTools(p) && IsWorkingToolless(p))
                        .ToList();
                }
                return _culprits;
            }
        }

        private static bool IsWorkingToolless(Pawn pawn)
        {
            var settings = SurvivalTools.Settings;
            if (settings == null) return false;

            var stats = settings.hardcoreMode
                ? pawn.AssignedToolRelevantWorkGiversStatDefsForAlerts()
                : pawn.AssignedToolRelevantWorkGiversStatDefs();

            if (stats.NullOrEmpty()) return false;

            foreach (var stat in stats)
            {
                if (!settings.hardcoreMode && StatFilters.IsOptionalStat(stat)) continue;
                if (!SurvivalToolUtility.ToolsExistForStat(stat)) continue;
                if (!ShouldShowAlertForStat(pawn, stat, settings)) continue;

                if (!pawn.HasSurvivalToolFor(stat))
                    return true;
            }
            return false;
        }

        private static string GetToollessWorkTypesString(Pawn pawn)
        {
            var sb = new StringBuilder();
            var givers = pawn.AssignedToolRelevantWorkGivers();
            if (givers == null) return string.Empty;

            foreach (var giver in givers)
            {
                var ext = giver.def.GetModExtension<WorkGiverExtension>();
                if (ext?.requiredStats == null) continue;

                var missing = ext.requiredStats
                    .Where(stat => SurvivalToolUtility.ToolsExistForStat(stat) && !pawn.HasSurvivalToolFor(stat))
                    .ToList();

                if (missing.Count == 0) continue;

                sb.AppendLine($"  {giver.def.label.CapitalizeFirst()}:");
                foreach (var stat in missing)
                {
                    var tools = SurvivalToolDiscovery.GetToolsForStat(stat).Select(d => d.label).Distinct().ToList();
                    if (tools.Any())
                        sb.AppendLine($"    {stat.label}: {string.Join(", ", tools)}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        public override TaggedString GetExplanation()
        {
            var culprits = Culprits;
            if (culprits.NullOrEmpty()) return TaggedString.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("ColonistNeedsSurvivalToolDesc".Translate());
            foreach (var p in culprits)
            {
                sb.AppendLine($"\n{p.LabelShort}:");
                sb.AppendLine(GetToollessWorkTypesString(p));
            }
            return sb.ToString();
        }

        public override string GetLabel()
        {
            var culprits = Culprits;
            int count = culprits.Count;

            if (CompatAPI.IsResearchReinventedActive)
            {
                bool hasRRNeeds = culprits.Any(p =>
                    p.AssignedToolRelevantWorkGivers()?.Any(g =>
                        g.def.GetModExtension<WorkGiverExtension>()?.requiredStats?
                        .Any(s => s?.defName == "ResearchSpeed" || s?.defName == "FieldResearchSpeedMultiplier") == true) == true);

                if (hasRRNeeds)
                {
                    return (count <= 1 ? "ColonistNeedsSurvivalTool" : "ColonistsNeedSurvivalTool").Translate(count)
                           + " (" + "Compat_ReinventedResearchAlert".Translate() + ")";
                }
            }

            return (count <= 1 ? "ColonistNeedsSurvivalTool" : "ColonistsNeedSurvivalTool").Translate(count);
        }

        public override AlertReport GetReport()
        {
            return Culprits.Any() ? AlertReport.CulpritsAre(Culprits) : AlertReport.Inactive;
        }

        private static bool ShouldShowAlertForStat(Pawn pawn, StatDef stat, SurvivalToolsSettings settings)
        {
            if (settings.hardcoreMode) return true;
            if (!settings.enableNormalModePenalties) return false;
            if (StatFilters.IsOptionalStat(stat)) return false;

            var currentEfficiency = pawn.GetStatValue(stat);
            var baseEfficiency = stat.defaultBaseValue;
            var penaltyThreshold = settings.noToolStatFactorNormal;

            var efficiencyRatio = currentEfficiency / baseEfficiency;
            return efficiencyRatio > penaltyThreshold * 1.2f;
        }
    }
}
