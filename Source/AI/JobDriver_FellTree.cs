using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools
{
    public class JobDriver_FellTree : JobDriver_PlantWork
    {
        protected override void Init()
        {
            base.Init();

            // Give Plants XP only if the plant could yield (mirrors vanilla harvest logic)
            if (Plant?.def?.plant?.harvestedThingDef != null && Plant.CanYieldNow())
            {
                xpPerTick = 0.085f;
            }
            else
            {
                xpPerTick = 0f;
            }
        }

        protected override Toil PlantWorkDoneToil()
        {
            return DestroyThing(TargetIndex.A);
        }

        // Simple, instant-done toil that destroys the targeted thing (plant)
        private Toil DestroyThing(TargetIndex ind)
        {
            var toil = new Toil();
            toil.initAction = () =>
            {
                var actor = pawn; // avoid capturing 'toil' inside its own initAction
                var thing = actor?.jobs?.curJob.GetTarget(ind).Thing;

                if (thing != null && !thing.Destroyed)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}
