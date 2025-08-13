using Verse;
using RimWorld;

namespace SurvivalTools
{
    public class Pawn_SurvivalToolAssignmentTracker : ThingComp
    {
        private SurvivalToolAssignment curSurvivalToolAssignment;
        public SurvivalToolForcedHandler forcedHandler;
        public int nextSurvivalToolOptimizeTick = -99999;

        private Pawn Pawn => (Pawn)parent;

        public SurvivalToolAssignment CurrentSurvivalToolAssignment
        {
            get
            {
                if (curSurvivalToolAssignment == null)
                {
                    var db = Current.Game?.GetComponent<SurvivalToolAssignmentDatabase>();
                    curSurvivalToolAssignment = db != null
                        ? db.DefaultSurvivalToolAssignment()
                        : null;
                }
                return curSurvivalToolAssignment;
            }
            set
            {
                // Fall back to default if someone tries to set null
                var db = Current.Game?.GetComponent<SurvivalToolAssignmentDatabase>();
                curSurvivalToolAssignment = value ?? db?.DefaultSurvivalToolAssignment();
                // Re-run optimization soon after assignment changes
                nextSurvivalToolOptimizeTick = Find.TickManager.TicksGame;
            }
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            if (forcedHandler == null)
                forcedHandler = new SurvivalToolForcedHandler();
        }

        public override void CompTick()
        {
            // Ensure forced handler always exists (defensive against old saves)
            if (forcedHandler == null)
                forcedHandler = new SurvivalToolForcedHandler();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();

            Scribe_Values.Look(ref nextSurvivalToolOptimizeTick, "nextSurvivalToolOptimizeTick", -99999);
            Scribe_Deep.Look(ref forcedHandler, "forcedHandler");
            Scribe_References.Look(ref curSurvivalToolAssignment, "curSurvivalToolAssignment");

            // After loading, make sure the handler exists
            if (Scribe.mode == LoadSaveMode.PostLoadInit && forcedHandler == null)
                forcedHandler = new SurvivalToolForcedHandler();
        }
    }
}
