// RimWorld 1.6 / C# 7.3
// Source/WorldComponent_DelayedValidation.cs
using RimWorld.Planet;
using Verse;
using SurvivalTools.Helpers;

namespace SurvivalTools
{
    /// <summary>
    /// World component that handles delayed job validation after game load
    /// </summary>
    public class WorldComponent_DelayedValidation : WorldComponent
    {
        private int scheduledValidationTick = -1;
        private bool hasValidated = false;

        public WorldComponent_DelayedValidation(World world) : base(world)
        {
        }

        public override void WorldComponentTick()
        {
            if (scheduledValidationTick > 0 && !hasValidated && Find.TickManager.TicksGame >= scheduledValidationTick)
            {
                hasValidated = true;
                scheduledValidationTick = -1;

                Log.Message("[SurvivalTools.JobValidation] Executing delayed job validation after tool stats have been properly initialized.");
                SurvivalToolValidation.ValidateExistingJobs("delayed validation after game load");
            }
        }

        public void ScheduleValidation(int targetTick)
        {
            if (!hasValidated)
            {
                scheduledValidationTick = targetTick;
                Log.Message($"[SurvivalTools.JobValidation] Scheduled validation for tick {targetTick} (current: {Find.TickManager.TicksGame})");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // Don't save validation state - we want it to reset each time we load
        }
    }
}
