using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace SurvivalTools.HarmonyStuff
{
    /// <summary>
    /// Compatibility patch for Nice Inventory Tab mod by Andromeda.
    /// Prevents survival tools from being treated as equipment/weapons, which causes crashes
    /// in Nice Inventory Tab's EquippedItem display logic.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_NiceInventoryTab_BlackListedWeapon
    {
        /// <summary>
        /// Only apply this patch if Nice Inventory Tab is active.
        /// </summary>
        static bool Prepare()
        {
            return ModCompatibilityCheck.NiceInventoryTab;
        }

        /// <summary>
        /// Target the BlackListedWeapon method in Nice Inventory Tab's ITab_Pawn_Gear_Patch class.
        /// This method determines which items should NOT be treated as equipment.
        /// </summary>
        static MethodBase TargetMethod()
        {
            var type = GenTypes.GetTypeInAnyAssembly("NiceInventoryTab.ITab_Pawn_Gear_Patch");
            if (type == null)
            {
                Log.Warning("[SurvivalTools] Could not find NiceInventoryTab.ITab_Pawn_Gear_Patch type for compatibility patch.");
                return null;
            }

            var method = AccessTools.Method(type, "BlackListedWeapon");
            if (method == null)
            {
                Log.Warning("[SurvivalTools] Could not find BlackListedWeapon method in Nice Inventory Tab for compatibility patch.");
                return null;
            }

            return method;
        }

        /// <summary>
        /// Add survival tools to the blacklist so they appear in inventory instead of equipment.
        /// Equipment display uses EquippedItem class which crashes on non-weapon/non-apparel items.
        /// Inventory display uses InventoryItem class which safely handles all item types.
        /// </summary>
        static void Postfix(ThingDef def, ref bool __result)
        {
            if (!__result && def.IsSurvivalTool())
            {
                __result = true;

                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                {
                    Log.Message($"[SurvivalTools] Nice Inventory Tab: Blacklisted {def.defName} (survival tool)");
                }
            }
        }
    }
}
