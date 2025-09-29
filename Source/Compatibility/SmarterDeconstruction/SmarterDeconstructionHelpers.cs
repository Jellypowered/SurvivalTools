// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SmarterDeconstruction/SmarterDeconstructionHelpers.cs
// Phase 10: Bulk mapping + right-click eligibility for deconstruction/uninstall.

using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SurvivalTools.Compatibility.SmarterDeconstruction
{
    internal static class SmarterDeconstructionHelpers
    {
        private static readonly string PkgId = "smarter.deconstruction"; // placeholder ID

        internal static bool Active =>
            ModsConfig.ActiveModsInLoadOrder.Any(m => m.PackageId != null && m.PackageId.Equals(PkgId, StringComparison.OrdinalIgnoreCase))
            || AccessTools.TypeByName("SmarterDeconstruction.WorkGiver_Deconstruct_SD") != null
            || AccessTools.TypeByName("SmarterDeconstruction.WorkGiver_Uninstall_SD") != null;

        internal static void Initialize()
        {
            if (!Active) return;
            try
            {
                var deconstructBase = typeof(WorkGiver_Deconstruct);
                var uninstallBase = AccessTools.TypeByName("RimWorld.WorkGiver_Uninstall");
                var sdDeconstructType = AccessTools.TypeByName("SmarterDeconstruction.WorkGiver_Deconstruct_SD");
                var sdUninstallType = AccessTools.TypeByName("SmarterDeconstruction.WorkGiver_Uninstall_SD");

                SurvivalTools.Compat.CompatAPI.MapWGsToStat_ByDerivationOrAlias(
                    ST_StatDefOf.DeconstructionSpeed,
                    new[] { deconstructBase, uninstallBase, sdDeconstructType, sdUninstallType },
                    new[] { "Deconstruct", "Uninstall" }
                );

                SurvivalTools.Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(sdDeconstructType);
                SurvivalTools.Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(sdUninstallType);
            }
            catch (Exception ex)
            {
                Log.Warning("[SurvivalTools][SmarterDeconstruction] Initialize error: " + ex.Message);
            }
        }
    }
}
