//Rimworld 1.6 / C# 7.3
//Patch_ThingDef_SpecialDisplayStats.cs
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
            // LabelCap is a TaggedString; ToString is fine for keys here.
            var existing = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e != null)
                    existing.Add((e.LabelCap?.ToString() ?? "") + "|" + (e.ValueString ?? ""));
            }

            // Case 1: Survival tool defs themselves (def view only; not per-instance)
            SurvivalToolProperties tProps;
            if (req.Thing == null && __instance.IsSurvivalTool(out tProps))
            {
                var mods = tProps?.baseWorkStatFactors;
                if (mods != null && mods.Count > 0)
                    AddStatDrawEntries(list, existing, mods, ST_StatCategoryDefOf.SurvivalTool);
            }

            // Case 2: Stuff materials that carry survival-tool stat factors (tool-stuff)
            if (__instance.IsStuff)
            {
                var ext = __instance.GetModExtension<SurvivalToolProperties>();
                var mods = ext?.baseWorkStatFactors;
                if (mods != null && mods.Count > 0)
                    AddStatDrawEntries(list, existing, mods, ST_StatCategoryDefOf.SurvivalTool);
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

                // Label & value
                var label = $"Tool — {modifier.stat.LabelCap}";
                var value = modifier.value.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Factor);

                // De-dupe: skip if an identical row already exists
                var key = label + "|" + value;
                if (existing.Contains(key))
                    continue;

                // Brief, safe description (no assumptions about localization keys here)
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
