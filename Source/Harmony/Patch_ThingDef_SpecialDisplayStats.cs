// Rimworld 1.6 / C# 7.3
// Source/Harmony/Patch_ThingDef_SpecialDisplayStats.cs
// Shows tool-related stat factors in the info card for SurvivalTool defs and tool-stuff.
// Also shows aggregated base factors for ThingDefs that have them (via SurvivalToolProperties or ToolResolver).
// Legacy code, KEEP.

using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(ThingDef))]
    [HarmonyPatch(nameof(ThingDef.SpecialDisplayStats))]
    public static class Patch_ThingDef_SpecialDisplayStats
    {
        public static void Postfix(ThingDef __instance, ref IEnumerable<StatDrawEntry> __result, StatRequest req)
        {
            if (__instance == null)
                return;

            // always work against a real list we control
            var list = (__result as List<StatDrawEntry>) ?? (__result?.ToList() ?? new List<StatDrawEntry>(4));

            // Build a quick de-dupe set (label + value) to avoid duplicate rows
            var existing = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e != null)
                    existing.Add((e.LabelCap?.ToString() ?? "") + "|" + (e.ValueString ?? ""));
            }

            // Aggregated stat factors for the def (covers SurvivalTool defs, enhanced weapon defs, and tool-stuff defs)
            var aggregatedDefFactors = AggregateDefToolFactors(__instance);
            if (aggregatedDefFactors.Count > 0)
            {
                AddStatDrawEntries(list, existing, aggregatedDefFactors, ST_StatCategoryDefOf.SurvivalTool);
            }

            // Actual SurvivalTool / VirtualTool instance overrides (instance factors already include effectiveness, quality, stuff multipliers)
            if (req.Thing is SurvivalTool actualTool)
            {
                var instFactors = actualTool.WorkStatFactors?.Where(m => m != null && m.stat != null).ToList();
                if (instFactors != null && instFactors.Count > 0)
                {
                    // Remove any previously added entries for the same stats to prevent duplicate display rows showing base+instance
                    var instStatDefs = new HashSet<StatDef>(instFactors.Select(m => m.stat));
                    list.RemoveAll(entry => entry.category == ST_StatCategoryDefOf.SurvivalTool &&
                        entry?.LabelCap != null && instStatDefs.Any(sd => entry.LabelCap.ToString().IndexOf(sd.label, System.StringComparison.OrdinalIgnoreCase) >= 0));
                    existing.RemoveWhere(key => instStatDefs.Any(sd => key.IndexOf(sd.label, System.StringComparison.OrdinalIgnoreCase) >= 0));

                    AddStatDrawEntries(list, existing, instFactors, ST_StatCategoryDefOf.SurvivalTool);
                }
            }

            __result = list;
        }

        /// <summary>
        /// Collects all survival tool related stat factors declared on a ThingDef either through
        /// SurvivalToolProperties.baseWorkStatFactors or statBases (which may have been injected by ToolResolver).
        /// Dedupe by StatDef keeping the maximum value. Returns an empty list if none found.
        /// </summary>
        private static List<StatModifier> AggregateDefToolFactors(ThingDef def)
        {
            var result = new List<StatModifier>();
            if (def == null) return result;

            try
            {
                // Extension factors (baseWorkStatFactors)
                var ext = def.GetModExtension<SurvivalToolProperties>();
                var extMods = ext?.baseWorkStatFactors;
                if (extMods != null)
                {
                    for (int i = 0; i < extMods.Count; i++)
                    {
                        var m = extMods[i];
                        if (m?.stat != null && m.value != 0f)
                            result.Add(new StatModifier { stat = m.stat, value = m.value });
                    }
                }

                // statBases (injected primary/secondary stats from ToolResolver or original defs)
                var bases = def.statBases;
                if (bases != null)
                {
                    for (int i = 0; i < bases.Count; i++)
                    {
                        var m = bases[i];
                        if (m?.stat != null && m.value != 0f)
                            result.Add(new StatModifier { stat = m.stat, value = m.value });
                    }
                }

                // Stuff-based definitions (tool-stuff) don't expose material multipliers here; those apply at instance level.
                // If it's stuff and has StuffPropsTool multipliers only (no ext), we still surface base factors so players see potential.*
                if (def.IsStuff)
                {
                    var stuffExt = def.GetModExtension<StuffPropsTool>();
                    if (stuffExt?.toolStatFactors != null && (extMods == null || extMods.Count == 0))
                    {
                        // Show only positive (>1) multipliers as factors; treat them as base factors to inform players.
                        for (int i = 0; i < stuffExt.toolStatFactors.Count; i++)
                        {
                            var m = stuffExt.toolStatFactors[i];
                            if (m?.stat != null && m.value > 1f)
                                result.Add(new StatModifier { stat = m.stat, value = m.value });
                        }
                    }
                }

                // Dedupe by stat keep max
                if (result.Count > 0)
                {
                    var dedup = new Dictionary<StatDef, float>();
                    for (int i = 0; i < result.Count; i++)
                    {
                        var m = result[i];
                        if (m?.stat == null) continue;
                        if (!dedup.TryGetValue(m.stat, out var cur) || m.value > cur)
                            dedup[m.stat] = m.value;
                    }
                    result = dedup.Select(kv => new StatModifier { stat = kv.Key, value = kv.Value }).ToList();
                }
            }
            catch { /* swallow for safety */ }

            return result;
        }

        private static void AddStatDrawEntries(
            List<StatDrawEntry> list,
            HashSet<string> existing,
            IEnumerable<StatModifier> modifiers,
            StatCategoryDef category)
        {
            if (list == null || existing == null || modifiers == null || category == null)
                return;

            foreach (var modifier in modifiers)
            {
                if (modifier == null || modifier.stat == null)
                    continue;

                // Only show stats that provide meaningful bonuses (> 100%).
                // Penalties (< 100%) are intentionally hidden to reduce clutter in info cards.
                if (modifier.value <= 1.0f)
                    continue;

                var label = $"Tool — {modifier.stat.LabelCap}";
                var value = modifier.value.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Factor);

                var key = label + "|" + value;
                if (existing.Contains(key))
                    continue;

                var report = $"When survival tools (or tool-stuff) apply, {modifier.stat.label.ToLower()} is multiplied by {modifier.value.ToStringPercent()}.";

                list.Add(new StatDrawEntry(
                    category: category,
                    label: label,
                    valueString: value,
                    reportText: report,
                    displayPriorityWithinCategory: 0));

                existing.Add(key);
            }
        }
    }
}
