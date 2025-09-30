// Rimworld 1.6 / C# 7.3
// Source/AI/JobDriver_FellTree.cs
using System;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools
{
    // Do we need to guard this if STC is active? (We don't want to do tree felling if STC is active.)
    public class JobDriver_FellTree : JobDriver_PlantWork
    {
        protected override void Init()
        {
            if (Helpers.TreeSystemArbiterActiveHelper.IsSTCAuthorityActive())
            {
                // Abort initialization – STC controls tree jobs
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            base.Init();

            // Defensive read of Plant
            var plant = Plant;
            if (plant != null && plant.def?.plant?.harvestedThingDef != null && plant.CanYieldNow())
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
                if (Helpers.TreeSystemArbiterActiveHelper.IsSTCAuthorityActive()) return; // STC: skip destroy
                try
                {
                    // Prefer the driver job (stable) rather than actor.CurJob which may change
                    var target = job?.GetTarget(ind);
                    var thing = target?.Thing;

                    if (thing == null) return;
                    if (thing.Destroyed) return;

                    // Ensure it's still a plant (extra safety)
                    if (thing is Plant)
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                    else
                    {
                        // If it's not a Plant but still exists, be conservative and avoid destroying unexpected things.
                        LogDebug(
                        $"[SurvivalTools] FellTree toil expected a Plant but found {thing.GetType().Name}: {thing} — skipping destroy.",
                        $"FellTree_NotPlant_{thing?.GetType().Name}_{thing?.ThingID}"
                        );

                    }
                }
                catch (System.Exception ex)
                {
                    // Critical exception — keep semantics but route through centralized logger
                    LogError($"[SurvivalTools] Exception in FellTree.DestroyThing initAction: {ex}");
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}
