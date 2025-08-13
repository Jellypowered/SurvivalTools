using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace SurvivalTools
{
    // Base driver for plant work (felling/harvesting trees)
    public abstract class JobDriver_PlantWork : JobDriver
    {
        protected Plant Plant => (Plant)job.targetA.Thing;

        protected virtual DesignationDef RequiredDesignation => null;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            var target = job.GetTarget(TargetIndex.A);
            if (target.IsValid)
            {
                if (!pawn.Reserve(target, job, 1, -1, null, errorOnFailed))
                    return false;
            }

            pawn.ReserveAsManyAsPossible(job.GetTargetQueue(TargetIndex.A), job, 1, -1, null);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Init();

            yield return Toils_JobTransforms.MoveCurrentTargetIntoQueue(TargetIndex.A);

            // Filter queue: remove null/forbidden or missing-designation entries (if RequiredDesignation is set)
            var initExtractTargetFromQueue = Toils_JobTransforms.ClearDespawnedNullOrForbiddenQueuedTargets(
                TargetIndex.A,
                RequiredDesignation == null
                    ? null
                    : new Func<Thing, bool>(t => Map.designationManager.DesignationOn(t, RequiredDesignation) != null)
            );
            yield return initExtractTargetFromQueue;

            yield return Toils_JobTransforms.SucceedOnNoTargetInQueue(TargetIndex.A);
            yield return Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.A);

            var gotoThing = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                .JumpIfDespawnedOrNullOrForbidden(TargetIndex.A, initExtractTargetFromQueue);

            if (RequiredDesignation != null)
            {
                gotoThing.FailOnThingMissingDesignation(TargetIndex.A, RequiredDesignation);
            }

            // Extra safety: target must be a plant
            gotoThing.FailOn(() => Plant == null);

            yield return gotoThing;

            var cut = new Toil();
            cut.tickAction = () =>
            {
                var actor = pawn;
                var plant = Plant;
                if (plant == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // Tool wear while working
                SurvivalToolUtility.TryDegradeTool(actor, ST_StatDefOf.TreeFellingSpeed);

                // Plants XP
                actor.skills?.Learn(SkillDefOf.Plants, xpPerTick, direct: false);

                // Work speed: TreeFellingSpeed scaled by growth (mature = faster)
                var statValue = actor.GetStatValue(ST_StatDefOf.TreeFellingSpeed, true);
                var workThisTick = statValue * Mathf.Lerp(3.3f, 1f, plant.Growth);

                workDone += workThisTick;

                if (workDone >= plant.def.plant.harvestWork)
                {
                    // Yield (if any)
                    if (plant.def.plant.harvestedThingDef != null)
                    {
                        if (actor.RaceProps.Humanlike
                            && plant.def.plant.harvestFailable
                            && Rand.Value > actor.GetStatValue(StatDefOf.PlantHarvestYield, true))
                        {
                            var loc = (actor.DrawPos + plant.DrawPos) * 0.5f;
                            MoteMaker.ThrowText(loc, Map, "TextMote_HarvestFailed".Translate(), 3.65f);
                        }
                        else
                        {
                            var yieldCount = plant.YieldNow();
                            if (yieldCount > 0)
                            {
                                var product = ThingMaker.MakeThing(plant.def.plant.harvestedThingDef);
                                product.stackCount = yieldCount;

                                if (actor.Faction != Faction.OfPlayer)
                                {
                                    product.SetForbidden(true, warnOnFail: true);
                                }

                                GenPlace.TryPlaceThing(product, actor.Position, Map, ThingPlaceMode.Near);
                                actor.records.Increment(RecordDefOf.PlantsHarvested);
                            }
                        }
                    }

                    // Finish sound
                    plant.def.plant.soundHarvestFinish.PlayOneShot(actor);

                    // NOTE: Do NOT call Plant.PlantCollected here (signature varies by RW version).
                    // Subclasses will handle destruction/collection in PlantWorkDoneToil.

                    workDone = 0f;
                    ReadyForNextToil();
                }
            };

            cut.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            if (RequiredDesignation != null)
            {
                cut.FailOnThingMissingDesignation(TargetIndex.A, RequiredDesignation);
            }
            cut.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            cut.defaultCompleteMode = ToilCompleteMode.Never;

            // Removed: WithEffect(EffecterDefOf.Harvest, ...) — not present in your RW 1.6 build.
            cut.WithProgressBar(
                TargetIndex.A,
                () =>
                {
                    var plant = Plant;
                    var total = plant?.def?.plant?.harvestWork ?? 1f;
                    return Mathf.Clamp01(workDone / total);
                },
                interpolateBetweenActorAndTarget: true,
                offsetZ: -0.5f
            );

            cut.PlaySustainerOrSound(() => Plant?.def?.plant?.soundHarvesting);
            cut.activeSkill = () => SkillDefOf.Plants;

            yield return cut;

            var done = PlantWorkDoneToil();
            if (done != null)
                yield return done;

            yield return Toils_Jump.Jump(initExtractTargetFromQueue);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workDone, "workDone", 0f);
        }

        protected virtual void Init() { }

        /// <summary>
        /// Subclasses should finalize the operation here (e.g., destroy plant for felling,
        /// call Plant.PlantCollected(pawn, <mode>) for harvesting, remove designations, etc.).
        /// </summary>
        protected virtual Toil PlantWorkDoneToil() => null;

        private float workDone;
        protected float xpPerTick;

        protected const TargetIndex PlantInd = TargetIndex.A;
    }
}
