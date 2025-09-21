// RimWorld 1.6 / C# 7.3
// Source/AI/AutoToolPickup_UtilityIntegrated.cs
using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools.HarmonyStuff
{
    /// <summary>
    /// OBSOLETE: Phase 5-6 Legacy system. Replaced by PreWork_AutoEquip gating system.
    /// Kept as stub for compatibility during refactor transition.
    /// </summary>
    [Obsolete("Phase 5-6 Legacy: Replaced by PreWork_AutoEquip - auto-tool pickup now handled by JobGate", false)]
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    public static class Patch_JobGiver_Work_TryIssueJobPackage_AutoTool
    {
        public static void Postfix(Pawn pawn, JobIssueParams jobParams, ref ThinkResult __result)
        {
            // Legacy auto-tool pickup system gutted - Phase 6 PreWork_AutoEquip handles this
            // No need to intercept JobGiver_Work anymore since PreWork_AutoEquip provides
            // seamless tool acquisition as part of the job gate flow

            // Phase 6 handles:
            // - Tool requirement validation via JobGate
            // - Automatic tool pickup via PreWork_AutoEquip 
            // - Hardcore mode gating via StatGatingHelper
            // - Assignment policy enforcement

            return;
        }

        /// <summary>
        /// OBSOLETE: Public API kept for compatibility. Use PreWork_AutoEquip system instead.
        /// </summary>
        [Obsolete("Phase 5-6 Legacy: Use PreWork_AutoEquip instead", false)]
        public static bool ShouldAttemptAutoTool(Job job, Pawn pawn, out System.Collections.Generic.List<StatDef> requiredStats)
        {
            requiredStats = null;
            // Legacy API - Phase 6 PreWork_AutoEquip handles tool requirements automatically
            return false;
        }

        /// <summary>
        /// OBSOLETE: Public API kept for compatibility. Use PreWork_AutoEquip system instead.
        /// </summary>
        [Obsolete("Phase 5-6 Legacy: Use PreWork_AutoEquip instead", false)]
        public static bool PawnHasHelpfulTool(Pawn pawn, System.Collections.Generic.List<StatDef> stats)
        {
            // Legacy API - Phase 6 handles tool validation internally
            return true; // Assume Phase 6 system handles this
        }
    }
}