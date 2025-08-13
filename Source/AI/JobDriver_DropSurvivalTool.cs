using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SurvivalTools
{
    public class JobDriver_DropSurvivalTool : JobDriver
    {
        private const int DurationTicks = 30;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // No reservations needed; we’re dropping from inventory.
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // If job target becomes invalid at any point, abort.
            this.FailOn(() => TargetThingA == null);
            this.FailOn(() => pawn.Dead || pawn.Destroyed);

            // Must have an inventory and contain the target before we proceed.
            this.FailOn(() => pawn.inventory == null);
            this.FailOn(() => !pawn.inventory.innerContainer.Contains(TargetThingA));

            // Stop moving and wait a short moment (UI/animation breathing room).
            yield return new Toil
            {
                initAction = () => pawn.pather?.StopDead(),
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = DurationTicks
            };

            // Perform the drop from inventory at/near the pawn.
            yield return new Toil
            {
                initAction = () =>
                {
                    var inv = pawn.inventory?.innerContainer;
                    if (inv == null)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    // Try to drop the exact target tool near the pawn.
                    if (!inv.TryDrop(TargetThingA, pawn.Position, pawn.MapHeld, ThingPlaceMode.Near, out var _))
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    // If we get here, drop succeeded.
                    ReadyForNextToil();
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}

