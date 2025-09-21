// RimWorld 1.6 / C# 7.3
// Source/Harmony/HarmonyPatches_CacheInvalidation.cs
// From Refactor Phase 4: KEEP
// Phase 4: Cache invalidation hooks for inventory and equipment changes.
// Provides minimal, safe hooks to maintain ScoreCache freshness.

using System;
using HarmonyLib;
using RimWorld;
using Verse;
using SurvivalTools.Helpers;

namespace SurvivalTools.HarmonyStuff
{
    /// <summary>
    /// Phase 4: Minimal cache invalidation patches for ScoreCache freshness.
    /// Uses guarded patches to handle signature changes gracefully.
    /// </summary>
    [HarmonyPatch]
    internal static class HarmonyPatches_CacheInvalidation
    {
        /// <summary>
        /// Invalidate cache when pawn inventory changes (item removed)
        /// Note: Pawn_InventoryTracker only has Notify_ItemRemoved, not Notify_ItemAdded
        /// </summary>
        [HarmonyPatch(typeof(Pawn_InventoryTracker), "Notify_ItemRemoved")]
        [HarmonyPostfix]
        internal static void Postfix_InventoryRemoved(Pawn_InventoryTracker __instance)
        {
            try
            {
                if (__instance?.pawn != null)
                {
                    ScoreCache.NotifyInventoryChanged(__instance.pawn);
                }
            }
            catch
            {
                // Guarded patch: if signature differs, no-op
            }
        }

        /// <summary>
        /// Invalidate cache when pawn equipment changes (item added)
        /// </summary>
        [HarmonyPatch(typeof(Pawn_EquipmentTracker), "Notify_EquipmentAdded")]
        [HarmonyPostfix]
        internal static void Postfix_EquipmentAdded(Pawn_EquipmentTracker __instance)
        {
            try
            {
                if (__instance?.pawn != null)
                {
                    ScoreCache.NotifyInventoryChanged(__instance.pawn);
                }
            }
            catch
            {
                // Guarded patch: if signature differs, no-op
            }
        }

        /// <summary>
        /// Invalidate cache when pawn equipment changes (item removed)
        /// </summary>
        [HarmonyPatch(typeof(Pawn_EquipmentTracker), "Notify_EquipmentRemoved")]
        [HarmonyPostfix]
        internal static void Postfix_EquipmentRemoved(Pawn_EquipmentTracker __instance)
        {
            try
            {
                if (__instance?.pawn != null)
                {
                    ScoreCache.NotifyInventoryChanged(__instance.pawn);
                }
            }
            catch
            {
                // Guarded patch: if signature differs, no-op
            }
        }

        /// <summary>
        /// Primary safety net: catch ThingOwner changes that affect pawn inventories/equipment
        /// This covers both additions and removals for all pawn containers
        /// </summary>
        [HarmonyPatch(typeof(ThingOwner), "NotifyAdded")]
        [HarmonyPostfix]
        internal static void Postfix_ThingOwnerAdded(ThingOwner __instance, Thing item)
        {
            try
            {
                // Check if this ThingOwner belongs to a pawn's inventory or equipment
                if (__instance?.Owner is Pawn_InventoryTracker inventoryTracker && inventoryTracker.pawn != null)
                {
                    ScoreCache.NotifyInventoryChanged(inventoryTracker.pawn);
                }
                else if (__instance?.Owner is Pawn_EquipmentTracker equipmentTracker && equipmentTracker.pawn != null)
                {
                    ScoreCache.NotifyInventoryChanged(equipmentTracker.pawn);
                }
            }
            catch
            {
                // Guarded patch: if signature differs, no-op
            }
        }

        /// <summary>
        /// Primary safety net: catch ThingOwner changes that affect pawn inventories/equipment
        /// This covers both additions and removals for all pawn containers
        /// </summary>
        [HarmonyPatch(typeof(ThingOwner), "NotifyRemoved")]
        [HarmonyPostfix]
        internal static void Postfix_ThingOwnerRemoved(ThingOwner __instance, Thing item)
        {
            try
            {
                // Check if this ThingOwner belongs to a pawn's inventory or equipment
                if (__instance?.Owner is Pawn_InventoryTracker inventoryTracker && inventoryTracker.pawn != null)
                {
                    ScoreCache.NotifyInventoryChanged(inventoryTracker.pawn);
                }
                else if (__instance?.Owner is Pawn_EquipmentTracker equipmentTracker && equipmentTracker.pawn != null)
                {
                    ScoreCache.NotifyInventoryChanged(equipmentTracker.pawn);
                }
            }
            catch
            {
                // Guarded patch: if signature differs, no-op
            }
        }
    }
}