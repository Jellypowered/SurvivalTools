//Rimworld 1.6 / C# 7.3
// JobDriver_HarvestTree.cs
using RimWorld;
using Verse;
using Verse.AI;
using System;
using static SurvivalTools.ST_Logging;

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
                try
                {
                    var plant = Plant; // use base property which is null-safe
                    var actor = pawn;

                    if (plant == null || plant.Destroyed || actor == null || actor.Destroyed)
                    {
                        return;
                    }

                    // Call PlantCollected in a guarded way — signature mismatches on some RW variants caused crashes previously.
                    // We catch exceptions and only log when debug logging is enabled to avoid noisy spam.
                    try
                    {
                        plant.PlantCollected(actor, PlantDestructionMode.Cut);
                    }
                    catch (Exception innerEx)
                    {
                        Log.ErrorOnce($"[SurvivalTools] PlantCollected threw in JobDriver_HarvestTree for {plant} : {innerEx}", 9832174); // leave error unchanged
                        // Fallback: don't crash; we can't reliably emulate PlantCollected across versions here.
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce($"[SurvivalTools] Unexpected exception in HarvestTree PlantWorkDoneToil.initAction: {ex}", 9832175); // leave error unchanged
                }
            };
            t.defaultCompleteMode = ToilCompleteMode.Instant;
            return t;
        }
    }
}
