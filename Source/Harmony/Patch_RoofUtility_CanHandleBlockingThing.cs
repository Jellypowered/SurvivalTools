// RimWorld 1.6 / C# 7.3
// Source/Harmony/Patch_RoofUtility_CanHandleBlockingThing.cs

// Legacy code: Needs refactoring to use centralized tool stat resolver/gating systems. 
//
// Purpose:
//   Prevents pawns from handling roof-blocking trees unless they meet tree-felling requirements.
//   Integrates SurvivalTools' gating so pawns without the right tools cannot bypass roofs.
//
// Notes:
//   - Early-outs preserve vanilla logic (only intervene if vanilla allowed it).
//   - Debug logging throttled with ShouldLogWithCooldown to prevent spam.
//   - Safe null guards for worker/blocker to avoid rare NREs.
//   - Adds JobFailReason (hardcoded) so players get an in-game message instead of silent blocking.

using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(RoofUtility))]
    [HarmonyPatch(nameof(RoofUtility.CanHandleBlockingThing))]
    public static class Patch_RoofUtility_CanHandleBlockingThing
    {
        public static void Postfix(ref bool __result, Thing blocker, Pawn worker)
        {
            // --- Early exits ---
            if (!__result) return;                          // vanilla already disallowed
            if (worker == null) return;                     // no pawn available
            if (blocker?.def?.plant?.IsTree != true) return; // only care about trees

            // --- SurvivalTools gating ---
            if (!worker.CanFellTrees())
            {
                if (IsDebugLoggingEnabled)
                {
                    // Avoid spam: many roof cells may query the same blocker repeatedly
                    var key = $"RoofTreeBlock_{worker.ThingID}_{blocker.ThingID}";
                    if (ShouldLogWithCooldown(key))
                    {
                        string pawnLabel = worker.LabelShort ?? worker.LabelCap ?? "pawn";
                        string blockerLabel = blocker.LabelShort ?? blocker.def?.label ?? "tree";
                        LogDecision(key, $"[SurvivalTools] {pawnLabel} cannot handle tree blocker {blockerLabel} (missing tree-felling tools).");
                    }
                }

                // Add an in-game job fail reason (non-translated, clear to player)
                JobFailReason.Is("Missing tree-felling tool");

                __result = false; // deny if no tree-felling tools
            }
        }
    }
}
