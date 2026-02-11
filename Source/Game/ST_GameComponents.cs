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

using RimWorld.Planet;
using Verse;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools
{
    /// <summary>
    /// Game component for tool gating enforcement and bound consumable maintenance.
    /// </summary>
    public sealed class ST_GameComponent : GameComponent
    {
        private int enforceAtTick = -1;

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
