//Rimworld 1.6 / C# 7.3
//Patch_ThingFilter_SetFromPreset.cs
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
            // Defensive guards
            if (__instance == null) return;
            if (preset != StorageSettingsPreset.DefaultStockpile) return;

            var cat = ST_ThingCategoryDefOf.SurvivalTools;
            if (cat == null) return; // defs missing/failed to load

            try
            {
                __instance.SetAllow(cat, true);

                if (SurvivalToolUtility.IsDebugLoggingEnabled &&
                    SurvivalToolUtility.ShouldLogWithCooldown("ST_FilterPreset_DefaultStockpile"))
                {
                    Log.Message("[SurvivalTools] Enabled SurvivalTools category in Default stockpile preset.");
                }
            }
            catch (System.Exception e)
            {
                Log.Error($"[SurvivalTools] Failed to enable SurvivalTools in Default stockpile preset: {e}");
            }
        }
    }
}
