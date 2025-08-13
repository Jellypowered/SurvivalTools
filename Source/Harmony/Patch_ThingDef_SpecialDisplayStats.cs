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
            // Start with the original list
            var list = (__result as List<StatDrawEntry>) ?? __result.ToList();

            // Tool def (not a concrete thing)
            SurvivalToolProperties tProps;
            if (req.Thing == null && __instance.IsSurvivalTool(out tProps) && tProps?.baseWorkStatFactors != null)
            {
                foreach (var modifier in tProps.baseWorkStatFactors)
                {
                    if (modifier?.stat == null) continue;

                    list.Add(new StatDrawEntry(
                        ST_StatCategoryDefOf.SurvivalTool,
                        modifier.stat.LabelCap,
                        modifier.value.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Factor),
                        modifier.stat.description,
                        displayPriorityWithinCategory: 0
                    ));
                }
            }

            // Stuff defs that affect tools
            var sPropsTool = __instance.IsStuff ? __instance.GetModExtension<StuffPropsTool>() : null;
            if (sPropsTool?.toolStatFactors != null)
            {
                foreach (var modifier in sPropsTool.toolStatFactors)
                {
                    if (modifier?.stat == null) continue;

                    list.Add(new StatDrawEntry(
                        ST_StatCategoryDefOf.SurvivalToolMaterial,
                        modifier.stat.LabelCap,
                        modifier.value.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Factor),
                        modifier.stat.description,
                        displayPriorityWithinCategory: 0
                    ));
                }
            }

            __result = list;
        }
    }
}
