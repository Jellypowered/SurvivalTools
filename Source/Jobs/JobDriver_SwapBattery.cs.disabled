// RimWorld 1.6 / C# 7.3
// Source/Jobs/JobDriver_SwapBattery.cs
// Phase 12: Battery system v2 - battery swapping job

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools
{
    /// <summary>
    /// Job driver for swapping batteries in powered tools.
    /// Self-targeted job that swaps the battery in a carried tool.
    /// </summary>
    public class JobDriver_SwapBattery : JobDriver
    {
        private const TargetIndex ToolInd = TargetIndex.A;
        private const TargetIndex BatteryInd = TargetIndex.B;

        private Thing Tool => job.GetTarget(ToolInd).Thing;
        private Thing Battery => job.GetTarget(BatteryInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // No reservations needed for self-targeted battery swap
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(ToolInd);
            this.FailOnDestroyedOrNull(BatteryInd);

            // Toil 1: Go to the battery if needed (should already be in inventory)
            yield return Toils_Goto.GotoThing(BatteryInd, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(BatteryInd);

            // Toil 2: Take battery to inventory if not already there
            yield return Toils_Haul.TakeToInventory(BatteryInd, 1);

            // Toil 3: Perform the swap
            Toil swapToil = new Toil();
            swapToil.initAction = delegate ()
            {
                Pawn actor = swapToil.actor;
                Thing tool = Tool;
                Thing battery = Battery;

                if (tool == null || battery == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                var powerComp = tool.TryGetComp<CompPowerTool>();
                if (powerComp == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // Eject old battery if present
                Thing oldBattery = powerComp.EjectBattery();
                if (oldBattery != null)
                {
                    // Try to put old battery in pawn's inventory
                    if (!actor.inventory.innerContainer.TryAdd(oldBattery))
                    {
                        // If inventory full, drop it
                        GenPlace.TryPlaceThing(oldBattery, actor.Position, actor.Map, ThingPlaceMode.Near);
                    }
                }

                // Insert new battery
                if (!powerComp.TryInsertBattery(battery))
                {
                    ST_Logging.LogWarning($"[Power] Failed to insert battery into {tool.LabelShort}");
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // Success message
                if (Prefs.DevMode)
                {
                    ST_Logging.LogDebug($"[Power] {actor.LabelShort} swapped battery in {tool.LabelShort}");
                }
            };
            swapToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return swapToil;
        }
    }
}
