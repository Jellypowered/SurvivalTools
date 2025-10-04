// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SmarterConstruction/SmarterConstructionHelpers.cs
// Phase 10 refactor: bulk mapping + exemptions + right-click eligibility via CompatAPI.

using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SurvivalTools.Compatibility.SmarterConstruction
{
    internal static class SmarterConstructionHelpers
    {
        private static readonly string PkgId = "dhultgren.smarterconstruction"; // adjust if exact differs

        internal static bool Active =>
            ModsConfig.ActiveModsInLoadOrder.Any(m => string.Equals(m.PackageId, PkgId, StringComparison.OrdinalIgnoreCase))
            || AccessTools.TypeByName("SmarterConstruction.WorkGiver_ConstructDeliverResources_SC") != null
            || AccessTools.TypeByName("SmarterConstruction.WorkGiver_ConstructFinishFrames_SC") != null;

        internal static void Initialize()
        {
            if (!Active) return;
            try
            {
                var deliveryBase = typeof(WorkGiver_ConstructDeliverResources);
                var finishBase = typeof(WorkGiver_ConstructFinishFrames);
                var repairBase = typeof(WorkGiver_Repair);

                var scDeliverType = AccessTools.TypeByName("SmarterConstruction.WorkGiver_ConstructDeliverResources_SC");
                var scFinishType = AccessTools.TypeByName("SmarterConstruction.WorkGiver_ConstructFinishFrames_SC");
                var scRepairType = AccessTools.TypeByName("SmarterConstruction.WorkGiver_Repair_SC");

                SurvivalTools.Compat.CompatAPI.ExemptPureDelivery_ByDerivationOrAlias(
                    new[] { deliveryBase, scDeliverType },
                    new[] { "ConstructDeliverResourcesToBlueprints", "ConstructDeliverResourcesToFrames" }
                );

                // Construction: use vanilla ConstructionSpeed stat (tool gating will still use mapping)
                SurvivalTools.Compat.CompatAPI.MapWGsToStat_ByDerivationOrAlias(
                    StatDefOf.ConstructionSpeed,
                    new[] { finishBase, scFinishType },
                    new[] { "ConstructFinishFrame" }
                );

                SurvivalTools.Compat.CompatAPI.MapWGsToStat_ByDerivationOrAlias(
                    ST_StatDefOf.MaintenanceSpeed,
                    new[] { repairBase, scRepairType },
                    new[] { "Repair" }
                );

                SurvivalTools.Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(scDeliverType);
                SurvivalTools.Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(scFinishType);
                SurvivalTools.Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(scRepairType);
            }
            catch (Exception ex)
            {
                Log.Warning("[SurvivalTools][SmarterConstruction] Initialize error: " + ex.Message);
            }
        }
    }
}
