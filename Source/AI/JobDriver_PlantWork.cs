// RimWorld 1.6 / C# 7.3
// JobDriver_PlantWork.cs (defensive / NRE-safe tweaks)
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
        protected Plant Plant => job?.targetA.Thing as Plant;

        protected virtual DesignationDef RequiredDesignation => null;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            var target = job?.GetTarget(TargetIndex.A) ?? LocalTargetInfo.Invalid;
            if (target.IsValid)
            {
                if (!pawn.Reserve(target, job, 1, -1, null, errorOnFailed))
                    return false;
            }

            pawn.ReserveAsManyAsPossible(job?.GetTargetQueue(TargetIndex.A), job, 1, -1, null);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Init();

            // Move current target into queue (vanilla behavior)
            yield return Toils_JobTransforms.MoveCurrentTargetIntoQueue(TargetIndex.A);

            // Filter queue: remove null/forbidden or missing-designation entries (if RequiredDesignation is set)
            var initExtractTargetFromQueue = Toils_JobTransforms.ClearDespawnedNullOrForbiddenQueuedTargets(
                TargetIndex.A,
                RequiredDesignation == null
                    ? null
                    : new Func<Thing, bool>(t => t != null && Map != null && Map.designationManager.DesignationOn(t, RequiredDesignation) != null)
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
                try
                {
                    var actor = pawn;
                    var plant = Plant;

                    if (actor == null || actor.DestroyedOrNull() || plant == null || plant.Destroyed)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    // Defensive: ensure plant.def and plant.def.plant exist
                    if (plant.def == null || plant.def.plant == null)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    // Tool wear while working
                    SurvivalToolUtility.TryDegradeTool(actor, ST_StatDefOf.TreeFellingSpeed);

                    // Plants XP (safe null-propagation)
                    actor.skills?.Learn(SkillDefOf.Plants, xpPerTick, direct: false);

                    // Work speed: TreeFellingSpeed scaled by growth (mature = faster)
                    var statValue = actor.GetStatValue(ST_StatDefOf.TreeFellingSpeed, true);
                    if (!float.IsFinite(statValue) || statValue <= 0f) statValue = 0f;

                    var growth = Mathf.Clamp01(plant.Growth);
                    var workThisTick = statValue * Mathf.Lerp(3.3f, 1f, growth);

                    if (!float.IsFinite(workThisTick)) workThisTick = 0f;
                    workDone = Mathf.Clamp(workDone + workThisTick, 0f, float.MaxValue / 4f);

                    var harvestWork = plant.def.plant.harvestWork;
                    if (!float.IsFinite(harvestWork) || harvestWork <= 0f) harvestWork = 1f;

                    if (workDone >= harvestWork)
                    {
                        // Yield (if any)
                        var harvestedDef = plant.def.plant.harvestedThingDef;
                        if (harvestedDef != null)
                        {
                            if (actor.RaceProps.Humanlike
                                && plant.def.plant.harvestFailable
                                && Rand.Value > actor.GetStatValue(StatDefOf.PlantHarvestYield, true))
                            {
                                // Show a small text mote for failure (safe map access)
                                if (Map != null)
                                {
                                    var loc = (actor.DrawPos + plant.DrawPos) * 0.5f;
                                    MoteMaker.ThrowText(loc, Map, "TextMote_HarvestFailed".Translate(), 3.65f);
                                }
                            }
                            else
                            {
                                var yieldCount = plant.YieldNow();
                                if (yieldCount > 0)
                                {
                                    var product = ThingMaker.MakeThing(harvestedDef);
                                    product.stackCount = Mathf.Max(1, yieldCount);

                                    // For non-player factions, keep it forbidden so player doesn't lose it to AI pawns
                                    if (actor.Faction != Faction.OfPlayer)
                                    {
                                        product.SetForbidden(true, warnOnFail: true);
                                    }

                                    // Try placing near actor; ignore failure (vanilla usually just tries)
                                    GenPlace.TryPlaceThing(product, actor.Position, Map, ThingPlaceMode.Near);
                                    actor.records?.Increment(RecordDefOf.PlantsHarvested);
                                }
                            }
                        }

                        // Finish sound (safe-call)
                        var finishSound = plant.def.plant.soundHarvestFinish;
                        finishSound?.PlayOneShot(actor);

                        // NOTE: Do NOT call Plant.PlantCollected here (signature varies by RW version).
                        // Subclasses will handle destruction/collection in PlantWorkDoneToil.

                        workDone = 0f;
                        ReadyForNextToil();
                    }
                }
                catch (Exception ex)
                {
                    // Defensive catch so a mod mismatch doesn't repeatedly crash the job driver.
                    // Avoid noisy logging; only log if debug flag enabled.
                    if (SurvivalToolUtility.IsDebugLoggingEnabled)
                        Log.ErrorOnce($"[SurvivalTools] Exception in JobDriver_PlantWork.cut.tickAction: {ex}", 1234567);
                    EndJobWith(JobCondition.Incompletable);
                }
            };

            cut.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            if (RequiredDesignation != null)
            {
                cut.FailOnThingMissingDesignation(TargetIndex.A, RequiredDesignation);
            }
            cut.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            cut.defaultCompleteMode = ToilCompleteMode.Never;

            // Progress bar: safe divide (total clamped to >= small epsilon)
            cut.WithProgressBar(
                TargetIndex.A,
                () =>
                {
                    var plant = Plant;
                    var total = plant?.def?.plant?.harvestWork ?? 1f;
                    if (!float.IsFinite(total) || total <= 0f) total = 1f;
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
