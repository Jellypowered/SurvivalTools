// Rimworld 1.6 / C# 7.3
// Source/Harmony/Patch_Pawn_InventoryTracker.cs
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.HarmonyStuff
{
    public static class Patch_Pawn_InventoryTracker
    {
        // ------------------------------
        // Prevent unloading an "in-use" tool (real or virtual)
        // ------------------------------
        [HarmonyPatch(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.FirstUnloadableThing), MethodType.Getter)]
        public static class FirstUnloadableThing_Postfix
        {
            public static void Postfix(Pawn_InventoryTracker __instance, ref ThingCount __result)
            {
                var thing = __result.Thing;
                if (thing == null || __instance == null) return;

                // If the first unloadable item is an in-use tool (real or virtual), try to pick another item.
                bool inUse =
                    (thing is SurvivalTool st && st.InUse) ||
                    (thing.def.IsToolStuff() && VirtualInUse(thing));

                if (!inUse) return;

                var inv = __instance.innerContainer;
                if (inv == null || inv.Count == 0)
                {
                    __result = default(ThingCount);
                    return;
                }

                // Find the first alternative item that is *not* an in-use SurvivalTool.
                Thing alt = null;
                for (int i = 0; i < inv.Count; i++)
                {
                    var t = inv[i];
                    if (t == null) continue;

                    // Skip in-use real tools
                    var asTool = t as SurvivalTool;
                    if (asTool != null)
                    {
                        if (!asTool.InUse)
                        {
                            alt = t;
                            break;
                        }
                        continue;
                    }

                    // Plain items (including tool-stuff stacks) are fine to unload
                    alt = t;
                    break;
                }

                __result = (alt != null) ? new ThingCount(alt, alt.stackCount) : default(ThingCount);
            }

            private static bool VirtualInUse(Thing toolStuff)
            {
                // Tool-stuff can be virtually wrapped; if that wrapper is "in use", treat this as in-use.
                var v = VirtualSurvivalTool.FromThing(toolStuff);
                return v != null && v.InUse;
            }
        }

        // ------------------------------
        // Idle auto-trim of excess tools (respecting forced + in-use)
        // ------------------------------
        [HarmonyPatch(typeof(Pawn_InventoryTracker))]
        public static class InventoryTracker_Tick_Patch
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                // Cover both names used across versions.
                var tick = AccessTools.Method(typeof(Pawn_InventoryTracker), "InventoryTrackerTick");
                var tickRare = AccessTools.Method(typeof(Pawn_InventoryTracker), "InventoryTrackerTickRare");

                if (tick != null) yield return tick;
                if (tickRare != null) yield return tickRare;

                if (tick == null && tickRare == null && IsDebugLoggingEnabled)
                    Log.Warning("[SurvivalTools] No inventory tracker tick method found to patch.");
            }

            public static void Postfix(Pawn_InventoryTracker __instance)
            {
                var pawn = __instance?.pawn;
                if (pawn == null || pawn.Map == null) return;

                // Every ~1s real-time @60 TPS; skip while busy.
                if (!pawn.IsHashIntervalTick(60) || pawn.jobs?.curJob != null) return;

                var s = SurvivalTools.Settings;
                if (s == null || s.toolLimit != true) return;
                if (!pawn.CanUseSurvivalTools() || !pawn.CanRemoveExcessSurvivalTools()) return;

                int heldCount = pawn.HeldSurvivalToolCount();
                float cap = pawn.GetStatValue(ST_StatDefOf.SurvivalToolCarryCapacity);
                if (heldCount <= cap) return;

                // Prefer dropping a held (real/virtual) SurvivalTool that is not forced and not in-use.
                Thing toolToDrop = FindDroppableHeldTool(pawn);
                if (toolToDrop == null)
                {
                    // Fallback: a plain tool-stuff stack from inventory if all tools are "in use".
                    var inv = pawn.inventory?.innerContainer;
                    if (inv != null)
                    {
                        for (int i = 0; i < inv.Count; i++)
                        {
                            var t = inv[i];
                            if (t != null && t.def.IsToolStuff())
                            {
                                // Respect forced handler on the physical thing.
                                if (!IsForced(pawn, t))
                                {
                                    toolToDrop = t;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (toolToDrop == null)
                {
                    if (IsDebugLoggingEnabled)
                    {
                        var key = $"InvOverCap_NoDrop_{pawn.ThingID}";
                        if (ShouldLogWithCooldown(key))
                            Log.Message($"[SurvivalTools.InventoryTick] {pawn.LabelShort} over tool limit, but all items are forced or in-use.");
                    }
                    return;
                }

                if (IsDebugLoggingEnabled)
                {
                    var key = $"InvOverCap_Drop_{pawn.ThingID}";
                    if (ShouldLogWithCooldown(key))
                        Log.Message($"[SurvivalTools.InventoryTick] {pawn.LabelShort} idle & over limit. Dropping {toolToDrop.LabelShort}.");
                }

                var dropJob = pawn.DequipAndTryStoreSurvivalTool(toolToDrop, enqueueCurrent: false);
                if (dropJob != null)
                {
                    pawn.jobs.TryTakeOrderedJob(dropJob, JobTag.Misc);
                }
            }

            private static Thing FindDroppableHeldTool(Pawn pawn)
            {
                // Scan the "held tools" projection (real tools + virtual tool-stuff).
                var held = pawn.GetHeldSurvivalTools();
                if (held == null) return null;

                foreach (var t in held)
                {
                    if (t == null) continue;

                    // Respect forced handler on the physical thing.
                    if (IsForced(pawn, PhysicalThingFor(t, pawn))) continue;

                    var asTool = t as SurvivalTool; // includes VirtualSurvivalTool
                    if (asTool != null)
                    {
                        if (!asTool.InUse)
                            return t;
                    }
                    else
                    {
                        // Shouldn't happen (projection returns tools or virtual tools),
                        // but if we ever get a plain thing here, allow it.
                        return t;
                    }
                }
                return null;
            }

            private static bool IsForced(Pawn pawn, Thing physical)
            {
                var tracker = pawn?.GetComp<Pawn_SurvivalToolAssignmentTracker>();
                if (tracker?.forcedHandler == null || physical == null) return false;
                return tracker.forcedHandler.IsForced(physical);
            }

            private static Thing PhysicalThingFor(Thing toolOrVirtual, Pawn pawn)
            {
                var st = toolOrVirtual as SurvivalTool;
                return st != null ? SurvivalToolUtility.BackingThing(st, pawn) ?? toolOrVirtual : toolOrVirtual;
            }
        }

        // ------------------------------
        // Clear forced flag when an item leaves inventory
        // ------------------------------
        [HarmonyPatch(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.Notify_ItemRemoved))]
        public static class Notify_ItemRemoved_Postfix
        {
            public static void Postfix(Pawn_InventoryTracker __instance, Thing item)
            {
                if (item == null) return;
                var pawn = __instance?.pawn;
                if (pawn == null) return;

                // Clear forced status for SurvivalTool or tool-stuff.
                if (item is SurvivalTool || item.def.IsToolStuff())
                {
                    var tracker = pawn.GetComp<Pawn_SurvivalToolAssignmentTracker>();
                    var fh = tracker?.forcedHandler;
                    fh?.SetForced(item, false);
                    try { SurvivalToolUtility.ToolFactorCache.InvalidateForThing(item); } catch { }
                }
            }
        }
    }
}
