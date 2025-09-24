// RimWorld 1.6 / C# 7.3
// Source/Game/ST_GameComponent.cs
// WorldComponent_DelayedValidation.cs needs merged into this. All the Game Components can be one file. This should be where they all live. 
// TODO: Merge WorldComponent_DelayedValidation.cs into this file and delete WorldComponent_DelayedValidation.cs
// TODO: Rename this file to ST_GameComponents.cs
// TODO: Rename class to ST_GameComponents
// TODO: Add other game components here as needed and remove their separate files
//
// Game component for enforcing tool gating on save loads
// - Schedules enforcement after map/pawn initialization
// - Runs once per save load with delay to allow proper initialization
// - Lightweight and allocation-free

using Verse;

namespace SurvivalTools
{
    public sealed class ST_GameComponent : GameComponent
    {
        private int enforceAtTick = -1;

        public ST_GameComponent(Game game) { }

        public override void StartedNewGame()
        {
            Helpers.ST_WearService.Clear();
            Schedule();
        }

        public override void LoadedGame()
        {
            Helpers.ST_WearService.Clear();
            Schedule();
        }

        public override void GameComponentTick()
        {
            if (enforceAtTick < 0) return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (now >= enforceAtTick)
            {
                enforceAtTick = -1;

                try
                {
                    var settings = SurvivalTools.Settings;
                    if (settings != null && settings.CurrentMode != DifficultyMode.Normal)
                    {
                        Gating.GatingEnforcer.EnforceAllRunningJobs(false);
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[SurvivalTools] Gating enforcer failed on save load: {ex.Message}");
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
}