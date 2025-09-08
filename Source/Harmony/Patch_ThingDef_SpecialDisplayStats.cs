// Rimworld 1.6 / C# 7.3
// Patch_ThingDef_SpecialDisplayStats.cs
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

            // Case 1: Survival tool defs
            SurvivalToolProperties tProps;
            if (__instance.IsSurvivalTool(out tProps))
            {
                var mods = tProps?.baseWorkStatFactors;
                if (mods != null && mods.Count > 0)
                    AddStatDrawEntries(list, existing, mods, ST_StatCategoryDefOf.SurvivalTool);
            }

            // Case 1.5: Actual SurvivalTool instances (override with calculated WorkStatFactors)
            if (req.Thing is SurvivalTool actualTool && actualTool.WorkStatFactors != null)
            {
                // Clear existing entries first to avoid duplicates
                list.RemoveAll(entry => entry.category == ST_StatCategoryDefOf.SurvivalTool);
                existing.RemoveWhere(key => key.Contains("Tool —"));

                AddStatDrawEntries(list, existing, actualTool.WorkStatFactors, ST_StatCategoryDefOf.SurvivalTool);
            }

            // Case 2: Stuff materials that carry survival-tool stat factors (tool-stuff)
            if (__instance.IsStuff)
            {
                // Check for SurvivalToolProperties first (for materials like cloth that can be tools)
                var ext = __instance.GetModExtension<SurvivalToolProperties>();
                var mods = ext?.baseWorkStatFactors;
                if (mods != null && mods.Count > 0)
                {
                    AddStatDrawEntries(list, existing, mods, ST_StatCategoryDefOf.SurvivalTool);
                }
                else
                {
                    // Check for StuffPropsTool (for materials like obsidian, etc.)
                    var stuffExt = __instance.GetModExtension<StuffPropsTool>();
                    var stuffMods = stuffExt?.toolStatFactors;
                    if (stuffMods != null && stuffMods.Count > 0)
                        AddStatDrawEntries(list, existing, stuffMods, ST_StatCategoryDefOf.SurvivalTool);
                }
            }

            __result = list;
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
