using Verse;
using RimWorld;

namespace SurvivalTools
{
    public class Pawn_SurvivalToolAssignmentTracker : ThingComp
    {
        private SurvivalToolAssignment _currentAssignment;
        public SurvivalToolForcedHandler forcedHandler;
        private int _lastOptimizedTick = -99999;
        private int _customOptimizeInterval = -1;

        public SurvivalToolAssignment CurrentSurvivalToolAssignment
        {
            get
            {
                if (_currentAssignment == null)
                {
                    // Lazy-initialize to the default global assignment
                    _currentAssignment = Current.Game?.GetComponent<SurvivalToolAssignmentDatabase>()?.DefaultSurvivalToolAssignment();
                }
                return _currentAssignment;
            }
            set => _currentAssignment = value;
        }

        public bool NeedsOptimization
        {
            get
            {
                int optimizeInterval;

                // Use custom interval if set, otherwise use setting-based default
                if (_customOptimizeInterval > 0)
                {
                    optimizeInterval = _customOptimizeInterval;
                }
                else
                {
                    // Use different optimization intervals based on AutoTool setting
                    optimizeInterval = (SurvivalTools.Settings?.autoTool == true)
                        ? 60000  // ~24 hours when AutoTool is enabled
                        : GenDate.TicksPerHour; // ~1 hour when AutoTool is disabled
                }

                return Find.TickManager.TicksGame > _lastOptimizedTick + optimizeInterval;
            }
        }

        public void Optimized(int minTicks = -1, int maxTicks = -1)
        {
            _lastOptimizedTick = Find.TickManager.TicksGame;

            // If specific timing is provided, use it for the next optimization check
            if (minTicks > 0 && maxTicks > 0)
            {
                _customOptimizeInterval = Rand.Range(minTicks, maxTicks);
            }
            else
            {
                _customOptimizeInterval = -1; // Reset to use default behavior
            }
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            forcedHandler = new SurvivalToolForcedHandler();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref _currentAssignment, "currentAssignment");
            Scribe_Deep.Look(ref forcedHandler, "forcedHandler");
            Scribe_Values.Look(ref _lastOptimizedTick, "lastOptimizedTick", -99999);
            Scribe_Values.Look(ref _customOptimizeInterval, "customOptimizeInterval", -1);

            // Post-load initialization for older saves
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (forcedHandler == null) forcedHandler = new SurvivalToolForcedHandler();

                // Validate that the assignment still exists
                if (_currentAssignment != null)
                {
                    var db = Current.Game?.GetComponent<SurvivalToolAssignmentDatabase>();
                    if (db?.AllSurvivalToolAssignments?.Contains(_currentAssignment) != true)
                    {
                        if (SurvivalToolUtility.IsDebugLoggingEnabled)
                        {
                            Log.Warning($"[SurvivalTools] Pawn {parent} had invalid tool assignment, resetting to default");
                        }
                        _currentAssignment = null; // Will be lazy-loaded to default
                    }
                }
            }
        }
    }
}
