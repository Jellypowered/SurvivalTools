// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SmarterConstruction/SmarterConstructionHelpers.cs
using System;
using Verse;

// Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs
using HarmonyLib;

namespace SurvivalTools.Compat.SmarterConstruction
{
    /// <summary>
    /// Helpers for SmarterConstruction compatibility.
    /// Defensive, minimal surface area for reflection lookups.
    /// </summary>
    internal static class SmarterConstructionHelpers
    {
        public static bool IsSmarterConstructionActive()
        {
            try
            {
                return ModLister.GetActiveModWithIdentifier("smarterconstruction.author") != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Safe type lookup for SmarterConstruction job driver types.
        /// </summary>
        public static Type GetJobDriverConstructType()
        {
            try { return AccessTools.TypeByName("SmarterConstruction.JobDriver_ConstructFinishFrame"); }
            catch { return null; }
        }
    }
}
