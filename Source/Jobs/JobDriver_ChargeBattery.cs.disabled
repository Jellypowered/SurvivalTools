// RimWorld 1.6 / C# 7.3
// Source/Jobs/JobDriver_ChargeBattery.cs
// Phase 12.2: Job driver for inserting batteries into chargers

using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace SurvivalTools
{
    /// <summary>
    /// Job driver for inserting a battery into a battery charger.
    /// Target A: Battery charger building
    /// Target B: Battery to insert
    /// </summary>
    public class JobDriver_ChargeBattery : JobDriver
    {
        private Building Charger => (Building)job.GetTarget(TargetIndex.A).Thing;
        private Thing Battery => job.GetTarget(TargetIndex.B).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Reserve the charger
            if (!pawn.Reserve(Charger, job, 1, -1, null, errorOnFailed))
                return false;

            // Reserve the battery
            if (!pawn.Reserve(Battery, job, 1, -1, null, errorOnFailed))
                return false;

            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Fail if charger or battery are destroyed/despawned
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);

            // Fail if charger is not powered
            this.FailOn(() =>
            {
                var comp = Charger?.GetComp<CompBatteryCharger>();
                return comp == null || !comp.HasRoom;
            });

            // Go to battery
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.B)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.B);

            // Pick up battery
            yield return Toils_Haul.StartCarryThing(TargetIndex.B, false, true, false);

            // Go to charger
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            // Insert battery into charger
            Toil insertBattery = new Toil();
            insertBattery.initAction = () =>
            {
                var chargerComp = Charger?.GetComp<CompBatteryCharger>();
                if (chargerComp == null)
                {
                    Log.Error($"[SurvivalTools] JobDriver_ChargeBattery: No CompBatteryCharger on {Charger}");
                    return;
                }

                var battery = pawn.carryTracker?.CarriedThing;
                if (battery == null)
                {
                    Log.Error($"[SurvivalTools] JobDriver_ChargeBattery: Pawn {pawn} not carrying battery");
                    return;
                }

                // Remove from pawn's carry tracker
                if (pawn.carryTracker.CarriedThing == battery)
                {
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Direct, out Thing droppedThing);
                    battery = droppedThing;
                }

                // Insert into charger
                if (chargerComp.TryInsertBattery(battery))
                {
                    Messages.Message($"Inserted {battery.LabelShort} into {Charger.LabelShort}",
                        Charger, MessageTypeDefOf.TaskCompletion, false);
                }
            };
            insertBattery.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return insertBattery;
        }
    }
}
