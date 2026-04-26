// RimWorld 1.6 / C# 7.3
// Source/Game/ST_GameComponents.cs
// Consolidated game/world components for SurvivalTools
// - ST_GameComponent: Enforces tool gating on save loads and maintains bound consumables
// - ST_WorldComponent_DelayedValidation: Handles delayed job validation after game load
//
// Game component for enforcing tool gating on save loads
// - Schedules enforcement after map/pawn initialization
// - Runs once per save load with delay to allow proper initialization
// - Lightweight and allocation-free

using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using SurvivalTools.Helpers;
using SurvivalTools.Scoring;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools
{
    /// <summary>
    /// Game component for tool gating enforcement and bound consumable maintenance.
    /// </summary>
    public sealed class ST_GameComponent : GameComponent
    {
        private int enforceAtTick = -1;

        // Periodic rescue scan for idle colonists that are gated due to missing tools.
        // The normal rescue path (StartJob prefix, WorkGiver postfix) never fires when
        // Pre_HasJobOnThing blocks ALL work before a job is created, leaving pawns wandering
        // forever without tools. This scan detects that situation and queues equip jobs.
        private int _nextIdleRescueTick = 0;
        private const int IdleRescueIntervalTicks = 600; // ~10 seconds at 60 TPS

        // Core gating stats for Hardcore mode (per StatFilters.ShouldBlockJobForMissingStat).
        // Resolved lazily after defs load; null entry = not yet resolved.
        private static List<StatDef> _coreGatingStats = null;

        private static List<StatDef> GetCoreGatingStats()
        {
            if (_coreGatingStats != null) return _coreGatingStats;
            _coreGatingStats = new List<StatDef>();
            foreach (var stat in SurvivalToolUtility.SurvivalToolStats)
            {
                if (stat != null && StatFilters.ShouldBlockJobForMissingStat(stat))
                    _coreGatingStats.Add(stat);
            }
            return _coreGatingStats;
        }

        public ST_GameComponent(Verse.Game game) { }

        public override void StartedNewGame()
        {
            ST_WearService.Clear();
            Schedule();
        }

        public override void LoadedGame()
        {
            ST_WearService.Clear();
            Schedule();
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager?.TicksGame ?? 0;

            // Low-frequency maintenance (every ~5000 ticks ≈ 83s @ 60 TPS) for bound consumable drift cleanup.
            if ((now % 5000) == 0)
            {
                try { ST_BoundConsumables.PruneDrifted(); } catch { /* defensive */ }
            }

            // Periodic idle-rescue scan: find colonists blocked from working due to missing tools
            // and queue equip jobs so they can recover without player intervention.
            if (now >= _nextIdleRescueTick)
            {
                _nextIdleRescueTick = now + IdleRescueIntervalTicks;
                try { TryRescueIdleBlockedColonists(); } catch { /* defensive */ }
            }

            if (enforceAtTick < 0) return;
            if (now >= enforceAtTick)
            {
                enforceAtTick = -1;

                try
                {
                    var settings = SurvivalToolsMod.Settings;
                    if (settings != null && settings.CurrentMode != DifficultyMode.Normal)
                    {
                        Gating.GatingEnforcer.EnforceAllRunningJobs(false);
                    }
                }
                catch (System.Exception ex)
                {
                    LogWarning($"[SurvivalTools] Gating enforcer failed on save load: {ex.Message}");
                }
            }
        }

        private void TryRescueIdleBlockedColonists()
        {
            var settings = SurvivalToolsMod.Settings;
            if (settings == null || !settings.hardcoreMode) return;
            if (!settings.enableAssignments || !settings.assignRescueOnGate) return;

            var maps = Find.Maps;
            if (maps == null) return;

            var stats = GetCoreGatingStats();
            if (stats == null || stats.Count == 0) return;

            float searchRadius = settings.EffectiveSearchRadius * (settings.hardcoreMode ? 0.75f : 1f);
            int pathBudget = settings.EffectivePathBudget;

            for (int mi = 0; mi < maps.Count; mi++)
            {
                var map = maps[mi];
                var pawns = map?.mapPawns?.FreeColonistsSpawned;
                if (pawns == null) continue;

                for (int pi = 0; pi < pawns.Count; pi++)
                {
                    var pawn = pawns[pi];
                    if (pawn == null || pawn.Dead || pawn.Downed || !pawn.Awake()) continue;

                    // Skip pawns already fetching a tool
                    if (Assign.AssignmentSearch.HasAcquisitionPendingOrQueued(pawn)) continue;

                    // Skip colonists currently doing real (non-wander) work — they're handled by
                    // the existing StartJob prefix / GatingEnforcer paths.
                    var curJobDef = pawn.CurJobDef;
                    bool isIdle = curJobDef == null
                        || curJobDef == JobDefOf.GotoWander
                        || curJobDef == JobDefOf.Wait
                        || curJobDef == JobDefOf.Wait_MaintainPosture;
                    if (!isIdle) continue;

                    for (int si = 0; si < stats.Count; si++)
                    {
                        var stat = stats[si];
                        if (!StatGatingHelper.ShouldBlockJobForStat(stat, settings, pawn)) continue;

                        // Only try rescue if pawn is actually assigned to work that uses this stat
                        if (!PawnAssignedToStatWork(pawn, stat)) continue;

                        float baseline = SurvivalToolUtility.GetNoToolBaseline(stat);
                        ToolScoring.GetBestTool(pawn, stat, out float score);
                        if (score > baseline + 0.001f) continue; // already has a tool for this stat

                        // Queue rescue — TryUpgradeFor has its own internal throttle so spam is safe
                        bool queued = Assign.AssignmentSearch.TryUpgradeFor(
                            pawn, stat, 0.001f, searchRadius, pathBudget,
                            Assign.AssignmentSearch.QueuePriority.Front,
                            "IdleRescueScan");

                        if (queued)
                        {
                            if (IsDebugLoggingEnabled)
                                LogDebug($"[IdleRescue] Queued tool search for {pawn.LabelShort} stat={stat.defName}", $"IdleRescue|{pawn.ThingID}|{stat.defName}");
                            break; // one rescue per pawn per scan pass
                        }
                    }
                }
            }
        }

        // Returns true if pawn is assigned to any work type that involves the given stat.
        private static bool PawnAssignedToStatWork(Pawn pawn, StatDef stat)
        {
            try
            {
                var ws = pawn.workSettings;
                if (ws == null) return false;

                if (stat == ST_StatDefOf.DiggingSpeed)
                    return !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Mining) && ws.WorkIsActive(WorkTypeDefOf.Mining);
                if (stat == StatDefOf.ConstructionSpeed || stat == ST_StatDefOf.DeconstructionSpeed || stat == ST_StatDefOf.MaintenanceSpeed)
                    return !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction) && ws.WorkIsActive(WorkTypeDefOf.Construction);
                if (stat == ST_StatDefOf.TreeFellingSpeed || stat == ST_StatDefOf.PlantHarvestingSpeed || stat == ST_StatDefOf.SowingSpeed)
                    return (!pawn.WorkTypeIsDisabled(WorkTypeDefOf.PlantCutting) && ws.WorkIsActive(WorkTypeDefOf.PlantCutting))
                        || (!pawn.WorkTypeIsDisabled(WorkTypeDefOf.Growing) && ws.WorkIsActive(WorkTypeDefOf.Growing));
                if (stat == ST_StatDefOf.ButcheryFleshSpeed)
                {
                    var cooking = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cooking");
                    return cooking != null && !pawn.WorkTypeIsDisabled(cooking) && ws.WorkIsActive(cooking);
                }
                if (stat == ST_StatDefOf.ResearchSpeed)
                    return !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Research) && ws.WorkIsActive(WorkTypeDefOf.Research);
            }
            catch { /* fail open */ }
            return true; // unknown stat: try anyway
        }

        private void Schedule()
        {
            // Give maps/pawns time to spawn properly
            int now = Find.TickManager?.TicksGame ?? 0;
            enforceAtTick = now + 90; // ~1.5 seconds
        }
    }

    /// <summary>
    /// World component that handles delayed job validation after game load.
    /// </summary>
    public sealed class ST_WorldComponent_DelayedValidation : WorldComponent
    {
        private int scheduledValidationTick = -1;
        private bool hasValidated = false;

        public ST_WorldComponent_DelayedValidation(World world) : base(world)
        {
        }

        public override void WorldComponentTick()
        {
            if (scheduledValidationTick > 0 && !hasValidated && Find.TickManager.TicksGame >= scheduledValidationTick)
            {
                hasValidated = true;
                scheduledValidationTick = -1;

                LogInfo("[SurvivalTools.JobValidation] Executing delayed job validation after tool stats have been properly initialized.");
                SurvivalToolValidation.ValidateExistingJobs("delayed validation after game load");
            }
        }

        public void ScheduleValidation(int targetTick)
        {
            if (!hasValidated)
            {
                scheduledValidationTick = targetTick;
                LogInfo($"[SurvivalTools.JobValidation] Scheduled validation for tick {targetTick} (current: {Find.TickManager.TicksGame})");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // Don't save validation state - we want it to reset each time we load
        }
    }
}
