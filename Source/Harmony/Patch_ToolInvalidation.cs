// RimWorld 1.6 / C# 7.3
// Source/Harmony/Patch_ToolInvalidation.cs
// Legacy Invalidation Patches for Tool Stat Cache.
// Ensures that when tools are damaged, destroyed, created, or moved,
// any cached tool stat entries are invalidated so they will be recomputed on next use.
// Is this still needed with the new centralized ToolStatResolver and its hardened caching?
// Possibly yes, as a safety net to ensure no stale entries linger.
using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.HarmonyStuff
{
    [StaticConstructorOnStartup]
    public static class Patch_ToolInvalidation
    {
        static Patch_ToolInvalidation()
        {
            try
            {
                var harmony = new Harmony("com.jellypowered.survivaltools.invalidation");

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
            catch
            {
                // best-effort
            }
        }

        // Postfix for Thing.TakeDamage
        private static void Postfix_Thing_TakeDamage(Thing __instance, DamageInfo dinfo)
        {
            try
            {
                if (__instance == null) return;
                if (SurvivalToolsMod.Settings?.debugLogging == true)
                {
                    try
                    {
                        // Only log if this thing is a SurvivalTool or a tool-stuff (has SurvivalToolProperties)
                        bool isSurvival = __instance is SurvivalTool || __instance.def?.GetModExtension<SurvivalToolProperties>() != null;
                        if (isSurvival)
                        {
                            var dmgDef = dinfo.Def?.defName ?? "(null)";
                            var mapName = __instance.Map != null ? __instance.Map.ToString() : "(none)";
                            LogDecision($"Thing_TakeDamage_{__instance.ThingID}", $"[SurvivalTools.Debug] Thing.TakeDamage: def={__instance.def?.defName ?? "null"} id={__instance.ThingID} hp={__instance.HitPoints}/{__instance.MaxHitPoints} dmg={dinfo.Amount} dmgDef={dmgDef} map={mapName}");
                        }
                    }
                    catch { }
                }

                // If the thing was destroyed or fell to 0 HP, invalidate related cache entries
                if (__instance.Destroyed || (__instance.HitPoints <= 0))
                {
                    SurvivalToolUtility.ToolFactorCache.InvalidateForThing(__instance);
                    try { SurvivalToolUtility.ClearCountersForThing(__instance); } catch { }
                }
            }
            catch { }
        }

        // Postfix for Thing.Destroy
        private static void Postfix_Thing_Destroy(Thing __instance, DestroyMode mode)
        {
            try
            {
                if (__instance == null) return;

                // Skip logging for virtual tools created by UI (they have no map and no parent holder)
                bool isVirtualTool = __instance.Map == null && __instance.ParentHolder == null;

                if (SurvivalToolsMod.Settings?.debugLogging == true && !isVirtualTool)
                {
                    try
                    {
                        bool isSurvival = __instance is SurvivalTool || __instance.def?.GetModExtension<SurvivalToolProperties>() != null;
                        if (isSurvival)
                        {
                            var mapName = __instance.Map != null ? __instance.Map.ToString() : "(none)";
                            string holderInfo = "";
                            try
                            {
                                // If the thing was held by a pawn (equipment or inventory), include holder info
                                var parentHolder = __instance.ParentHolder;
                                if (parentHolder != null)
                                {
                                    var eqHolder = parentHolder as Pawn_EquipmentTracker;
                                    var invHolder = parentHolder as Pawn_InventoryTracker;
                                    Pawn holderPawn = null;
                                    bool isEquipped = false;
                                    if (eqHolder?.pawn != null)
                                    {
                                        holderPawn = eqHolder.pawn;
                                        isEquipped = true;
                                    }
                                    else if (invHolder?.pawn != null)
                                    {
                                        holderPawn = invHolder.pawn;
                                        isEquipped = false;
                                    }

                                    // If a pawn holder exists and the pawn is allowed to use survival tools
                                    // (i.e., not blacklisted), append holder info. Otherwise fallback to standard log.
                                    if (holderPawn != null)
                                    {
                                        try
                                        {
                                            if (PawnToolValidator.CanUseSurvivalTools(holderPawn))
                                            {
                                                holderInfo = isEquipped ? $" heldBy={holderPawn.LabelShort} (equipped)" : $" heldBy={holderPawn.LabelShort} (inventory)";
                                            }
                                        }
                                        catch { /* best-effort: if validator throws, don't include holder info */ }
                                    }
                                }
                            }
                            catch { }

                            LogDecision($"Thing_Destroy_{__instance.ThingID}", $"[SurvivalTools.Debug] Thing.Destroy: def={__instance.def?.defName ?? "null"} id={__instance.ThingID} destroyMode={mode} map={mapName}{holderInfo}");
                        }
                    }
                    catch { }
                }
                SurvivalToolUtility.ToolFactorCache.InvalidateForThing(__instance);
                try { SurvivalToolUtility.ClearCountersForThing(__instance); } catch { }
            }
            catch { }
        }

        // Postfix for ThingMaker.MakeThing
        private static void Postfix_ThingMaker_MakeThing(Thing __result)
        {
            try
            {
                if (__result == null) return;

                // Don't log creation of virtual tools (they'll be destroyed shortly for UI display)
                // Virtual tools have no map and no parent holder initially
                bool isLikelyVirtualTool = __result.Map == null && __result.ParentHolder == null &&
                                          (__result.def?.IsWeapon == true || __result.def?.IsMeleeWeapon == true);

                if (SurvivalToolsMod.Settings?.debugLogging == true && !isLikelyVirtualTool)
                {
                    try
                    {
                        bool isSurvival = __result is SurvivalTool || __result.def?.GetModExtension<SurvivalToolProperties>() != null;
                        if (isSurvival)
                        {
                            LogDecision($"Thing_MakeThing_{__result.ThingID}", $"[SurvivalTools.Debug] ThingMaker.MakeThing: def={__result.def?.defName ?? "null"} id={__result.ThingID} stack={__result.stackCount}");
                        }
                    }
                    catch { }
                }
                // If a new tool or tool-stuff was created by crafting, invalidate its key so
                // any cached entries will be recomputed on first use (safe and conservative).
                if (__result.def != null && (__result.def.IsSurvivalTool() || __result.def.IsToolStuff()))
                {
                    SurvivalToolUtility.ToolFactorCache.InvalidateForThing(__result);
                    try { SurvivalToolUtility.ClearCountersForThing(__result); } catch { }
                }
            }
            catch { }
        }

        // Generic postfix used for equipment add/remove patches
        private static void Postfix_Equipment_Changed(ThingWithComps equipment)
        {
            try
            {
                if (equipment == null) return;
                // Debug log to help verify that the reflection-based equipment patches are attached and firing.
                if (SurvivalToolsMod.Settings?.debugLogging == true)
                {
                    try
                    {
                        // Only log equipment changes for survival tools / tool-stuffs
                        bool isSurvival = equipment is SurvivalTool || equipment.def?.GetModExtension<SurvivalToolProperties>() != null;
                        if (isSurvival)
                        {
                            var holder = equipment.ParentHolder as Pawn_EquipmentTracker;
                            var pawn = holder?.pawn ?? (equipment.ParentHolder as Pawn_InventoryTracker)?.pawn;
                            var pawnLabel = pawn?.LabelShort ?? "(unknown)";
                            LogDecision($"Equipment_Changed_{equipment.GetHashCode()}", $"[SurvivalTools.Debug] Equipment changed: {equipment.def?.defName ?? "null"} on pawn {pawnLabel}");
                        }
                    }
                    catch { }
                }
                SurvivalToolUtility.ToolFactorCache.InvalidateForThing(equipment);
                try { SurvivalToolUtility.ClearCountersForThing(equipment); } catch { }
            }
            catch { }
        }
    }
}
