// RimWorld 1.6 / C# 7.3
// Source/ToolAssignments/LegacyAssignmentForwarders.cs
// Phase 11.11: Legacy manual assignment system forwarders for save compatibility
// Manual tool assignment profiles replaced by automatic AssignmentSearch system
// forcedHandler functionality migrated to Pawn_ForcedToolTracker

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools
{
    /// <summary>
    /// Legacy GameComponent for manual tool assignment profiles.
    /// Phase 11.11: Replaced by automatic AssignmentSearch. Kept for save compatibility only.
    /// Loads old data gracefully but doesn't use it.
    /// </summary>
    [Obsolete("Phase 11.11: Manual assignment system replaced by automatic AssignmentSearch. Data loaded but unused.", false)]
    public class SurvivalToolAssignmentDatabase : GameComponent
    {
        private bool initialized = false;
        private List<SurvivalToolAssignment> survivalToolAssignments = new List<SurvivalToolAssignment>();

        public SurvivalToolAssignmentDatabase(Game game) { }

        public override void FinalizeInit()
        {
            // Phase 11.11: No longer generate starting assignments (automatic system used instead)
            if (!initialized)
                initialized = true;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Collections.Look(ref survivalToolAssignments, "survivalToolAssignments", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (survivalToolAssignments == null) survivalToolAssignments = new List<SurvivalToolAssignment>();

                // Defensive: ensure filters/labels exist on all entries
                foreach (var assignment in survivalToolAssignments)
                {
                    if (assignment != null)
                    {
                        if (assignment.filter == null) assignment.filter = new ThingFilter();
                        if (assignment.label.NullOrEmpty()) assignment.label = "Unnamed";
                    }
                }

                // Migration log
                if (initialized && survivalToolAssignments.Count > 0 && IsDebugLoggingEnabled)
                    LogInfo($"[SurvivalTools] Loaded {survivalToolAssignments.Count} legacy tool assignment profiles (now unused - automatic assignment active)");
            }
        }

        // Stub methods for any remaining code references
        public List<SurvivalToolAssignment> AllSurvivalToolAssignments => survivalToolAssignments ?? new List<SurvivalToolAssignment>();

        public SurvivalToolAssignment DefaultSurvivalToolAssignment() =>
            survivalToolAssignments?.FirstOrDefault() ?? new SurvivalToolAssignment(1, "Default");

        public AcceptanceReport TryDelete(SurvivalToolAssignment toolAssignment) =>
            new AcceptanceReport("Phase 11.11: Manual assignment system disabled. Use automatic tool assignment instead.");

        public SurvivalToolAssignment MakeNewSurvivalToolAssignment()
        {
            var assignment = new SurvivalToolAssignment(1, "New");
            return assignment;
        }
    }

    /// <summary>
    /// Legacy tool assignment profile class.
    /// Phase 11.11: Replaced by automatic tool scoring. Kept for save compatibility only.
    /// </summary>
    [Obsolete("Phase 11.11: Manual assignment profiles replaced by automatic tool scoring.", false)]
    public class SurvivalToolAssignment : IExposable, ILoadReferenceable
    {
        public int uniqueId;
        public string label;
        public ThingFilter filter = new ThingFilter();

        public SurvivalToolAssignment() { }

        public SurvivalToolAssignment(int uniqueId, string label)
        {
            this.uniqueId = uniqueId;
            this.label = string.IsNullOrEmpty(label) ? "Unnamed" : label;
            if (filter == null) filter = new ThingFilter();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref uniqueId, "uniqueId", 0);
            Scribe_Values.Look(ref label, "label", "Unnamed");
            Scribe_Deep.Look(ref filter, "filter");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (string.IsNullOrEmpty(label)) label = "Unnamed";
                if (filter == null) filter = new ThingFilter();
            }
        }

        // ILoadReferenceable: keep a stable reference key independent of label changes
        public string GetUniqueLoadID() => $"SurvivalToolAssignment_{uniqueId}";

        public override string ToString() => $"{label} (ID: {uniqueId})";

        // QoL helpers (non-breaking)
        public string LabelCap => (label ?? "Unnamed").CapitalizeFirst();
        public bool Allows(Thing t) => true; // Legacy stub - always allow
        public void Rename(string newLabel) => label = string.IsNullOrEmpty(newLabel) ? "Unnamed" : newLabel.Trim();
    }

    /// <summary>
    /// Legacy per-pawn assignment tracker.
    /// Phase 11.11: Assignment profiles replaced by Pawn_ForcedToolTracker. 
    /// Migrates forcedHandler data on first load.
    /// </summary>
    [Obsolete("Phase 11.11: Assignment tracking replaced by Pawn_ForcedToolTracker. forcedHandler migrated automatically.", false)]
    public class Pawn_SurvivalToolAssignmentTracker : ThingComp
    {
        private SurvivalToolAssignment _currentAssignment;
        public SurvivalToolForcedHandler forcedHandler; // MIGRATED to Pawn_ForcedToolTracker
        private int _lastOptimizedTick = -99999;
        private int _customOptimizeInterval = -1;

        /// <summary>
        /// Current survival tool assignment for this pawn.
        /// Phase 11.11: No longer used by automatic system, but kept for save compatibility.
        /// </summary>
        public SurvivalToolAssignment CurrentSurvivalToolAssignment
        {
            get => _currentAssignment;
            set => _currentAssignment = value;
        }

        /// <summary>
        /// Phase 11.11: No longer used. AssignmentSearch handles all optimization decisions.
        /// </summary>
        public bool NeedsOptimization => false;

        /// <summary>
        /// Phase 11.11: No-op. AssignmentSearch handles optimization timing.
        /// </summary>
        public void Optimized(int minTicks = -1, int maxTicks = -1) { }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
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
                // MIGRATION: Copy forcedHandler to new standalone comp
                if (forcedHandler != null && parent is Pawn pawn)
                {
                    var newComp = pawn.TryGetComp<Pawn_ForcedToolTracker>();
                    if (newComp != null)
                    {
                        // Only migrate if new comp doesn't have data yet (avoid overwriting newer data)
                        if (newComp.forcedHandler == null || !newComp.forcedHandler.SomethingForced)
                        {
                            newComp.forcedHandler = forcedHandler;
                            if (IsDebugLoggingEnabled && forcedHandler.SomethingForced)
                                LogInfo($"[SurvivalTools] Migrated {forcedHandler.ForcedTools?.Count ?? 0} forced tool(s) for {pawn.LabelShort}");
                        }
                    }
                }

                // Defensive: never leave this null after load
                if (forcedHandler == null)
                    forcedHandler = new SurvivalToolForcedHandler();

                // Validate the saved assignment still exists in the db; if not, just clear it
                if (_currentAssignment != null)
                {
                    var db = Current.Game?.GetComponent<SurvivalToolAssignmentDatabase>();
                    if (db == null || db.AllSurvivalToolAssignments == null || !db.AllSurvivalToolAssignments.Contains(_currentAssignment))
                    {
                        _currentAssignment = null;
                    }
                }
            }
        }
    }
}
