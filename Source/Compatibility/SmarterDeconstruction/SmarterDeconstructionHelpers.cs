// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SmarterDeconstruction/SmarterDeconstructionHelpers.cs
using Verse;

// Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs

namespace SurvivalTools.Compat.SmarterDeconstruction
{
    internal static class SmarterDeconstructionHelpers
    {
        public static bool IsSmarterDeconstructionActive()
        {
            try { return ModLister.GetActiveModWithIdentifier("smarterdeconstruction.author") != null; }
            catch { return false; }
        }

        public static bool IsTDEnhancementPackActive()
        {
            try { return ModLister.GetActiveModWithIdentifier("td.enhancement.pack") != null; }
            catch { return false; }
        }

        /// <summary>
        /// Determines if SmarterDeconstruction should be considered the authoritative owner for WorkGiver_Scanner compatibility hooks.
        /// This helps deduplicate patches when multiple mods (e.g., TD Enhancement Pack) attempt to patch the same targets.
        /// </summary>
        public static bool ShouldOwnWorkGiverScannerHooks()
        {
            // Default to true when SmarterDeconstruction is active. Other compat modules can check CompatibilityRegistry to decide.
            try { return IsSmarterDeconstructionActive(); }
            catch { return false; }
        }
    }
}
