// Rimworld 1.6 / C# 7.3
// JobDriver_DropSurvivalTool.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using RimWorld;

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
            // Note: don't FailOn(() => !pawn.inventory.innerContainer.Contains(...))
            // because virtual wrappers may target a wrapper while the inventory contains the backing thing.

            yield return Toils_General.Wait(DurationTicks, TargetIndex.None);

            yield return Toils_General.DoAtomic(() =>
            {
                if (pawn == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                var toolAsSurvival = ToolToDrop as SurvivalTool;
                Thing physicalBacking = SurvivalToolUtility.BackingThing(toolAsSurvival, pawn) ?? ToolToDrop;

                if (pawn.inventory?.innerContainer == null)
                {
                    if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    {
                        var k = $"DropTool_NoInventory_{pawn.ThingID}";
                        if (SurvivalToolUtility.ShouldLogWithCooldown(k))
                            Log.Message($"[SurvivalTools.DropTool] {pawn.LabelShort} has no inventory; cannot drop tool.");
                    }
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                var inv = pawn.inventory.innerContainer;

                // Prefer exact reference match, fallback to def match
                Thing inventoryInstance = null;
                if (physicalBacking != null)
                    inventoryInstance = inv.FirstOrDefault(t => t == physicalBacking);

                if (inventoryInstance == null && physicalBacking != null)
                    inventoryInstance = inv.FirstOrDefault(t => t.def == physicalBacking.def && t.stackCount > 0);

                if (inventoryInstance == null && ToolToDrop != null)
                    inventoryInstance = inv.FirstOrDefault(t => t == ToolToDrop || t.def == ToolToDrop.def);

                if (inventoryInstance == null)
                {
                    if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    {
                        var k = $"DropTool_NotInInventory_{pawn.ThingID}";
                        if (SurvivalToolUtility.ShouldLogWithCooldown(k))
                            Log.Message($"[SurvivalTools.DropTool] {pawn.LabelShort} attempted to drop {physicalBacking?.LabelShort ?? "tool"} but no matching item found in inventory.");
                    }
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // If the found stack has more than 1, split off 1. Otherwise operate on the instance directly.
                Thing toDrop = inventoryInstance.stackCount > 1
                    ? inventoryInstance.SplitOff(1)
                    : inventoryInstance;

                // Try dropping via the ThingOwner API (common RW overload)
                Thing droppedTool = null;
                bool dropOk = false;
                try
                {
                    dropOk = inv.TryDrop(toDrop, pawn.Position, pawn.Map, ThingPlaceMode.Near, out droppedTool);
                }
                catch
                {
                    // If the TryDrop signature differs, fall back to GenPlace below
                    dropOk = false;
                    droppedTool = null;
                }

                if (!dropOk || droppedTool == null)
                {
                    // Fallback: attempt to place on the map directly
                    bool placed = false;
                    try
                    {
                        placed = GenPlace.TryPlaceThing(toDrop, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                    }
                    catch
                    {
                        placed = false;
                    }

                    if (!placed)
                    {
                        // Couldn't place it. Log and try to restore the split-off item back into the inventory.
                        if (SurvivalToolUtility.IsDebugLoggingEnabled)
                        {
                            var k = $"DropTool_PlaceFailed_{pawn.ThingID}";
                            if (SurvivalToolUtility.ShouldLogWithCooldown(k))
                                Log.Warning($"[SurvivalTools.DropTool] {pawn.LabelShort} failed to drop/place {toDrop.Label}.");
                        }

                        // If we had split off a single item, try to merge it back into any compatible stack safely:
                        if (toDrop != inventoryInstance)
                        {
                            try
                            {
                                // Try to merge into an existing stack with available space
                                var sameStack = inv.FirstOrDefault(t => t.def == toDrop.def && t.stackCount < t.def.stackLimit);
                                if (sameStack != null)
                                {
                                    // move as much as we can into the existing stack
                                    int freeSpace = sameStack.def.stackLimit - sameStack.stackCount;
                                    int moveAmount = Math.Min(freeSpace, toDrop.stackCount);
                                    if (moveAmount > 0)
                                    {
                                        sameStack.stackCount += moveAmount;
                                        toDrop.stackCount -= moveAmount;
                                    }

                                    // if nothing left in toDrop, destroy it
                                    if (toDrop.stackCount <= 0)
                                    {
                                        try { toDrop.Destroy(); } catch { /* swallow */ }
                                    }
                                    else
                                    {
                                        // try to add remainder back into inventory
                                        try
                                        {
                                            inv.TryAdd(toDrop);
                                        }
                                        catch
                                        {
                                            // last resort: destroy remainder to avoid leak (very unlikely)
                                            try { toDrop.Destroy(); } catch { }
                                        }
                                    }
                                }
                                else
                                {
                                    // no existing partial stack: try to add the split item back
                                    try
                                    {
                                        inv.TryAdd(toDrop);
                                    }
                                    catch
                                    {
                                        // give up quietly; attempt to destroy to avoid orphaned things in memory
                                        try { toDrop.Destroy(); } catch { }
                                    }
                                }
                            }
                            catch
                            {
                                // swallow restore exceptions to avoid crashing the save
                                try { toDrop.Destroy(); } catch { }
                            }
                        }

                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    // If placed directly we treat the placed object as the droppedTool
                    droppedTool = toDrop;
                }

                // Clear forced-handler references on the inventory instance and dropped thing
                pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>()?.forcedHandler?.SetForced(inventoryInstance, false);
                pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>()?.forcedHandler?.SetForced(droppedTool, false);

                // Try to find storage cell and enqueue haul job if appropriate
                if (TryFindStorageForTool(droppedTool, out IntVec3 storageCell))
                {
                    var haulJob = JobMaker.MakeJob(JobDefOf.HaulToCell, droppedTool, storageCell);
                    haulJob.count = 1;
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

            // 1) Try best storage for current priority
            if (StoreUtility.TryFindBestBetterStoreCellFor(tool, pawn, map, StoreUtility.CurrentStoragePriorityOf(tool), faction, out storageCell))
                return true;

            // 2) Try any storage cell (Unstored)
            if (StoreUtility.TryFindBestBetterStoreCellFor(tool, pawn, map, StoragePriority.Unstored, faction, out storageCell))
                return true;

            // 3) Try stockpiles that accept the tool
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

            return false;
        }
    }
}
