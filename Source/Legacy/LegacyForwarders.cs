// RimWorld 1.6 / C# 7.3
// Source/Legacy/LegacyForwarders.cs
// Phase 9 Consolidation: Public legacy symbols retained as [Obsolete] no-ops or thin forwarders.
// IMPORTANT: Do not remove or rename these classes if referenced by XML <thinkRoot Class="..."> or other mod patches.

using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools // keep original namespace for XML binding
{
    [Obsolete("Legacy optimizer replaced by AssignmentSearch. Stub returns null.", false)]
    public class JobGiver_OptimizeSurvivalTools : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            // PreWork_AutoEquip + AssignmentSearch handle upgrades now.
            return null; // No-op to avoid duplicate acquisition logic.
        }
    }

    [Obsolete("Legacy auto-pickup integrated into AssignmentSearch; all methods no-op.", false)]
    public static class AutoToolPickup_UtilityIntegrated
    {
        public static bool ShouldPickUp(Pawn pawn, Thing thing) => false;
        public static void EnqueuePickUp(Pawn pawn, Thing thing) { /* no-op */ }
        // If older code reflected additional helpers, add them here as no-ops.
    }
}

namespace SurvivalTools.Legacy // alternate legacy namespace safety net
{
    using System;
    using RimWorld;
    using Verse;

    [Obsolete("Legacy optimizer alias (alternate namespace) retained.", false)]
    public class JobGiver_OptimizeSurvivalTools : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn) => null;
    }

    [Obsolete("Legacy auto-pickup alias retained.", false)]
    public static class AutoToolPickup_UtilityIntegrated
    {
        public static bool ShouldPickUp(Pawn pawn, Thing thing) => false;
        public static void EnqueuePickUp(Pawn pawn, Thing thing) { }
    }
}

// Extra-hardcore legacy patch shim (no Harmony attributes).
namespace SurvivalTools
{
    [Obsolete("Legacy extra-hardcore patch shim removed; gating handled by JobGate.", false)]
    public static class Patch_Pawn_JobTracker_ExtraHardcore
    {
        // If any external reflection calls these, provide safe no-op helpers.
        public static bool IsBlocked(Pawn pawn, Job job) => false; // New logic resides in JobGate.ShouldBlock
    }
}
