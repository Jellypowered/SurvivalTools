// RimWorld 1.6 / C# 7.3
// Source/Compatibility/TDEnhancementPack/TDEnhancementPackHelpers.cs
// Phase 10: Bulk WG mapping + right-click eligibility for construction / repair / deconstruct family (incl. TD Enhancement Pack variants).

using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SurvivalTools.Compatibility.TDEnhancementPack
{
    internal static class TDEnhancementPackHelpers
    {
        private static readonly string PkgIdGuess = "Uuugggg.TDEnhancementPack"; // safe guess; detection is OR'd

        internal static bool Active =>
            ModsConfig.ActiveModsInLoadOrder.Any(m =>
                (m.PackageId != null && m.PackageId.Equals(PkgIdGuess, StringComparison.OrdinalIgnoreCase)) ||
                (m.Name != null && m.Name.IndexOf("TD Enhancement Pack", StringComparison.OrdinalIgnoreCase) >= 0));

        internal static void Initialize()
        {
            try
            {
                if (!Active) return; // no-op when mod absent

                // Known vanilla base classes (null-safe for reflection calls below)
                var wgConstruct = AccessTools.TypeByName("RimWorld.WorkGiver_ConstructAffectFloor") ?? AccessTools.TypeByName("RimWorld.WorkGiver_ConstructDeliverResources");
                var wgRepair = typeof(WorkGiver_Repair);
                var wgDeconstruct = typeof(WorkGiver_Deconstruct);
                var wgUninstall = typeof(WorkGiver_Uninstall);
                var wgSmoothWall = AccessTools.TypeByName("RimWorld.WorkGiver_ConstructSmoothWall");
                var wgSmoothFloor = AccessTools.TypeByName("RimWorld.WorkGiver_ConstructSmoothFloor");

                // defName alias groups (strings compared case-insensitively inside helper)
                var constructAliases = new[] { "Construct", "FinishFrame", "BuildRoofs", "RemoveRoofs" };
                var repairAliases = new[] { "Repair", "RepairCarry" };
                var deconstructAliases = new[] { "Deconstruct", "Uninstall" };
                var smoothAliases = new[] { "SmoothWall", "SmoothFloor" };

                // Map ConstructionSpeed (construction + smoothing)
                Compat.CompatAPI.MapWGsToStat_ByDerivationOrAlias(
                    StatDefOf.ConstructionSpeed,
                    new[] { wgConstruct, wgSmoothWall, wgSmoothFloor },
                    constructAliases.Concat(smoothAliases)
                );

                // Map MaintenanceSpeed (repairs)
                Compat.CompatAPI.MapWGsToStat_ByDerivationOrAlias(
                    ST_StatDefOf.MaintenanceSpeed,
                    new[] { wgRepair },
                    repairAliases
                );

                // Map DeconstructionSpeed (deconstruct/uninstall)
                Compat.CompatAPI.MapWGsToStat_ByDerivationOrAlias(
                    ST_StatDefOf.DeconstructionSpeed,
                    new[] { wgDeconstruct, wgUninstall },
                    deconstructAliases
                );

                // Right-click rescue eligibility (register all relevant worker subclasses)
                Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(wgConstruct);
                Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(wgRepair);
                Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(wgDeconstruct);
                Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(wgUninstall);
                Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(wgSmoothWall);
                Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(wgSmoothFloor);
            }
            catch (Exception ex)
            {
                Log.Warning("[SurvivalTools][TDEnhancementPack] Initialize error: " + ex.Message);
            }
        }
    }
}
