// RimWorld 1.6 / C# 7.3
// Source/Pawn_ForcedToolTracker.cs
// Phase 11.11: Extracted forced tool tracking from legacy assignment system
// Replaces Pawn_SurvivalToolAssignmentTracker's forcedHandler functionality

using Verse;

namespace SurvivalTools
{
    /// <summary>
    /// Standalone comp for tracking tools the player has explicitly "forced" a pawn to keep.
    /// Migrated from legacy Pawn_SurvivalToolAssignmentTracker.forcedHandler (Phase 11.11).
    /// </summary>
    public class Pawn_ForcedToolTracker : ThingComp
    {
        public SurvivalToolForcedHandler forcedHandler;

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            if (forcedHandler == null)
                forcedHandler = new SurvivalToolForcedHandler();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref forcedHandler, "forcedHandler");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (forcedHandler == null)
                    forcedHandler = new SurvivalToolForcedHandler();
            }
        }
    }
}
