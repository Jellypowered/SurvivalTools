using HarmonyLib;
using RimWorld;
using Verse;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(ThingFilter))]
    [HarmonyPatch(nameof(ThingFilter.SetFromPreset))]
    public static class Patch_ThingFilter_SetFromPreset
    {
        public static void Postfix(ThingFilter __instance, StorageSettingsPreset preset)
        {
            if (preset == StorageSettingsPreset.DefaultStockpile)
            {
                __instance.SetAllow(ST_ThingCategoryDefOf.SurvivalTools, true);
            }
        }
    }
}
