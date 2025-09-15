// RimWorld 1.6 / C# 7.3
// Source/Alerts/Alert_SurvivalToolNeedsReplacing.cs
//
// QoL: shows damaged tool % remaining and suggests researched replacements.

using System;
using System.Collections.Generic;
using System.Linq; // retained for non-hot path grouping in explanation only
using System.Text;
using RimWorld;
using Verse;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools
{
    public class Alert_SurvivalToolNeedsReplacing : Alert
    {
        // Threshold expressed as remaining HP fraction (0..1). Tools at or below this
        // HP fraction will trigger the replacing alert. Using HP% is clearer for players
        // than the internal "lifespan" estimate which combined multiple factors.
        private const float DamagedToolHpFractionThreshold = 0.5f;

        public Alert_SurvivalToolNeedsReplacing()
        {
            defaultPriority = AlertPriority.Medium;
        }

        private IEnumerable<Pawn> WorkersDamagedTools
        {
            get
            {
                var all = PawnsFinder.AllMaps_FreeColonistsSpawned;
                for (int i = 0; i < all.Count; i++)
                {
                    var p = all[i];
                    if (p == null || !p.Spawned) continue;
                    if (!PawnToolValidator.CanUseSurvivalTools(p)) continue;
                    if (HasDamagedTools(p)) yield return p;
                }
            }
        }

        private static bool HasDamagedTools(Pawn pawn)
        {
            var toolsEnum = pawn.GetAllUsableSurvivalTools();
            if (toolsEnum == null) return false;
            foreach (var t in toolsEnum)
            {
                SurvivalTool st = t as SurvivalTool;
                if (st == null && t?.def != null && t.def.IsToolStuff()) st = VirtualTool.FromThing(t);
                if (st == null) continue;
                if (IsToolBelowThreshold(st)) return true;
            }
            return false;
        }

        private static bool IsToolBelowThreshold(SurvivalTool tool)
        {
            var twc = ResolveThingWithComps(tool);
            if (twc == null || !twc.def.useHitPoints) return false;

            // Use direct HP fraction for thresholding. This is more intuitive and stable
            // than combining lifespan estimates with HP.
            float hpFrac = twc.MaxHitPoints > 0 ? (float)twc.HitPoints / twc.MaxHitPoints : 0f;
            hpFrac = Math.Max(0f, Math.Min(1f, hpFrac));
            return hpFrac <= DamagedToolHpFractionThreshold;
        }

        private static ThingWithComps ResolveThingWithComps(SurvivalTool tool)
        {
            // Prefer an explicit SourceThing for virtual wrappers (most accurate for inventory stacks)
            if (tool is VirtualTool vtool)
            {
                if (vtool.SourceThing is ThingWithComps srcTwc) return srcTwc;
            }

            var backingThing = SurvivalToolUtility.BackingThing(tool);
            if (backingThing is ThingWithComps btc) return btc;
            if (tool is ThingWithComps twc) return twc;
            return null;
        }

        private static string FormatToolLifespan(SurvivalTool tool)
        {
            var twc = ResolveThingWithComps(tool);
            if (twc == null || !twc.def.useHitPoints) return tool.LabelShort;
            // Display the actual HP / MaxHP and a human-friendly percentage (HP%). This
            // replaces the previous "lifespan" presentation so players see the concrete
            // condition of the item.
            float hpFrac = twc.MaxHitPoints > 0 ? (float)twc.HitPoints / twc.MaxHitPoints : 0f;
            hpFrac = Math.Max(0f, Math.Min(1f, hpFrac));
            return $"{tool.LabelShort} - Condition: {twc.HitPoints}/{twc.MaxHitPoints} ({hpFrac:P0})";
        }

        public override TaggedString GetExplanation()
        {
            var culprits = WorkersDamagedTools.ToList();
            if (culprits.Count == 0) return TaggedString.Empty;
            // Build concise grouped explanation like:
            // (2) 30% - Hammer, Axe
            var sb = new StringBuilder();
            sb.AppendLine("SurvivalToolNeedsReplacingDesc".Translate());

            // Map from tool summary -> list of pawns
            var groups = new Dictionary<string, List<string>>();

            foreach (var p in culprits)
            {
                if (p == null) continue;
                var failing = p.GetAllUsableSurvivalTools()
                    .OfType<SurvivalTool>()
                    .Where(IsToolBelowThreshold)
                    .ToList();

                if (failing.Count == 0) continue;

                foreach (var tool in failing)
                {
                    var summary = FormatToolLifespan(tool);
                    if (!groups.ContainsKey(summary)) groups[summary] = new List<string>();
                    groups[summary].Add(p.LabelShort);
                }
            }

            // Render up to 3 lines ordered by number of affected pawns
            foreach (var kv in groups.OrderByDescending(kv => kv.Value.Count).Take(3))
            {
                var toolNames = kv.Key; // already contains percent info
                var pawnList = kv.Value.Distinct().Take(3);
                sb.AppendLine($"({kv.Value.Count}) {toolNames} - {string.Join(", ", pawnList)}");
            }

            int remaining = groups.Values.Sum(list => list.Count) - groups.Values.Take(3).Sum(list => list.Count);
            if (remaining > 0)
                sb.AppendLine("...and " + remaining + " more");

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
