// RimWorld 1.6 / C# 7.3
// Source/AI/JobDriver_PlantWork.cs
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;
using SurvivalTools.Helpers;
using Verse.Sound;
using System.Linq;

namespace SurvivalTools
{
    /// <summary>
    /// Base driver for plant work (felling/harvesting trees).
    /// Adds defensive null checks, unified stat gating, and diagnostic logging.
    /// </summary>
    public abstract class JobDriver_PlantWork : JobDriver
    {
        protected Plant Plant => job?.targetA.Thing as Plant;
        protected virtual DesignationDef RequiredDesignation => null;

        private float workDone;
        protected float xpPerTick;
        protected const TargetIndex PlantInd = TargetIndex.A;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            var target = job?.GetTarget(TargetIndex.A) ?? LocalTargetInfo.Invalid;
            if (target.IsValid && !pawn.Reserve(target, job, 1, -1, null, errorOnFailed))
                return false;

            pawn.ReserveAsManyAsPossible(job?.GetTargetQueue(TargetIndex.A), job, 1, -1, null);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Init();

            yield return Toils_JobTransforms.MoveCurrentTargetIntoQueue(TargetIndex.A);

            var initExtractTargetFromQueue = Toils_JobTransforms.ClearDespawnedNullOrForbiddenQueuedTargets(
                TargetIndex.A,
                RequiredDesignation == null
                    ? null
                    : new Func<Thing, bool>(t => t != null && Map != null &&
                        Map.designationManager.DesignationOn(t, RequiredDesignation) != null)
            );
            yield return initExtractTargetFromQueue;

            yield return Toils_JobTransforms.SucceedOnNoTargetInQueue(TargetIndex.A);
            yield return Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.A);

            var gotoThing = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                .JumpIfDespawnedOrNullOrForbidden(TargetIndex.A, initExtractTargetFromQueue);

            if (RequiredDesignation != null)
                gotoThing.FailOnThingMissingDesignation(TargetIndex.A, RequiredDesignation);

            gotoThing.FailOn(() => Plant == null);
            yield return gotoThing;

            var cut = new Toil
            {
                tickAction = () =>
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

                        if (plant.def?.plant == null)
                        {
                            EndJobWith(JobCondition.Incompletable);
                            return;
                        }

                        // ✅ Unified stat gating
                        var settings = SurvivalTools.Settings;
                        if (settings?.hardcoreMode == true &&
                            StatGatingHelper.ShouldBlockJobForStat(ST_StatDefOf.TreeFellingSpeed, settings, actor))
                        {
                            EndJobWith(JobCondition.Incompletable);
                            return;
                        }

                        // Degrade tools and grant XP
                        SurvivalToolUtility.TryDegradeTool(actor, ST_StatDefOf.TreeFellingSpeed);
                        actor.skills?.Learn(SkillDefOf.Plants, xpPerTick, direct: false);

                        float statValue = actor.GetStatValue(ST_StatDefOf.TreeFellingSpeed, true);

                        // Unified diagnostics
                        DumpStatDiag(actor, ST_StatDefOf.TreeFellingSpeed, actor.CurJob?.def?.defName);

                        // Trace best tool factor
                        var bestTool = actor.GetBestSurvivalTool(ST_StatDefOf.TreeFellingSpeed);
                        float bestFactor = bestTool?.WorkStatFactors?
                            .FirstOrDefault(m => m.stat == ST_StatDefOf.TreeFellingSpeed)?.value ?? -1f;

                        LogDebug(
                            $"[SurvivalTools.TreeFelling] {actor.LabelShort} TreeFellingSpeed={statValue} bestTool={(bestTool != null ? bestTool.LabelCapNoCount : "null")} bestFactor={bestFactor}",
                            $"TreeFelling_StatValue_{actor.thingIDNumber}"
                        );

                        if (!float.IsFinite(statValue) || statValue < 0f)
                        {
                            statValue = 0f;
                            LogWarning($"[SurvivalTools.TreeFelling] {actor.LabelShort} had invalid TreeFellingSpeed, set to 0");
                        }

                        float growth = Mathf.Clamp01(plant.Growth);
                        float workThisTick = statValue * Mathf.Lerp(3.3f, 1f, growth); // Mature plants need less extra effort
                        if (!float.IsFinite(workThisTick)) workThisTick = 0f;

                        workDone = Mathf.Clamp(workDone + workThisTick, 0f, float.MaxValue / 4f);

                        float harvestWork = plant.def.plant.harvestWork;
                        if (!float.IsFinite(harvestWork) || harvestWork <= 0f) harvestWork = 1f;

                        if (workDone >= harvestWork)
                        {
                            // Yield (safe)
                            var harvestedDef = plant.def.plant.harvestedThingDef;
                            if (harvestedDef != null)
                            {
                                if (actor.RaceProps.Humanlike
                                    && plant.def.plant.harvestFailable
                                    && Rand.Value > actor.GetStatValue(StatDefOf.PlantHarvestYield, true))
                                {
                                    if (Map != null)
                                        MoteMaker.ThrowText((actor.DrawPos + plant.DrawPos) * 0.5f, Map, "TextMote_HarvestFailed".Translate(), 3.65f);
                                }
                                else
                                {
                                    int yieldCount = plant.YieldNow();
                                    if (yieldCount > 0)
                                    {
                                        var product = ThingMaker.MakeThing(harvestedDef);
                                        product.stackCount = Mathf.Max(1, yieldCount);

                                        if (actor.Faction != Faction.OfPlayer)
                                            product.SetForbidden(true, true);

                                        GenPlace.TryPlaceThing(product, actor.Position, Map, ThingPlaceMode.Near);
                                        actor.records?.Increment(RecordDefOf.PlantsHarvested);
                                    }
                                }
                            }

                            plant.def.plant.soundHarvestFinish?.PlayOneShot(actor);

                            workDone = 0f;
                            ReadyForNextToil();
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"[SurvivalTools] Exception in JobDriver_PlantWork.cut.tickAction: {ex}");
                        EndJobWith(JobCondition.Incompletable);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Never,
                activeSkill = () => SkillDefOf.Plants
            };

            cut.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            if (RequiredDesignation != null)
                cut.FailOnThingMissingDesignation(TargetIndex.A, RequiredDesignation);
            cut.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);

            cut.WithProgressBar(TargetIndex.A, () =>
            {
                var total = Plant?.def?.plant?.harvestWork ?? 1f;
                if (!float.IsFinite(total) || total <= 0f) total = 1f;
                return Mathf.Clamp01(workDone / total);
            }, interpolateBetweenActorAndTarget: true, offsetZ: -0.5f);

            cut.PlaySustainerOrSound(() => Plant?.def?.plant?.soundHarvesting);
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
        /// Subclasses should finalize the operation (destroy plant, call PlantCollected, etc.).
        /// </summary>
        protected virtual Toil PlantWorkDoneToil() => null;
    }
}
