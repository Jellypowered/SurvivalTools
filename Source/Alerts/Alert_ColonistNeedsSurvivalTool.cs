// RimWorld 1.6 / C# 7.3
// Alert_ColonistNeedsSurvivalTool.cs
//
// Rewritten for clarity:
// - Grouped by stat (Construction, Medical, Plants, etc.)
// - Collapses medical stats into "Medical"
// - Shows pawns per stat with overflow ("…and (X) others")
// - Upgrade suggestions shown once per stat (if enabled)
// - Excludes Glitterworld Multitool from suggestions
// - Cached culprits, minimal LINQ, safe fallbacks
// - RR tag preserved

using System;
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
        private const int MaxPawnsToShow = 5;

        private static readonly StatDef Stat_WorkSpeedGlobal =
            DefDatabase<StatDef>.GetNamedSilentFail("WorkSpeedGlobal") ??
            DefDatabase<StatDef>.GetNamedSilentFail("GlobalWorkSpeed");

        public override AlertPriority Priority => AlertPriority.Medium;

        private List<Pawn> Culprits
        {
            get
            {
                int now = Find.TickManager?.TicksGame ?? 0;
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
            if (settings == null || pawn == null) return false;

            var stats = settings.hardcoreMode
                ? pawn.AssignedToolRelevantWorkGiversStatDefsForAlerts()
                : pawn.AssignedToolRelevantWorkGiversStatDefs();

            bool anyMissingRequired = false;

            if (stats != null)
            {
                foreach (var stat in stats)
                {
                    if (stat == null) continue;
                    if (!settings.hardcoreMode && StatFilters.IsOptionalStat(stat)) continue;
                    if (!SurvivalToolUtility.ToolsExistForStat(stat)) continue;
                    if (!ShouldShowAlertForStat(pawn, stat, settings)) continue;

                    if (!pawn.HasSurvivalToolFor(stat))
                    {
                        anyMissingRequired = true;
                        break;
                    }
                }
            }

            bool globalPenalty = false;
            if (Stat_WorkSpeedGlobal != null && ShouldShowAlertForStat(pawn, Stat_WorkSpeedGlobal, settings))
            {
                if (!pawn.HasSurvivalToolFor(Stat_WorkSpeedGlobal))
                    globalPenalty = true;
            }

            return anyMissingRequired || globalPenalty;
        }

        public override TaggedString GetExplanation()
        {
            var culprits = Culprits;
            if (culprits.NullOrEmpty()) return TaggedString.Empty;

            var settings = SurvivalTools.Settings;
            var sb = new StringBuilder(256);
            sb.AppendLine("ColonistNeedsSurvivalToolDesc".Translate());

            // Group missing stats -> pawns
            var statToPawns = new Dictionary<string, List<Pawn>>();
            var statToSuggestions = new Dictionary<string, List<string>>();

            foreach (var pawn in culprits)
            {
                var givers = pawn.AssignedToolRelevantWorkGivers();
                if (givers == null) continue;

                foreach (var giver in givers)
                {
                    var ext = giver?.def?.GetModExtension<WorkGiverExtension>();
                    if (ext?.requiredStats == null) continue;

                    foreach (var stat in ext.requiredStats)
                    {
                        if (stat == null) continue;
                        if (!settings.hardcoreMode && StatFilters.IsOptionalStat(stat)) continue;
                        if (!SurvivalToolUtility.ToolsExistForStat(stat)) continue;
                        if (!ShouldShowAlertForStat(pawn, stat, settings)) continue;
                        if (pawn.HasSurvivalToolFor(stat)) continue;

                        string label = NormalizeStatLabel(stat);
                        if (!statToPawns.TryGetValue(label, out var list))
                        {
                            list = new List<Pawn>();
                            statToPawns[label] = list;
                        }
                        list.Add(pawn);

                        if (settings.showUpgradeSuggestions)
                        {
                            var tools = SurvivalToolDiscovery.GetToolsForStat(stat)
                            .Where(t => t != null)
                            // Exclude Glitterworld Multitool by defName OR by label
                            .Where(t => !t.defName.Contains("Glitterworld", StringComparison.OrdinalIgnoreCase)
                            && !t.label.ToLower().Contains("glitterworld"))
                            .Where(ResearchUnlocks.IsToolResearchUnlocked)
                            .Select(t => t.label)
                            .Distinct()
                            .Take(3)
                            .ToList();


                            if (tools.Any())
                                statToSuggestions[label] = tools;
                        }
                    }
                }
            }

            // ALSO include the global work-speed stat if applicable — it isn't tied to a specific WorkGiver
            if (Stat_WorkSpeedGlobal != null)
            {
                string globalLabel = NormalizeStatLabel(Stat_WorkSpeedGlobal);
                foreach (var pawn in culprits)
                {
                    if (!ShouldShowAlertForStat(pawn, Stat_WorkSpeedGlobal, settings))
                        continue;
                    if (pawn.HasSurvivalToolFor(Stat_WorkSpeedGlobal))
                        continue;

                    if (!statToPawns.TryGetValue(globalLabel, out var list))
                    {
                        list = new List<Pawn>();
                        statToPawns[globalLabel] = list;
                    }
                    if (!list.Contains(pawn)) list.Add(pawn);

                    if (settings.showUpgradeSuggestions)
                    {
                        var tools = SurvivalToolDiscovery.GetToolsForStat(Stat_WorkSpeedGlobal)
                            .Where(t => t != null)
                            // Exclude Glitterworld Multitool
                            .Where(t => !t.defName.Contains("Glitterworld", StringComparison.OrdinalIgnoreCase)
                                        && !t.label.ToLower().Contains("glitterworld"))
                            .Where(ResearchUnlocks.IsToolResearchUnlocked)
                            .Select(t => t.label)
                            .Distinct()
                            .Take(3)
                            .ToList();

                        if (tools.Any())
                            statToSuggestions[globalLabel] = statToSuggestions.ContainsKey(globalLabel)
                                ? statToSuggestions[globalLabel].Union(tools).Distinct().Take(3).ToList()
                                : tools;
                    }
                }
            }

            // Write grouped output
            foreach (var kv in statToPawns.OrderBy(kv => kv.Key))
            {
                string statLabel = kv.Key;
                var pawns = kv.Value.Distinct().ToList();
                if (pawns.Count == 0) continue;

                sb.AppendLine();
                sb.Append($"{statLabel}: ");

                var pawnNames = pawns.Take(MaxPawnsToShow).Select(p => p.LabelShort).ToList();
                sb.Append(string.Join(", ", pawnNames));

                if (pawns.Count > MaxPawnsToShow)
                    sb.Append($" and ({pawns.Count - MaxPawnsToShow}) others");

                if (settings.showUpgradeSuggestions && statToSuggestions.TryGetValue(statLabel, out var suggs))
                {
                    sb.AppendLine();
                    sb.Append("  (" + string.Join(", ", suggs) + ")");
                }
            }

            return sb.ToString().TrimEnd();
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
                    return (count <= 1
                            ? "ColonistNeedsSurvivalTool".Translate(count)
                            : "ColonistsNeedSurvivalTool".Translate(count))
                           + " (" + "Compat_ReinventedResearchAlert".Translate() + ")";
                }
            }

            return (count <= 1
                ? "ColonistNeedsSurvivalTool".Translate(count)
                : "ColonistsNeedSurvivalTool".Translate(count));
        }

        public override AlertReport GetReport()
        {
            var culprits = Culprits;
            return (culprits != null && culprits.Count > 0)
                ? AlertReport.CulpritsAre(culprits)
                : AlertReport.Inactive;
        }

        private static bool ShouldShowAlertForStat(Pawn pawn, StatDef stat, SurvivalToolsSettings settings)
        {
            if (stat == null || pawn == null || settings == null) return false;
            if (settings.hardcoreMode) return true;
            if (!settings.enableNormalModePenalties) return false;
            if (StatFilters.IsOptionalStat(stat)) return false;

            float baseEfficiency = Math.Max(1f, stat.defaultBaseValue);
            float current = Math.Max(0f, pawn.GetStatValue(stat));
            float ratio = current / baseEfficiency;

            float threshold = Math.Max(0.01f, settings.noToolStatFactorNormal);
            return ratio <= (threshold + 0.05f);
        }

        /// <summary>
        /// Normalizes certain stats to shared labels (e.g. all medical stats -> "Medical")
        /// </summary>
        private static string NormalizeStatLabel(StatDef stat)
        {
            if (stat == null) return "Unknown";
            string name = stat.defName;

            if (name == "MedicalOperationSpeed" || name == "MedicalSurgerySuccessChance" || name == "MedicalTendQuality")
                return "Medical";

            if (stat == Stat_WorkSpeedGlobal)
                return "Global";

            return stat.label.CapitalizeFirst();
        }
    }

    internal static class ResearchUnlocks
    {
        public static bool IsToolResearchUnlocked(ThingDef toolDef)
        {
            if (toolDef == null) return false;
            var rp = toolDef.researchPrerequisites;
            if (rp.NullOrEmpty()) return true;
            foreach (var proj in rp)
                if (proj != null && !proj.IsFinished) return false;
            return true;
        }
    }
}
