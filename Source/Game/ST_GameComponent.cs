// RimWorld 1.6 / C# 7.3
// Source/Game/ST_GameComponent.cs
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
            Schedule();
        }

        public override void LoadedGame()
        {
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