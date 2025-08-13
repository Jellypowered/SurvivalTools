using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools
{
    // Harvests tree yield, then clears the HarvestPlant designation.
    public class JobDriver_HarvestTree : JobDriver_PlantWork
    {
        protected override void Init()
        {
            base.Init();
            xpPerTick = 0.085f;
        }

        protected override Toil PlantWorkDoneToil()
        {
            var t = new Toil();
            t.initAction = () =>
            {
                var plant = ((Plant)job.targetA.Thing);
                if (plant != null && !plant.Destroyed)
                {
                    // If your enum has 'Cut', use that; otherwise pick the appropriate one from your build.
                    plant.PlantCollected(pawn, PlantDestructionMode.Cut);
                }
            };
            t.defaultCompleteMode = ToilCompleteMode.Instant;
            return t;
        }
    }
}
