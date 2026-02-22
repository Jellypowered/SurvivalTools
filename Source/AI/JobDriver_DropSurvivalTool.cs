// Rimworld 1.6 / C# 7.3
// Source/AI/JobDriver_DropSurvivalTool.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using RimWorld;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools
{
    public class JobDriver_DropSurvivalTool : JobDriver
    {
        private const int DurationTicks = 15;

        private Thing ToolToDrop => job?.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            
            // Fix: Add standard fail conditions to prevent pawns getting stuck
            this.FailOn(() => pawn.Downed);
            this.FailOn(() => pawn.Drafted);
            this.FailOn(() => pawn.InMentalState);

            // Cache map reference for performance
            var map = pawn?.Map;

            // Fix: Simplified drop logic - always drop at current position, then haul to storage
            // This prevents pathfinding failures from causing pawns to freeze
            // Original complex logic: try to path to storage/home cell before dropping
            // New simple logic: drop here, THEN enqueue haul job if storage exists

            yield return Toils_General.Wait(DurationTicks, TargetIndex.None);

            yield return Toils_General.DoAtomic(() =>
            {
                if (pawn == null || map == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                var toolAsSurvival = ToolToDrop as SurvivalTool;
                var backing = SurvivalToolUtility.BackingThing(toolAsSurvival, pawn) ?? ToolToDrop;

                // Cache inventory reference for performance
                var inv = pawn.inventory?.innerContainer;
                if (inv == null)
                {
                    LogDebug($"[SurvivalTools.DropTool] {pawn.LabelShort} has no inventory; cannot drop tool.", $"DropTool_NoInventory_{pawn.ThingID}");
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // Handle drop from equipment if applicable
                // Cache equipment list to avoid repeated property access
                var eqList = pawn.equipment?.AllEquipmentListForReading;
                ThingWithComps eqTool = null;
                if (eqList != null)
                {
                    int eqCount = eqList.Count;
                    for (int i = 0; i < eqCount; i++)
                    {
                        var e = eqList[i];
                        if (e == null) continue;
                        if (e == backing || e == ToolToDrop || e.def == ToolToDrop?.def)
                        {
                            eqTool = e; break;
                        }
                    }
                }

                // If the tool is equipped, drop it via equipment tracker at our current cell
                Thing droppedTool = null;
                if (eqTool != null)
                {
                    try
                    {
                        // Use cached map reference (implicit in TryDropEquipment)
                        if (pawn.equipment.TryDropEquipment(eqTool, out ThingWithComps droppedEq, pawn.Position))
                        {
                            droppedTool = droppedEq;
                        }
                    }
                    catch { droppedTool = null; }
                }
                else
                {
                    // Otherwise, drop from inventory
                    // Prefer exact reference match, fallback to def match
                    Thing inventoryInstance = null;
                    if (backing != null)
                        inventoryInstance = inv.FirstOrDefault(t => t == backing);

                    if (inventoryInstance == null && backing != null)
                        inventoryInstance = inv.FirstOrDefault(t => t.def == backing.def && t.stackCount > 0);

                    if (inventoryInstance == null && ToolToDrop != null)
                        inventoryInstance = inv.FirstOrDefault(t => t == ToolToDrop || t.def == ToolToDrop.def);

                    if (inventoryInstance == null)
                    {
                        LogDebug($"[SurvivalTools.DropTool] {pawn.LabelShort} attempted to drop {backing?.LabelShort ?? "tool"} but no matching item found in inventory.", $"DropTool_NotInInventory_{pawn.ThingID}");
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    // If the found stack has more than 1, split off 1. Otherwise operate on the instance directly.
                    Thing toDrop = inventoryInstance.stackCount > 1
                        ? inventoryInstance.SplitOff(1)
                        : inventoryInstance;

                    // Try dropping via the ThingOwner API at our current (preferred) cell
                    bool dropOk = false;
                    try
                    {
                        if (map != null)
                            dropOk = inv.TryDrop(toDrop, pawn.Position, map, ThingPlaceMode.Near, out droppedTool);
                    }
                    catch
                    {
                        dropOk = false;
                        droppedTool = null;
                    }

                    if (!dropOk || droppedTool == null)
                    {
                        // Fallback: attempt to place on the map directly
                        bool placed = false;
                        try
                        {
                            if (map != null)
                                placed = GenPlace.TryPlaceThing(toDrop, pawn.Position, map, ThingPlaceMode.Near);
                        }
                        catch
                        {
                            placed = false;
                        }

                        if (!placed)
                        {
                            if (IsDebugLoggingEnabled)
                            {
                                var k = $"DropTool_PlaceFailed_{pawn.ThingID}";
                                if (ShouldLogWithCooldown(k))
                                    LogWarning($"[SurvivalTools.DropTool] {pawn.LabelShort} failed to drop/place {toDrop.Label}.");
                            }

                            // Attempt to restore split-off items safely
                            if (toDrop != inventoryInstance)
                            {
                                try
                                {
                                    var sameStack = inv.FirstOrDefault(t => t.def == toDrop.def && t.stackCount < t.def.stackLimit);
                                    if (sameStack != null)
                                    {
                                        int freeSpace = sameStack.def.stackLimit - sameStack.stackCount;
                                        int moveAmount = Math.Min(freeSpace, toDrop.stackCount);
                                        if (moveAmount > 0)
                                        {
                                            sameStack.stackCount += moveAmount;
                                            toDrop.stackCount -= moveAmount;
                                        }

                                        if (toDrop.stackCount <= 0)
                                        {
                                            try { toDrop.Destroy(); } catch { }
                                        }
                                        else
                                        {
                                            try { inv.TryAdd(toDrop); }
                                            catch { try { toDrop.Destroy(); } catch { } }
                                        }
                                    }
                                    else
                                    {
                                        try { inv.TryAdd(toDrop); }
                                        catch { try { toDrop.Destroy(); } catch { } }
                                    }
                                }
                                catch
                                {
                                    try { toDrop.Destroy(); } catch { }
                                }
                            }

                            EndJobWith(JobCondition.Incompletable);
                            return;
                        }

                        droppedTool = toDrop;
                    }
                }

                // Ensure the dropped tool is not forbidden so pawns can interact with it immediately
                try { droppedTool.SetForbidden(false, false); } catch { }

                // Clear forced-handler references
                pawn.TryGetComp<Pawn_ForcedToolTracker>()?.forcedHandler?.SetForced(droppedTool, false);

                // After dropping, if we're still over the immediate effective limit (e.g. Nightmare 1) enforce again
                try
                {
                    try
                    {
                        var settings = SurvivalToolsMod.Settings;
                        if (settings != null)
                            SurvivalTools.Assign.NightmareCarryEnforcer.EnforceNow(pawn, null, SurvivalTools.Assign.AssignmentSearch.GetEffectiveCarryLimit(pawn, settings), "post-drop");
                    }
                    catch { }
                }
                catch { }

                // Fix: Simplified haul logic - try to find storage and enqueue haul job
                // This happens AFTER dropping, so we never get stuck pathing to storage
                if (TryFindStorageForTool(droppedTool, out IntVec3 storageCell))
                {
                    var haulJob = JobMaker.MakeJob(JobDefOf.HaulToCell, droppedTool, storageCell);
                    haulJob.count = 1;
                    if (pawn.jobs?.jobQueue != null)
                        pawn.jobs.jobQueue.EnqueueFirst(haulJob);
                }
            });
        }

        private bool TryFindStorageForTool(Thing tool, out IntVec3 storageCell)
        {
            storageCell = IntVec3.Invalid;
            if (pawn?.Map == null || tool == null) return false;

            var map = pawn.Map;
            var faction = pawn.Faction;

            // 1) Try to find proper storage
            if (StoreUtility.TryFindBestBetterStoreCellFor(tool, pawn, map, StoreUtility.CurrentStoragePriorityOf(tool), faction, out storageCell))
                return true;

            if (StoreUtility.TryFindBestBetterStoreCellFor(tool, pawn, map, StoragePriority.Unstored, faction, out storageCell))
                return true;

            // 2) Try to find a stockpile that accepts this tool
            var stockpiles = map.zoneManager.AllZones.OfType<Zone_Stockpile>();
            foreach (var stockpile in stockpiles)
            {
                if (stockpile == null) continue;
                if (!stockpile.settings.AllowedToAccept(tool)) continue;

                var validCell = stockpile.cells.FirstOrDefault(cell =>
                    StoreUtility.IsValidStorageFor(cell, map, tool) &&
                    pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Deadly));

                if (validCell.IsValid)
                {
                    storageCell = validCell;
                    return true;
                }
            }

            // 3) Fallback: if no storage/stockpile, at least haul to nearby home area
            var home = map.areaManager?.Home;
            if (home != null)
            {
                // Search a small radius for a standable, reachable home cell
                int maxRadius = 12;
                for (int r = 0; r <= maxRadius; r++)
                {
                    var ring = GenRadial.RadialCellsAround(pawn.Position, r, true);
                    foreach (var c in ring)
                    {
                        if (!c.InBounds(map)) continue;
                        if (!home[c]) continue;
                        if (!c.Standable(map)) continue;
                        if (!pawn.CanReserveAndReach(c, PathEndMode.OnCell, Danger.Deadly)) continue;
                        storageCell = c;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}

