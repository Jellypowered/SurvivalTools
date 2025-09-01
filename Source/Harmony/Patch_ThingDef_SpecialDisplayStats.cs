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
            var list = (__result as List<StatDrawEntry>) ?? __result.ToList();

            // Tool def (not a concrete thing)
            if (req.Thing == null && __instance.IsSurvivalTool(out var tProps) && tProps?.baseWorkStatFactors != null)
            {
                AddStatDrawEntries(list, tProps.baseWorkStatFactors, ST_StatCategoryDefOf.SurvivalTool);
            }

            // Stuff defs that affect tools
            var sPropsTool = __instance.IsStuff ? __instance.GetModExtension<StuffPropsTool>() : null;
            if (sPropsTool?.toolStatFactors != null)
            {
                AddStatDrawEntries(list, sPropsTool.toolStatFactors, ST_StatCategoryDefOf.SurvivalToolMaterial);
            }

            __result = list;
        }

        private static void AddStatDrawEntries(List<StatDrawEntry> list, IEnumerable<StatModifier> modifiers, StatCategoryDef category)
        {
            foreach (var modifier in modifiers)
            {
                if (modifier?.stat == null) continue;

                list.Add(new StatDrawEntry(
                    category,
                    modifier.stat.LabelCap,
                    modifier.value.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Factor),
                    modifier.stat.description,
                    displayPriorityWithinCategory: 0
                ));
            }
        }
    }
}
