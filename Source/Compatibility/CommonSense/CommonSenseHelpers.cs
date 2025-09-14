// RimWorld 1.6 / C# 7.3
// Source/Compatibility/CommonSense/CommonSenseHelpers.cs
using System;
using Verse;

namespace SurvivalTools.Compat.CommonSense
{
    // Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs
    /// <summary>
    /// Helper utilities for CommonSense compatibility.
    /// All methods are defensive and safe to call when CommonSense is not loaded.
    /// </summary>
    public static class CommonSenseHelpers
    {
        /// <summary>
        /// Returns true if CommonSense mod is active/available.
        /// Use this before calling any CommonSense-specific APIs.
        /// </summary>
        public static bool IsCommonSenseActive()
        {
            try
            {
                // Safe check via ModLister if available at runtime
                return Verse.ModLister.GetActiveModWithIdentifier("com.rimworld.caister.commonsense") != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Centralized check whether a pawn should be exempt from SurvivalTools logic
        /// when CommonSense provides alternate handling (e.g. wanderers, gear UI overrides).
        /// </summary>
        public static bool ShouldBypassForCommonSense(Pawn pawn)
        {
            if (pawn == null) return false;
            try
            {
                // Placeholder for future CommonSense-specific heuristics.
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
