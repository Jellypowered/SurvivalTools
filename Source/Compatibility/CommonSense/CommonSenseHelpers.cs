// RimWorld 1.6 / C# 7.3
// Source/Compatibility/CommonSense/CommonSenseHelpers.cs
// Phase 10: Bulk WG mapping + right-click eligibility for cleaning (vanilla + CommonSense derivatives).

using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SurvivalTools.Compatibility.CommonSense
{
    internal static class CommonSenseHelpers
    {
        private static readonly string PkgIdGuess = "mehni.rimworld.commonSense"; // benign if inaccurate

        internal static bool Active =>
            ModsConfig.ActiveModsInLoadOrder.Any(m =>
                (m.PackageId != null && m.PackageId.Equals(PkgIdGuess, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(m.Name) && m.Name.IndexOf("Common Sense", StringComparison.OrdinalIgnoreCase) >= 0))
            || AccessTools.TypeByName("CommonSense.WorkGiver_CleanFilth_CS") != null
            || AccessTools.TypeByName("CommonSense.WorkGiver_CleanArea_CS") != null;

        internal static void Initialize()
        {
            try
            {
                var vanillaCleanWG = typeof(WorkGiver_CleanFilth);
                var csCleanWG = AccessTools.TypeByName("CommonSense.WorkGiver_CleanFilth_CS");
                var csCleanArea = AccessTools.TypeByName("CommonSense.WorkGiver_CleanArea_CS");

                // 1) Map cleaning WGs -> CleaningSpeed
                SurvivalTools.Compat.CompatAPI.MapWGsToStat_ByDerivationOrAlias(
                    ST_StatDefOf.CleaningSpeed,
                    new[] { vanillaCleanWG, csCleanWG, csCleanArea },
                    new[] { "CleanFilth", "CleanArea" }
                );

                // 2) Right-click eligibility (include vanilla always)
                SurvivalTools.Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(vanillaCleanWG);
                SurvivalTools.Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(csCleanWG);
                SurvivalTools.Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(csCleanArea);
            }
            catch (Exception ex)
            {
                Log.Warning("[SurvivalTools][CommonSense] Initialize error: " + ex.Message);
            }
        }
    }
}
