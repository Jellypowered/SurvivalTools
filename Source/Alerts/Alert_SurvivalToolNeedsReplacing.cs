// RimWorld 1.6 / C# 7.3
// Alert_SurvivalToolNeedsReplacing.cs
//
// QoL: shows damaged tool % remaining and suggests researched replacements.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools
{
    public class Alert_SurvivalToolNeedsReplacing : Alert
    {
        private const float DamagedToolRemainingLifespanThreshold = 0.5f;

        public Alert_SurvivalToolNeedsReplacing()
        {
            defaultPriority = AlertPriority.Medium;
        }

        private IEnumerable<Pawn> WorkersDamagedTools =>
            PawnsFinder.AllMaps_FreeColonistsSpawned
                .Where(p => p.Spawned && PawnToolValidator.CanUseSurvivalTools(p) && HasDamagedTools(p));

        private static bool HasDamagedTools(Pawn pawn)
        {
            return pawn.GetAllUsableSurvivalTools()
                .OfType<SurvivalTool>()
                .Any(IsToolBelowThreshold);
        }

        private static bool IsToolBelowThreshold(SurvivalTool tool)
        {
            var twc = ResolveThingWithComps(tool);
            if (twc == null || !twc.def.useHitPoints) return false;

            float hpFrac = twc.MaxHitPoints > 0 ? (float)twc.HitPoints / twc.MaxHitPoints : 0f;
            float lifespanRemaining = twc.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan) * hpFrac;

            if (IsDebugLoggingEnabled)
            {
                LogDebug($"[SurvivalTools.AlertReplace] tool={tool.LabelShort} hpFrac={hpFrac:F2} lifespanRemaining={lifespanRemaining:F2}",
                    $"AlertReplace_{tool.def.defName}_{tool.thingIDNumber}");
            }

            return lifespanRemaining <= DamagedToolRemainingLifespanThreshold;
        }

        private static ThingWithComps ResolveThingWithComps(SurvivalTool tool)
        {
            var backingThing = SurvivalToolUtility.BackingThing(tool);
            if (backingThing is ThingWithComps btc) return btc;
            if (tool is ThingWithComps twc) return twc;
            return null;
        }

        private static string FormatToolLifespan(SurvivalTool tool)
        {
            var twc = ResolveThingWithComps(tool);
            if (twc == null || !twc.def.useHitPoints) return tool.LabelShort;

            float hpFrac = twc.MaxHitPoints > 0 ? (float)twc.HitPoints / twc.MaxHitPoints : 0f;
            float lifespanRemaining = twc.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan) * hpFrac;

            int percent = (int)(lifespanRemaining * 100f);
            return $"{tool.LabelShort} ({percent}%)";
        }

        public override TaggedString GetExplanation()
        {
            var culprits = WorkersDamagedTools.ToList();
            if (culprits.Count == 0) return TaggedString.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("SurvivalToolNeedsReplacingDesc".Translate());

            foreach (var p in culprits)
            {
                var failing = p.GetAllUsableSurvivalTools()
                    .OfType<SurvivalTool>()
                    .Where(IsToolBelowThreshold)
                    .ToList();

                if (failing.Count == 0) continue;

                sb.AppendLine($"\n{p.LabelShort}:");

                foreach (var tool in failing)
                {
                    var replacement = SurvivalToolDiscovery.GetBestReplacement(p, tool);
                    if (replacement != null)
                    {
                        sb.AppendLine($"  {FormatToolLifespan(tool)} → Replacement: {replacement.label}");
                    }
                    else
                    {
                        sb.AppendLine($"  {FormatToolLifespan(tool)}");
                    }
                }
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
