// RimWorld 1.6 / C# 7.3
// Source/Compatibility/TDEnhancementPack/TDEnhancementPackHelpers.cs
using System;
using Verse;

// Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs

namespace SurvivalTools.Compat.TDEnhancementPack
{
    /// <summary>
    /// Helper utilities for TD Enhancement Pack compatibility.
    /// Uses safe mod detection via ModLister and routes logs through ST_Logging.
    /// </summary>
    internal static class TDEnhancementPackHelpers
    {
        /// <summary>
        /// Returns true when TD Enhancement Pack is active. Safe no-op if ModLister API is unavailable.
        /// </summary>
        public static bool IsTDEnhancementPackActive()
        {
            try { return Verse.ModLister.GetActiveModWithIdentifier("td.enhancement.pack") != null; }
            catch (Exception)
            {
                // Fallback: be conservative and treat mod as not present.
                return false;
            }
        }

        /// <summary>
        /// Wrapper to log compatibility messages consistently.
        /// </summary>
        public static void LogCompat(string message)
        {
            try { ST_Logging.LogCompatMessage(message); }
            catch { /* swallow logging failures */ }
        }
    }
}
