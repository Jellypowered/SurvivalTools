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

        private Thing ToolToDrop => job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => !pawn.inventory.innerContainer.Contains(ToolToDrop));

            yield return Toils_General.Wait(DurationTicks, TargetIndex.None);

            yield return Toils_General.DoAtomic(() =>
            {
                if (!pawn.inventory.innerContainer.TryDrop(ToolToDrop, pawn.Position, pawn.Map, ThingPlaceMode.Near, out Thing droppedTool))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // Try to find appropriate storage for the dropped tool
                if (TryFindStorageForTool(droppedTool, out IntVec3 storageCell))
                {
                    // Create a haul job to move the tool to storage
                    var haulJob = HaulAIUtility.HaulToStorageJob(pawn, droppedTool, false);
                    if (haulJob != null)
                    {
                        pawn.jobs.jobQueue.EnqueueFirst(haulJob);
                    }
                }
            });
        }

        private bool TryFindStorageForTool(Thing tool, out IntVec3 storageCell)
        {
            storageCell = IntVec3.Invalid;

            // Try to find any storage cell that accepts this tool, regardless of priority
            // Check storage areas and stockpiles
            var map = pawn.Map;
            var faction = pawn.Faction;

            // First try: Find best storage with current priority rules
            if (StoreUtility.TryFindBestBetterStoreCellFor(tool, pawn, map, StoreUtility.CurrentStoragePriorityOf(tool), faction, out storageCell))
            {
                return true;
            }

            // Second try: Find any valid storage cell, even with lower priority
            if (StoreUtility.TryFindBestBetterStoreCellFor(tool, pawn, map, StoragePriority.Unstored, faction, out storageCell))
            {
                return true;
            }

            // Third try: Look for stockpiles that accept this tool category
            var stockpiles = map.zoneManager.AllZones.OfType<Zone_Stockpile>();
            foreach (var stockpile in stockpiles)
            {
                if (stockpile.settings.AllowedToAccept(tool))
                {
                    // Find a good cell in this stockpile
                    var validCell = stockpile.cells.FirstOrDefault(cell =>
                        StoreUtility.IsValidStorageFor(cell, map, tool) &&
                        pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Deadly));

                    if (validCell.IsValid)
                    {
                        storageCell = validCell;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}