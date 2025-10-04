// RimWorld 1.6 / C# 7.3
// Source/GameComponent_SurvivalToolsValidation.cs
// Game component to trigger job validation after loading a game
// - Ensures all survival tools have up-to-date stat factors
// - Legacy code, Keep functionality. 
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools
{
    /// <summary>
    /// Game component that triggers job validation when a save is loaded
    /// </summary>
    public class GameComponent_SurvivalToolsValidation : GameComponent
    {
        private bool hasValidatedThisSession = false;
        private int scheduledValidationTick = -1;

        public GameComponent_SurvivalToolsValidation(Game game) : base()
        {
        }

        public override void FinalizeInit()
        {
            // Trigger validation after game initialization with proper timing
            if (!hasValidatedThisSession)
            {
                // Ensure any leftover deterministic counters from previous sessions/maps are cleared.
                try { SurvivalToolUtility.ClearAllCounters(); } catch { }
                // Immediate stat refresh - don't queue it, do it now before anything else
                RefreshAllToolStats();

                // Then queue delayed validation to ensure jobs are checked after everything is settled
                LongEventHandler.QueueLongEvent(() =>
                {
                    // Schedule validation for 3 seconds after load to ensure everything is settled
                    var ticksToWait = 180; // 3 seconds at 60 TPS
                    scheduledValidationTick = Find.TickManager.TicksGame + ticksToWait;

                    // Schedule the ToolFactorCache activation at the same time so computed
                    // factors are cached only after the world is stable. This prevents
                    // early caching during PostLoadInit which has caused CTDs in the past.
                    SurvivalToolUtility.ToolFactorCache.ScheduleActivation(scheduledValidationTick);

                    LogInfo("[SurvivalTools.JobValidation] Tool stats refreshed immediately. Job validation scheduled for 3 seconds after load.");

                }, "SurvivalTools: Scheduling delayed job validation...", false, null);
            }
        }

        public override void GameComponentTick()
        {
            // Execute delayed validation when the time comes
            // Allow the ToolFactorCache to flip into Initialized when the scheduled tick arrives
            SurvivalToolUtility.ToolFactorCache.CheckActivation();

            if (scheduledValidationTick > 0 && !hasValidatedThisSession && Find.TickManager.TicksGame >= scheduledValidationTick)
            {
                hasValidatedThisSession = true;
                scheduledValidationTick = -1;

                LogInfo("[SurvivalTools.JobValidation] Executing delayed job validation after tool stats have been properly initialized.");
                SurvivalToolValidation.ValidateExistingJobs("delayed validation after game load");
            }
        }

        public override void ExposeData()
        {
            // Don't save hasValidatedThisSession - we want it to reset each time we load
            base.ExposeData();

            // Phase 12: Clear transient state before saving to prevent Job reference warnings
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                try
                {
                    // Release all tool reservations first (they reference Job instances)
                    SurvivalTools.Assign.AssignmentSearch.ReleaseAllToolReservations();

                    // Then clear transient dictionaries
                    SurvivalTools.Assign.AssignmentSearch.ClearTransientState();
                    SurvivalTools.Assign.PreWork_AutoEquip.ClearTransientState();
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[SurvivalTools] Error clearing transient state on save: {ex}");
                }
            }
        }

        /// <summary>
        /// Refresh stat factors for all existing survival tools on the map.
        /// This fixes tools that were created with old stat calculation logic.
        /// </summary>
        private void RefreshAllToolStats()
        {
            if (Find.Maps == null) return;

            int refreshedCount = 0;
            var refreshedPawns = new HashSet<Pawn>();

            foreach (var map in Find.Maps)
            {
                if (map?.listerThings?.ThingsInGroup(ThingRequestGroup.HaulableEver) == null) continue;

                foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
                {
                    if (thing is SurvivalTool tool)
                    {
                        // Force immediate stat factor initialization by accessing WorkStatFactors
                        // This triggers the lazy initialization we just added
                        var _ = tool.WorkStatFactors.ToList(); // Force evaluation
                        refreshedCount++;

                        // If this tool is held by a pawn, mark them for stat cache refresh
                        if (tool.ParentHolder is Pawn_InventoryTracker inventory && inventory.pawn != null)
                        {
                            refreshedPawns.Add(inventory.pawn);
                        }
                        else if (tool.ParentHolder is Pawn_EquipmentTracker equipment && equipment.pawn != null)
                        {
                            refreshedPawns.Add(equipment.pawn);
                        }
                    }
                }
            }

            // Force stat cache refresh for all pawns with refreshed tools
            foreach (var pawn in refreshedPawns)
            {
                pawn.health?.capacities?.Notify_CapacityLevelsDirty();
                if (pawn.workSettings != null)
                {
                    pawn.workSettings.Notify_UseWorkPrioritiesChanged();
                }
                // Force full stat recalculation
                pawn.Notify_DisabledWorkTypesChanged();
            }

            if (refreshedCount > 0)
            {
                LogInfo($"[SurvivalTools] Refreshed stat factors for {refreshedCount} existing tools and invalidated stat caches for {refreshedPawns.Count} pawns");
            }
        }
    }
}
