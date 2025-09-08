// Rimworld 1.6 / C# 7.3
// Pawn_SurvivalToolAssignmentTracker.cs
using Verse;
using RimWorld;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools
{
    /// <summary>
    /// Per-pawn component that stores the current survival tool assignment,
    /// forced tool state, and optimization cooldown bookkeeping.
    /// </summary>
    public class Pawn_SurvivalToolAssignmentTracker : ThingComp
    {
        private SurvivalToolAssignment _currentAssignment;
        public SurvivalToolForcedHandler forcedHandler;

        private int _lastOptimizedTick = -99999;   // last time we "optimized"
        private int _customOptimizeInterval = -1;  // next-interval override (randomized window), -1 = use default

        /// <summary>
        /// Current survival tool assignment for this pawn.
        /// Lazily falls back to the database default if unset or invalid.
        /// </summary>
        public SurvivalToolAssignment CurrentSurvivalToolAssignment
        {
            get
            {
                if (_currentAssignment == null)
                {
                    _currentAssignment = Current.Game?.GetComponent<SurvivalToolAssignmentDatabase>()?.DefaultSurvivalToolAssignment();
                }
                return _currentAssignment;
            }
            set
            {
                _currentAssignment = value;
            }
        }

        /// <summary>
        /// Whether the optimizer should run again now.
        /// Uses a longer interval when AutoTool is enabled (we opportunistically pick up tools anyway),
        /// and a shorter interval when AutoTool is disabled (we rely on periodic optimization more).
        /// </summary>
        public bool NeedsOptimization
        {
            get
            {
                int optimizeInterval;

                if (_customOptimizeInterval > 0)
                {
                    optimizeInterval = _customOptimizeInterval;
                }
                else
                {
                    // ~24 in-game hours with AutoTool, ~1 hour otherwise
                    optimizeInterval = (SurvivalTools.Settings?.autoTool == true)
                        ? 60000
                        : GenDate.TicksPerHour;
                }

                // Use subtraction to avoid wrap-related edge cases
                return Find.TickManager.TicksGame - _lastOptimizedTick > optimizeInterval;
            }
        }

        /// <summary>
        /// Marks optimization as completed and (optionally) schedules the next window.
        /// </summary>
        public void Optimized(int minTicks = -1, int maxTicks = -1)
        {
            _lastOptimizedTick = Find.TickManager.TicksGame;

            if (minTicks > 0 && maxTicks > 0)
            {
                _customOptimizeInterval = Rand.Range(minTicks, maxTicks);
            }
            else
            {
                _customOptimizeInterval = -1; // revert to default cadence
            }
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            // Ensure forced handler always exists
            if (forcedHandler == null)
                forcedHandler = new SurvivalToolForcedHandler();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();

            Scribe_References.Look(ref _currentAssignment, "currentAssignment");
            Scribe_Deep.Look(ref forcedHandler, "forcedHandler");
            Scribe_Values.Look(ref _lastOptimizedTick, "lastOptimizedTick", -99999);
            Scribe_Values.Look(ref _customOptimizeInterval, "customOptimizeInterval", -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Defensive: never leave this null after load
                if (forcedHandler == null)
                    forcedHandler = new SurvivalToolForcedHandler();

                // Validate the saved assignment still exists in the db; if not, fall back to default on next access
                if (_currentAssignment != null)
                {
                    var db = Current.Game?.GetComponent<SurvivalToolAssignmentDatabase>();
                    if (db == null || db.AllSurvivalToolAssignments == null || !db.AllSurvivalToolAssignments.Contains(_currentAssignment))
                    {
                        var pawn = parent as Pawn;
                        var who = pawn?.LabelShort ?? parent?.ToString() ?? "unknown pawn";
                        if (ShouldLogWithCooldown($"InvalidAssignment_{who}"))
                            Log.Warning($"[SurvivalTools] {who} had an invalid survival tool assignment; resetting to default.");
                        _currentAssignment = null; // lazy default on next get
                    }
                }
            }
        }
    }
}
