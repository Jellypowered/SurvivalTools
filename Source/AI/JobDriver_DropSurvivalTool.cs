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

            yield return Toils_General.Wait(DurationTicks, TargetIndex.None);

            yield return Toils_General.DoAtomic(() =>
            {
                if (pawn == null || pawn.Map == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                var toolAsSurvival = ToolToDrop as SurvivalTool;
                var backing = SurvivalToolUtility.BackingThing(toolAsSurvival, pawn) ?? ToolToDrop;

                if (pawn.inventory?.innerContainer == null)
                {
                    LogDebug($"[SurvivalTools.DropTool] {pawn.LabelShort} has no inventory; cannot drop tool.", $"DropTool_NoInventory_{pawn.ThingID}");
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                var inv = pawn.inventory.innerContainer;

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

                // Try dropping via the ThingOwner API
                Thing droppedTool = null;
                bool dropOk = false;
                try
                {
                    if (pawn.Map != null)
                        dropOk = inv.TryDrop(toDrop, pawn.Position, pawn.Map, ThingPlaceMode.Near, out droppedTool);
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
                        if (pawn.Map != null)
                            placed = GenPlace.TryPlaceThing(toDrop, pawn.Position, pawn.Map, ThingPlaceMode.Near);
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
                                Log.Warning($"[SurvivalTools.DropTool] {pawn.LabelShort} failed to drop/place {toDrop.Label}.");
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

                // Clear forced-handler references
                pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>()?.forcedHandler?.SetForced(inventoryInstance, false);
                pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>()?.forcedHandler?.SetForced(droppedTool, false);

                // Try to find storage cell and enqueue haul job if appropriate
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

            if (StoreUtility.TryFindBestBetterStoreCellFor(tool, pawn, map, StoreUtility.CurrentStoragePriorityOf(tool), faction, out storageCell))
                return true;

            if (StoreUtility.TryFindBestBetterStoreCellFor(tool, pawn, map, StoragePriority.Unstored, faction, out storageCell))
                return true;

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

