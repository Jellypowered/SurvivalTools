// RimWorld 1.6 / C# 7.3
// Source/UI/ToolManagement/FloatMenu_DropToolOptions.cs
//
// Adds context menu options for manually dropping survival tools.
// Provides two options:
// - "Drop [tool] for repair" - blocks auto-equip until tool is repaired
// - "Drop [tool] for disassembly/smelting" - blocks auto-equip until tool is destroyed
//
// Tools are tracked by GameComponent_DroppedToolTracker to prevent auto-pickup interference.

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using SurvivalTools.Game;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.UI.ToolManagement
{
    /// <summary>
    /// Adds "Drop [tool]" float menu options for survival tools in pawn inventory/equipment.
    /// This makes it easier for players to manually drop tools for repair, smelting, or storage.
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    internal static class FloatMenu_DropToolOptions
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(List<Pawn> selectedPawns, Vector3 clickPos, ref FloatMenuContext context, ref List<FloatMenuOption> __result)
        {
            System.Diagnostics.Stopwatch _profilerWatch = null;
            try
            {
                var s = SurvivalToolsMod.Settings;
                // Start profiling if enabled
                if (s?.profileFloatMenuPerformance == true && Prefs.DevMode)
                {
                    _profilerWatch = System.Diagnostics.Stopwatch.StartNew();
                }

                // ULTRA-FAST EARLY EXITS (avoid any allocations or expensive checks)
                if (selectedPawns == null || selectedPawns.Count != 1)
                    return;

                var pawn = selectedPawns[0];
                if (pawn == null || !pawn.IsColonist || pawn.Dead || pawn.Downed)
                    return;

                // Quick check: does pawn have ANY tools? Exit before expensive position checks
                if (!HasAnyTools(pawn))
                    return;

                // Check if clicked on the pawn itself (self-click to drop tools)
                IntVec3 cell = IntVec3Utility.ToIntVec3(clickPos);
                if (!cell.InBounds(pawn.Map))
                    return;

                // Only show drop options when clicking on the pawn or adjacent cells
                if (cell.DistanceTo(pawn.Position) > 1)
                    return;

                // Now get the full list (we know there's at least one)
                var carriedTools = GetCarriedTools(pawn);
                if (carriedTools.Count == 0)
                    return;

                // Add separator before tool drop options
                __result.Add(new FloatMenuOption("─ Manage Tools ─", null) { Disabled = true });

                // Add drop options for each tool (repair and disassembly)
                foreach (var tool in carriedTools)
                {
                    AddDropToolOptions(__result, pawn, tool);
                }
            }
            catch (Exception ex)
            {
                LogError($"[SurvivalTools.DropTools] Exception in FloatMenu postfix: {ex}");
            }
            finally
            {
                // Profile timing
                if (_profilerWatch != null)
                {
                    _profilerWatch.Stop();
                    var elapsed = _profilerWatch.Elapsed.TotalMilliseconds;
                    if (elapsed > 0.5) // Only log if > 0.5ms
                    {
                        Log.Warning($"[FloatMenu.Perf] DropToolOptions took {elapsed:F2}ms");
                    }
                }
            }
        }

        /// <summary>
        /// Ultra-fast check: does this pawn have ANY tools? (before we build the full list)
        /// </summary>
        private static bool HasAnyTools(Pawn pawn)
        {
            try
            {
                // Check equipped first (most common case)
                var primary = pawn.equipment?.Primary;
                if (primary != null && (primary is SurvivalTool || primary.def.IsSurvivalTool()))
                    return true;

                // Check inventory
                var inv = pawn.inventory?.innerContainer;
                if (inv != null)
                {
                    for (int i = 0; i < inv.Count; i++)
                    {
                        var thing = inv[i];
                        if (thing is SurvivalTool || thing.def.IsSurvivalTool())
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static List<Thing> GetCarriedTools(Pawn pawn)
        {
            var tools = new List<Thing>();

            try
            {
                // Get tools from inventory
                var inv = pawn.inventory?.innerContainer;
                if (inv != null)
                {
                    foreach (var thing in inv)
                    {
                        if (thing is SurvivalTool || thing.def.IsSurvivalTool())
                            tools.Add(thing);
                    }
                }

                // Get primary equipped tool
                var primary = pawn.equipment?.Primary;
                if (primary != null && (primary is SurvivalTool || primary.def.IsSurvivalTool()))
                {
                    // Only add if not already in list (shouldn't happen but be safe)
                    if (!tools.Any(t => t.thingIDNumber == primary.thingIDNumber))
                        tools.Add(primary);
                }
            }
            catch (Exception ex)
            {
                LogError($"[SurvivalTools.DropTools] Exception getting tools for {pawn.LabelShort}: {ex}");
            }

            return tools;
        }

        /// <summary>
        /// Add both "drop for repair" and "drop for disassembly" options for a tool.
        /// </summary>
        private static void AddDropToolOptions(List<FloatMenuOption> options, Pawn pawn, Thing tool)
        {
            try
            {
                // Build base tool label with quality/HP info
                string baseLabel = GetToolDisplayLabel(tool);

                // Check if tool is forced - can't drop forced tools
                bool isForced = IsToolForced(pawn, tool);
                if (isForced)
                {
                    var opt = new FloatMenuOption($"Drop {baseLabel} (forced - unforce first)", null);
                    opt.Disabled = true;
                    opt.tooltip = new TipSignal("This tool is forced and cannot be dropped. Unforce it first in the tool assignment.");
                    options.Add(opt);
                    return;
                }

                // Check if pawn can drop the tool
                if (!CanDropTool(pawn, tool, out string failReason))
                {
                    var opt = new FloatMenuOption($"Drop {baseLabel} ({failReason})", null);
                    opt.Disabled = true;
                    options.Add(opt);
                    return;
                }

                // Option 1: Drop for repair (only if tool is damaged)
                if (tool.HitPoints < tool.MaxHitPoints)
                {
                    AddDropForRepairOption(options, pawn, tool, baseLabel);
                }

                // Option 2: Drop for disassembly/smelting (always available)
                AddDropForDisassemblyOption(options, pawn, tool, baseLabel);
            }
            catch (Exception ex)
            {
                LogError($"[SurvivalTools.DropTools] Exception adding drop options: {ex}");
            }
        }

        private static void AddDropForRepairOption(List<FloatMenuOption> options, Pawn pawn, Thing tool, string baseLabel)
        {
            Action action = () =>
            {
                try
                {
                    // Create drop job
                    Job dropJob = CreateDropJob(tool);
                    pawn.jobs?.TryTakeOrderedJob(dropJob, JobTag.Misc, requestQueueing: false);

                    // Track the tool to prevent auto-equip until repaired
                    GameComponent_DroppedToolTracker.MarkDroppedForRepair(tool);

                    if (IsDebugLoggingEnabled)
                        LogDebug($"[DropTools] {pawn.LabelShort} dropped {tool.LabelShort} for repair", $"DropToolRepair_{pawn.ThingID}");
                }
                catch (Exception ex)
                {
                    LogError($"[SurvivalTools.DropTools] Exception dropping for repair: {ex}");
                    Messages.Message($"Failed to drop {tool.LabelShort}: {ex.Message}", MessageTypeDefOf.RejectInput);
                }
            };

            var option = new FloatMenuOption($"Drop {baseLabel} for repair", action);
            option.tooltip = new TipSignal($"Drop {tool.LabelCap} and prevent auto-equip until repaired.\n\nThe tool will be blocked from automatic pickup until it reaches full HP.");
            options.Add(option);
        }

        private static void AddDropForDisassemblyOption(List<FloatMenuOption> options, Pawn pawn, Thing tool, string baseLabel)
        {
            Action action = () =>
            {
                try
                {
                    // Create drop job
                    Job dropJob = CreateDropJob(tool);
                    pawn.jobs?.TryTakeOrderedJob(dropJob, JobTag.Misc, requestQueueing: false);

                    // Track the tool to prevent auto-equip until destroyed
                    GameComponent_DroppedToolTracker.MarkDroppedForDisassembly(tool);

                    if (IsDebugLoggingEnabled)
                        LogDebug($"[DropTools] {pawn.LabelShort} dropped {tool.LabelShort} for disassembly", $"DropToolDisasm_{pawn.ThingID}");
                }
                catch (Exception ex)
                {
                    LogError($"[SurvivalTools.DropTools] Exception dropping for disassembly: {ex}");
                    Messages.Message($"Failed to drop {tool.LabelShort}: {ex.Message}", MessageTypeDefOf.RejectInput);
                }
            };

            var option = new FloatMenuOption($"Drop {baseLabel} for disassembly", action);
            option.tooltip = new TipSignal($"Drop {tool.LabelCap} for smelting or disassembly.\n\nThe tool will be blocked from automatic pickup until destroyed.");
            options.Add(option);
        }

        /// <summary>
        /// Build display label for a tool with quality and HP info.
        /// </summary>
        private static string GetToolDisplayLabel(Thing tool)
        {
            string label = tool.LabelShort;

            // Add quality if available
            QualityCategory qc;
            if (tool.TryGetQuality(out qc))
            {
                label += $" ({qc})";
            }

            // Add HP info if damaged
            if (tool.HitPoints < tool.MaxHitPoints)
            {
                float hpPct = (float)tool.HitPoints / tool.MaxHitPoints;
                label += $" ({hpPct:P0} HP)";
            }

            return label;
        }

        /// <summary>
        /// Create appropriate drop job for a tool.
        /// </summary>
        private static Job CreateDropJob(Thing tool)
        {
            // Check if it's a survival tool or has Primary equipment type
            if (tool is SurvivalTool || tool.def.equipmentType != EquipmentType.Primary)
            {
                return JobMaker.MakeJob(ST_JobDefOf.DropSurvivalTool, tool);
            }
            else
            {
                return JobMaker.MakeJob(JobDefOf.DropEquipment, tool);
            }
        }

        private static bool IsToolForced(Pawn pawn, Thing tool)
        {
            try
            {
                var tracker = pawn.GetComp<Pawn_ForcedToolTracker>();
                if (tracker != null && tracker.forcedHandler != null)
                {
                    return tracker.forcedHandler.IsForced(tool);
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Check if a pawn can drop a tool.
        /// Note: This only blocks dropping if the tool is ACTIVELY being used in the current job.
        /// Tools that are just equipped but not currently in use CAN be dropped.
        /// </summary>
        private static bool CanDropTool(Pawn pawn, Thing tool, out string failReason)
        {
            failReason = null;

            try
            {
                // Check if pawn is capable of manipulation
                if (pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) == false)
                {
                    failReason = "cannot manipulate";
                    return false;
                }

                // IMPORTANT: Only block if the tool is actively being used in the CURRENT JOB.
                // This allows dropping tools between jobs or while idle.
                // Tools are only "in use" during the toils of a job that requires them.
                var curJob = pawn.CurJob;
                if (curJob != null && !curJob.playerForced) // Allow dropping during forced jobs
                {
                    var jobTool = curJob.targetA.Thing;
                    if (jobTool != null && (jobTool == tool || jobTool.thingIDNumber == tool.thingIDNumber))
                    {
                        failReason = "actively in use";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogWarning($"[DropTools] Exception checking drop capability: {ex}");
                failReason = "error";
                return false;
            }
        }
    }
}
