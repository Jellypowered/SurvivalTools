// RimWorld 1.6 / C# 7.3
// Source/Harmony/Patch_WorkGiver_GrowerSow_JobOnCell.cs
// Legacy patch to block vanilla tree cutting when tree-felling tools are required.
// TODO: evaluate removal after full tool integration is complete.

using HarmonyLib;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;
using Verse.AI;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(WorkGiver_GrowerSow))]
    [HarmonyPatch(nameof(WorkGiver_GrowerSow.JobOnCell))]
    public static class Patch_WorkGiver_GrowerSow_JobOnCell
    {
        public static void Postfix(ref Job __result, Pawn pawn)
        {
            var job = __result;
            if (job == null) return;
            // Force JobGate decision visibility for ConstructFinishFrames WG (FinishFrame jobs)
            if (job.def == JobDefOf.FinishFrame && pawn != null && SurvivalTools.ST_Logging.IsDebugLoggingEnabled)
            {
                try
                {
                    var wgFinish = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructFinishFrames");
                    if (wgFinish != null)
                        SurvivalTools.Gating.JobGate.ShouldBlock(pawn, wgFinish, job.def, pawn.CurJob != null && pawn.CurJob.playerForced, out var _, out var _, out var _);
                }
                catch { }
                // Do NOT return here; let tree intercept continue if this is actually a CutPlant (handled below)
            }
            // Ensure Sow decisions always produce a JobGate evaluation/log (compat visibility)
            if (job.def == JobDefOf.Sow && pawn != null && SurvivalTools.ST_Logging.IsDebugLoggingEnabled)
            {
                try
                {
                    var wgGrowerSow = DefDatabase<WorkGiverDef>.GetNamedSilentFail("GrowerSow");
                    if (wgGrowerSow != null)
                        SurvivalTools.Gating.JobGate.ShouldBlock(pawn, wgGrowerSow, job.def, pawn.CurJob != null && pawn.CurJob.playerForced, out var _, out var _, out var _);
                }
                catch { }
                return; // nothing else to do for pure sow jobs
            }
            if (job.def != JobDefOf.CutPlant) return;

            var thing = job.targetA.Thing;
            var plant = thing?.def?.plant;
            if (plant == null || !plant.IsTree) return;

            // Use centralized tree-felling gate
            // Needs STC Compatibility: (We don't want to do tree felling if STC is active.)
            if (!Helpers.TreeSystemArbiterActiveHelper.IsSTCAuthorityActive() && pawn != null && pawn.CanFellTrees())
            {
                __result = new Job(ST_JobDefOf.FellTree, job.targetA);
                return;
            }

            // Block vanilla cut if requirements aren't met
            if (!Helpers.TreeSystemArbiterActiveHelper.IsSTCAuthorityActive())
                __result = null; // only null out if we own trees

            if (IsDebugLoggingEnabled && pawn != null)
            {
                var key = $"ST_BlockSowCutTree_{pawn.ThingID}";
                if (ShouldLogWithCooldown(key))
                    LogDecision(key, $"[SurvivalTools] Blocked CutPlant on tree '{thing?.LabelShort ?? "unknown"}' for {pawn.LabelShort}: missing tree-felling tools.");
            }

            // Force a JobGate evaluation/log line for GrowerSow context (ensures consistent one-line decision visibility)
            try
            {
                if (pawn != null && SurvivalTools.ST_Logging.IsDebugLoggingEnabled)
                {
                    var wgGrowerSow = DefDatabase<WorkGiverDef>.GetNamedSilentFail("GrowerSow");
                    if (wgGrowerSow != null)
                    {
                        bool forced = pawn.CurJob != null && pawn.CurJob.playerForced;
                        // Invoke JobGate.ShouldBlock only for logging; we already modified result above if needed.
                        SurvivalTools.Gating.JobGate.ShouldBlock(pawn, wgGrowerSow, job.def, forced, out var _, out var _, out var _);
                    }
                }
            }
            catch { }
        }
    }
}
