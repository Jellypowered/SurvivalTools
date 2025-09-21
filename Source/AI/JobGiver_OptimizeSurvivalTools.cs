// RimWorld 1.6 / C# 7.3
// Source/AI/JobGiver_OptimizeSurvivalTools.cs
using System;
using RimWorld;
using Verse;
using Verse.AI;
using SurvivalTools.Assign;

namespace SurvivalTools
{
    /// <summary>
    /// OBSOLETE: Phase 4-6 Legacy system. Replaced by AssignmentSearch.TryUpgradeFor and PreWork_AutoEquip.
    /// Kept as stub for compatibility during refactor transition.
    /// </summary>
    [Obsolete("Phase 4-6 Legacy: Use AssignmentSearch.TryUpgradeFor or PreWork_AutoEquip instead", false)]
    public class JobGiver_OptimizeSurvivalTools : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Legacy optimization system gutted - Phase 6 PreWork_AutoEquip handles tool assignment
            if (SurvivalTools.Settings?.toolOptimization != true) return null;
            if (!pawn.CanUseSurvivalTools()) return null;

            // Forward to new Phase 6 assignment system
            // AssignmentSearch.TryUpgradeFor handles the actual optimization logic now
            try
            {
                var tracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
                if (tracker != null)
                {
                    // Let Phase 6 handle assignment upgrades - no job needed from here
                    // PreWork_AutoEquip will handle tool acquisition as part of work flow
                    return null;
                }
            }
            catch
            {
                // Defensive: if Phase 6 system fails, don't break the game
            }

            return null;
        }

        /// <summary>
        /// OBSOLETE: Public API kept for compatibility. Use AssignmentSearch.TryUpgradeFor instead.
        /// </summary>
        [Obsolete("Phase 4-6 Legacy: Use AssignmentSearch.TryUpgradeFor instead", false)]
        public static Job TryOptimizeToolsFor(Pawn pawn)
        {
            if (pawn?.TryGetComp<Pawn_SurvivalToolAssignmentTracker>() != null)
            {
                // Forward to Phase 6 assignment system
                return null; // PreWork_AutoEquip handles this automatically now
            }
            return null;
        }
    }
}