// Rimworld 1.6 / C# 7.3
// Patch_ThingFilter_SetFromPreset.cs
using HarmonyLib;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.HarmonyStuff
{
    /// <summary>
    /// Ensures that SurvivalTools category is enabled by default
    /// in the "Default stockpile" preset.
    /// 
    /// Does NOT affect player-edited stockpiles or other presets.
    /// </summary>
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
            if (cat == null)
            {
                if (IsDebugLoggingEnabled)
                    Log.Warning("[SurvivalTools] SurvivalTools ThingCategoryDef missing — stockpile patch skipped.");
                return;
            }

            try
            {
                // Enable SurvivalTools in Default stockpile preset
                __instance.SetAllow(cat, true);

                if (IsDebugLoggingEnabled &&
                    ShouldLogWithCooldown("ST_FilterPreset_DefaultStockpile"))
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
