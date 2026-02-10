// RimWorld 1.6 / C# 7.3
// Source/Assign/PostAddHooks.cs
// Nightmare (extraHardcoreMode) invariant reinforcement on actual inventory insertions.
// Hooks low-level add/transfer paths so that after a tool physically enters a pawn's
// inventory we immediately enforce the strict carry limit (no credit for queued drops).
//
// Targets:
//  A) Pawn_InventoryTracker.TryAddAndUnforbid(Thing item)
//  B) ThingOwner.TryTransferToContainer(Thing item, ThingOwner otherContainer, int count,
//       out Thing resultingTransferredItem, bool canMergeWithExistingStacks = true)
//
// Both postfixes are allocation-light and guarded so they are effectively no-ops when
// extraHardcoreMode is disabled or the moved item is not a real tool.

using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using SurvivalTools.Helpers;

namespace SurvivalTools.Assign
{
    [StaticConstructorOnStartup]
    internal static class PostAddHooks
    {
        // Lightweight per-pawn cooldown store for optional instrumentation (independent of global ST_Logging cooldowns)
        private static readonly System.Collections.Generic.Dictionary<(string, int), int> _cooldowns = new System.Collections.Generic.Dictionary<(string, int), int>(64);

        static PostAddHooks()
        {
            try
            {
                var H = new HarmonyLib.Harmony("Jelly.SurvivalToolsReborn.PostAddHooks");

                // A) Pawn_InventoryTracker.TryAddAndUnforbid(Thing item)
                var mA = AccessTools.Method(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.TryAddAndUnforbid), new Type[] { typeof(Thing) });
                if (mA != null)
                {
                    H.Patch(mA, postfix: new HarmonyMethod(typeof(PostAddHooks), nameof(Postfix_TryAddAndUnforbid)));
                }
                else
                {
                    Log.Warning("[SurvivalTools.PostAddHooks] Method Pawn_InventoryTracker.TryAddAndUnforbid not found; Nightmare post-add enforcement (path A) skipped.");
                }

                // B) ThingOwner.TryTransferToContainer(Thing item, ThingOwner otherContainer, int count,
                //      out Thing resultingTransferredItem, bool canMergeWithExistingStacks = true)
                var mB = AccessTools.Method(typeof(ThingOwner), "TryTransferToContainer",
                    new Type[] { typeof(Thing), typeof(ThingOwner), typeof(int), typeof(Thing).MakeByRefType(), typeof(bool) });
                if (mB != null)
                {
                    H.Patch(mB, postfix: new HarmonyMethod(typeof(PostAddHooks), nameof(Postfix_TryTransferToContainer)));
                }
                else
                {
                    Log.Warning("[SurvivalTools.PostAddHooks] Method ThingOwner.TryTransferToContainer not found; Nightmare post-add enforcement (path B) skipped.");
                }

#if DEBUG
                Log.Message("[SurvivalTools.PostAddHooks] Initialized (TryAddAndUnforbid + TryTransferToContainer postfixes).");
#endif
            }
            catch (Exception ex)
            {
                Log.Error("[SurvivalTools.PostAddHooks] Exception during static ctor: " + ex);
            }
        }

        // --------------------- POSTFIX A ---------------------
        // public void Pawn_InventoryTracker.TryAddAndUnforbid(Thing item)
        public static void Postfix_TryAddAndUnforbid(Pawn_InventoryTracker __instance, Thing item)
        {
            try
            {
                var pawn = __instance?.pawn; if (pawn == null) return;
                var settings = SurvivalToolsMod.Settings; if (settings == null || !settings.extraHardcoreMode) return;
                if (item == null) return;
                if (!NightmareCarryEnforcer.IsCarryLimitedTool(item)) return; // unified predicate
                if (Helpers.ST_BoundConsumables.IsBoundUnit(item)) return; // rag exclusion (defensive; IsRealTool already filters)

                int allowed = AssignmentSearch.GetEffectiveCarryLimit(pawn, settings);
                NightmareCarryEnforcer.EnforceNow(pawn, keeperOrNull: null, allowed: allowed, reason: "post-add.TryAddAndUnforbid");
                TryLogWithLocalCooldown(("nm-carry-postadd", pawn.thingIDNumber), 60,
                    $"[NightmareCarry] post-add enforce | pawn={pawn.LabelShortCap} keep=auto carried={CountRealTools(pawn)} allowed={allowed}");
            }
            catch (Exception ex)
            {
                Log.Error("[SurvivalTools.PostAddHooks] Exception in Postfix_TryAddAndUnforbid: " + ex);
            }
        }

        // --------------------- POSTFIX B ---------------------
        // public int ThingOwner.TryTransferToContainer(Thing item, ThingOwner otherContainer, int count,
        //      out Thing resultingTransferredItem, bool canMergeWithExistingStacks = true)
        public static void Postfix_TryTransferToContainer(
            ThingOwner __instance,
            Thing item,
            ThingOwner otherContainer,
            int count,
            ref Thing resultingTransferredItem,
            ref int __result)
        {
            try
            {
                if (__result <= 0) return; // nothing moved
                var inv = otherContainer?.Owner as Pawn_InventoryTracker; if (inv == null) return;
                var pawn = inv.pawn; if (pawn == null) return;
                var settings = SurvivalToolsMod.Settings; if (settings == null || !settings.extraHardcoreMode) return;

                var added = resultingTransferredItem ?? item; if (added == null) return;
                if (!NightmareCarryEnforcer.IsCarryLimitedTool(added)) return;
                if (Helpers.ST_BoundConsumables.IsBoundUnit(added)) return;

                int allowed = AssignmentSearch.GetEffectiveCarryLimit(pawn, settings);
                NightmareCarryEnforcer.EnforceNow(pawn, keeperOrNull: null, allowed: allowed, reason: "post-add.TryTransferToContainer");
                TryLogWithLocalCooldown(("nm-carry-posttx", pawn.thingIDNumber), 60,
                    $"[NightmareCarry] post-transfer enforce | pawn={pawn.LabelShortCap} carried={CountRealTools(pawn)} allowed={allowed}");
            }
            catch (Exception ex)
            {
                Log.Error("[SurvivalTools.PostAddHooks] Exception in Postfix_TryTransferToContainer: " + ex);
            }
        }

        private static int CountRealTools(Pawn pawn)
        {
            int c = 0;
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

        private static void TryLogWithLocalCooldown((string, int) key, int ticks, string msg)
        {
            try
            {
                if (!SurvivalTools.ST_Logging.IsDebugLoggingEnabled) return; // only when debug logging on
                int now = Find.TickManager?.TicksGame ?? 0;
                if (_cooldowns.TryGetValue(key, out var until) && now < until) return;
                _cooldowns[key] = now + ticks;
                SurvivalTools.ST_Logging.LogInfo(msg);
            }
            catch { }
        }
    }
}
