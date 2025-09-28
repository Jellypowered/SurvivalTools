// RimWorld 1.6 / C# 7.3
// Source/Assign/PostEquipHooks.cs
// Nightmare (extraHardcoreMode) carry invariant reinforcement at equipment insertion points.
// Complements: PreWork_AutoEquip (pre-start fence), PostAddHooks (inventory adds), JobGate, and TryTakeOrderedJob prefix.
// This closes the gap where an Equip job lands (AddEquipment / Notify_EquipmentAdded) before work starts.

using System;
using HarmonyLib;
using RimWorld;
using Verse;
using SurvivalTools.Helpers; // ST_BoundConsumables

namespace SurvivalTools.Assign
{
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.AddEquipment))]
    internal static class PostEquipHooks_AddEquipment
    {
        private static readonly System.Collections.Generic.Dictionary<(string, int), int> _cooldowns = new System.Collections.Generic.Dictionary<(string, int), int>(32);

        [HarmonyPostfix]
        private static void Postfix(Pawn_EquipmentTracker __instance, ThingWithComps newEq)
        {
            try
            {
                var pawn = __instance?.pawn; if (pawn == null || newEq == null) return;
                var settings = SurvivalToolsMod.Settings; if (settings == null || !settings.extraHardcoreMode) return;
                if (!NightmareCarryEnforcer.IsCarryLimitedTool(newEq)) return; // unified predicate
                if (ST_BoundConsumables.IsBoundUnit(newEq)) return;

                int allowed = AssignmentSearch.GetEffectiveCarryLimit(pawn, settings);
                // Favor keeping the newly equipped tool (keeper=newEq)
                NightmareCarryEnforcer.EnforceNow(pawn, keeperOrNull: newEq, allowed: allowed, reason: "post-equip.AddEquipment");

                TryLogCooldown(("nm-carry-postequip", pawn.thingIDNumber), 60,
                    $"[NightmareCarry] post-equip enforce | pawn={pawn.LabelShortCap} carried={CountRealTools(pawn)} allowed={allowed}");
            }
            catch (Exception ex)
            {
                Log.Error("[SurvivalTools.PostEquipHooks] Exception in AddEquipment postfix: " + ex);
            }
        }

        // Secondary hook some mods trigger (idempotent safety net)
        [HarmonyPatch(typeof(Pawn_EquipmentTracker), "Notify_EquipmentAdded")]
        internal static class PostEquipHooks_NotifyAdded
        {
            private static readonly System.Collections.Generic.Dictionary<(string, int), int> _cooldowns2 = new System.Collections.Generic.Dictionary<(string, int), int>(32);

            [HarmonyPostfix]
            private static void Postfix(Pawn_EquipmentTracker __instance, ThingWithComps eq)
            {
                try
                {
                    var pawn = __instance?.pawn; if (pawn == null || eq == null) return;
                    var settings = SurvivalToolsMod.Settings; if (settings == null || !settings.extraHardcoreMode) return;
                    if (!NightmareCarryEnforcer.IsCarryLimitedTool(eq)) return;
                    if (ST_BoundConsumables.IsBoundUnit(eq)) return;

                    int allowed = AssignmentSearch.GetEffectiveCarryLimit(pawn, settings);
                    NightmareCarryEnforcer.EnforceNow(pawn, keeperOrNull: eq, allowed: allowed, reason: "post-equip.NotifyAdded");

                    TryLogCooldown2(("nm-carry-postequip2", pawn.thingIDNumber), 60,
                        $"[NightmareCarry] post-equip notify | pawn={pawn.LabelShortCap} carried={CountRealTools(pawn)} allowed={allowed}");
                }
                catch (Exception ex)
                {
                    Log.Error("[SurvivalTools.PostEquipHooks] Exception in Notify_EquipmentAdded postfix: " + ex);
                }
            }

            private static void TryLogCooldown2((string, int) key, int ticks, string msg)
            {
                try
                {
                    if (!SurvivalTools.ST_Logging.IsDebugLoggingEnabled) return;
                    int now = Find.TickManager?.TicksGame ?? 0;
                    if (_cooldowns2.TryGetValue(key, out var until) && now < until) return;
                    _cooldowns2[key] = now + ticks;
                    SurvivalTools.ST_Logging.LogInfo(msg);
                }
                catch { }
            }
        }

        private static void TryLogCooldown((string, int) key, int ticks, string msg)
        {
            try
            {
                if (!SurvivalTools.ST_Logging.IsDebugLoggingEnabled) return;
                int now = Find.TickManager?.TicksGame ?? 0;
                if (_cooldowns.TryGetValue(key, out var until) && now < until) return;
                _cooldowns[key] = now + ticks;
                SurvivalTools.ST_Logging.LogInfo(msg);
            }
            catch { }
        }

        private static int CountRealTools(Pawn pawn)
        {
            int c = 0; if (pawn == null) return 0;
            try
            {
                var inv = pawn.inventory?.innerContainer; if (inv != null)
                    for (int i = 0; i < inv.Count; i++) { var t = inv[i]; if (NightmareCarryEnforcer.IsCarryLimitedTool(t)) c++; }
                var eq = pawn.equipment?.AllEquipmentListForReading; if (eq != null)
                    for (int i = 0; i < eq.Count; i++) { var t = eq[i]; if (NightmareCarryEnforcer.IsCarryLimitedTool(t)) c++; }
            }
            catch { }
            return c;
        }
    }
}
