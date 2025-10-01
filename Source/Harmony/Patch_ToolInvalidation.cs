// RimWorld 1.6 / C# 7.3
// Source/Harmony/Patch_ToolInvalidation.cs
// Phase 11.4 LEGACY: Old cache invalidation hooks for ToolFactorCache.
// 
// MODERN SYSTEM (Phase 4):
//   - HarmonyPatches_CacheInvalidation.cs hooks inventory/equipment changes → ScoreCache
//   - ToolStatResolver.Version auto-invalidates all cached scores on quirk/settings changes
//   - HP damage handled by dynamic score calculation (includes condition factor)
//
// These legacy hooks are redundant because:
//   1. Equipment changes: Handled by HarmonyPatches_CacheInvalidation
//   2. HP damage: Score calculation includes current HP dynamically
//   3. Quality/settings: Resolver version bump invalidates all scores
//   4. Destroy/MakeThing: Not needed (resolver version handles def changes)
//
// Phase 11.4 guards added - set STRIP_11_4_OLD_INVALIDATION=true to disable.
using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.HarmonyStuff
{
    public static class Patch_ToolInvalidation
    {
        internal static void Init(Harmony harmony)
        {
            try
            {
                // Patch Thing.TakeDamage(DamageInfo)
                var takeDamage = AccessTools.Method(typeof(Thing), "TakeDamage", new Type[] { typeof(DamageInfo) });
                if (takeDamage != null)
                    harmony.Patch(takeDamage, postfix: new HarmonyMethod(typeof(Patch_ToolInvalidation).GetMethod(nameof(Postfix_Thing_TakeDamage), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)));

                // Patch Thing.Destroy(DestroyMode)
                var destroy = AccessTools.Method(typeof(Thing), "Destroy", new Type[] { typeof(DestroyMode) });
                if (destroy != null)
                    harmony.Patch(destroy, postfix: new HarmonyMethod(typeof(Patch_ToolInvalidation).GetMethod(nameof(Postfix_Thing_Destroy), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)));

                // Patch ThingMaker.MakeThing(ThingDef, ThingDef)
                var makeThing = AccessTools.Method(typeof(ThingMaker), "MakeThing", new Type[] { typeof(ThingDef), typeof(ThingDef) });
                if (makeThing != null)
                    harmony.Patch(makeThing, postfix: new HarmonyMethod(typeof(Patch_ToolInvalidation).GetMethod(nameof(Postfix_ThingMaker_MakeThing), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)));

                // Patch Pawn_EquipmentTracker add/remove methods if present
                var eqType = AccessTools.TypeByName("Pawn_EquipmentTracker");
                if (eqType != null)
                {
                    var add = AccessTools.Method(eqType, "AddEquipment");
                    if (add != null)
                        harmony.Patch(add, postfix: new HarmonyMethod(typeof(Patch_ToolInvalidation).GetMethod(nameof(Postfix_Equipment_Changed), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)));

                    var remove = AccessTools.Method(eqType, "RemoveEquipment");
                    if (remove != null)
                        harmony.Patch(remove, postfix: new HarmonyMethod(typeof(Patch_ToolInvalidation).GetMethod(nameof(Postfix_Equipment_Changed), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)));
                }
            }
            catch { }
        }

        // Postfix for Thing.TakeDamage
        private static void Postfix_Thing_TakeDamage(Thing __instance, DamageInfo dinfo)
        {
            // Phase 11.9: Dead code removed. Modern system: HarmonyPatches_CacheInvalidation + resolver version.
            // HP damage handled by dynamic score calculation (includes condition factor).
            // No-op shim kept for Harmony patch stability.
        }

        // Postfix for Thing.Destroy
        private static void Postfix_Thing_Destroy(Thing __instance, DestroyMode mode)
        {
            // Phase 11.9: Dead code removed. Modern system: HarmonyPatches_CacheInvalidation + resolver version.
            // No-op shim kept for Harmony patch stability.
        }

        // Postfix for ThingMaker.MakeThing
        private static void Postfix_ThingMaker_MakeThing(Thing __result)
        {
            // Phase 11.9: Dead code removed. Modern system: resolver version handles def changes.
            // No-op shim kept for Harmony patch stability.
        }

        // Generic postfix used for equipment add/remove patches
        private static void Postfix_Equipment_Changed(ThingWithComps equipment)
        {
            // Phase 11.9: Dead code removed. Modern system: HarmonyPatches_CacheInvalidation.
            // No-op shim kept for Harmony patch stability.
        }
    }
}
