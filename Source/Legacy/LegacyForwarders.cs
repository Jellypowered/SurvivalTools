// RimWorld 1.6 / C# 7.3
// Source/Legacy/LegacyForwarders.cs
// Phase 9 Consolidation: Public legacy symbols retained as [Obsolete] no-ops or thin forwarders.
// Phase 11.1: Add compile-time switch for optimizer logic stripping.
// IMPORTANT: Do not remove or rename these classes if referenced by XML <thinkRoot Class="..."> or other mod patches.

using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools // keep original namespace for XML binding
{
    /// <summary>
    /// Legacy optimizer JobGiver retained for XML compatibility only.
    /// Phase 11.1: Internal logic stripped; PreWork_AutoEquip + AssignmentSearch handle all upgrades.
    /// </summary>
    [Obsolete("Phase 11: legacy shim - optimizer replaced by AssignmentSearch. Stub returns null.", false)]
    public class JobGiver_OptimizeSurvivalTools : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Phase 11.9: Dead code removed. PreWork_AutoEquip + AssignmentSearch handle all upgrades.
            return null; // No-op shim for external mod compatibility
        }
    }

    /// <summary>
    /// Legacy auto-pickup utility retained for compatibility only.
    /// Phase 11.1: All methods are no-ops; functionality moved to AssignmentSearch.
    /// </summary>
    [Obsolete("Phase 11: legacy shim - auto-pickup integrated into AssignmentSearch; all methods no-op.", false)]
    public static class AutoToolPickup_UtilityIntegrated
    {
        public static bool ShouldPickUp(Pawn pawn, Thing thing)
        {
            // Phase 11.9: Dead code removed. AssignmentSearch handles all pickup logic.
            return false; // No-op shim for external mod compatibility
        }

        public static void EnqueuePickUp(Pawn pawn, Thing thing)
        {
            // Phase 11.9: Dead code removed. AssignmentSearch handles all enqueue logic.
            // No-op shim for external mod compatibility
        }
    }
}

namespace SurvivalTools.Legacy // alternate legacy namespace safety net
{
    using System;
    using RimWorld;
    using Verse;
    using Verse.AI;

    /// <summary>
    /// Legacy optimizer JobGiver alias (alternate namespace) retained for compatibility.
    /// Phase 11.1: Internal logic stripped.
    /// </summary>
    [Obsolete("Phase 11: legacy shim - optimizer alias (alternate namespace) retained.", false)]
    public class JobGiver_OptimizeSurvivalTools : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Phase 11.9: Dead code removed. No-op shim for external mod compatibility.
            return null;
        }
    }

    /// <summary>
    /// Legacy auto-pickup alias (alternate namespace) retained for compatibility.
    /// Phase 11.1: All methods are no-ops.
    /// </summary>
    [Obsolete("Phase 11: legacy shim - auto-pickup alias retained.", false)]
    public static class AutoToolPickup_UtilityIntegrated
    {
        public static bool ShouldPickUp(Pawn pawn, Thing thing)
        {
            // Phase 11.9: Dead code removed. No-op shim for external mod compatibility.
            return false;
        }

        public static void EnqueuePickUp(Pawn pawn, Thing thing)
        {
            // Phase 11.9: Dead code removed. No-op shim for external mod compatibility.
        }
    }
}

// Extra-hardcore legacy patch shim (no Harmony attributes).
namespace SurvivalTools
{
    /// <summary>
    /// Legacy extra-hardcore patch shim removed; gating handled by JobGate.
    /// Phase 11.1: Retained for compatibility only.
    /// </summary>
    [Obsolete("Phase 11: legacy shim - extra-hardcore patch removed; gating handled by JobGate.", false)]
    public static class Patch_Pawn_JobTracker_ExtraHardcore
    {
        // If any external reflection calls these, provide safe no-op helpers.
        public static bool IsBlocked(Pawn pawn, Job job)
        {
            // Phase 11.9: Dead code removed. JobGate.ShouldBlock is authoritative.
            return false; // No-op shim for external mod compatibility
        }
    }
}
