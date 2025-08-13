using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace SurvivalTools
{
    [StaticConstructorOnStartup]
    public static class ModCompatibilityCheck
    {
        // Cache the active list once
        private static readonly List<ModMetaData> Active = ModsConfig.ActiveModsInLoadOrder.ToList();

        private static bool Has(string nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId)) return false;
            return Active.Any(m =>
                string.Equals(m.PackageId, nameOrId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.PackageIdNonUnique, nameOrId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Name, nameOrId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasAny(params string[] namesOrIds) =>
            namesOrIds != null && namesOrIds.Any(Has);

        // Known package IDs (best-effort; keep names as fallbacks)
        public static readonly bool Quarry =
            HasAny("Mrofa.Quarry", "Saebbi.Quarry", "Quarry 1.0", "Quarry");

        public static readonly bool FluffyBreakdowns =
            HasAny("Fluffy.Breakdowns", "Fluffy Breakdowns");

        public static readonly bool TurretExtensions =
            HasAny("XND.TurretExtensions", "XeoNovaDan.TurretExtensions", "[XND] Turret Extensions");

        public static readonly bool PrisonLabor =
            HasAny("Avius.PrisonLabor", "Prison Labor");

        public static readonly bool CombatExtended =
            HasAny("CETeam.CombatExtended", "Combat Extended");

        public static readonly bool PickUpAndHaul =
            HasAny("Mehni.PickUpAndHaul", "PickUpAndHaul", "Pick Up And Haul");

        public static readonly bool MendAndRecycle =
            HasAny("mipen.Mending", "MendAndRecycle", "Mend and Recycle");

        public static readonly bool DubsBadHygiene =
            HasAny("Dubwise.DubsBadHygiene", "Dubs Bad Hygiene");

        // Useful aggregate
        public static readonly bool OtherInventoryModsActive = CombatExtended || PickUpAndHaul;
    }
}
