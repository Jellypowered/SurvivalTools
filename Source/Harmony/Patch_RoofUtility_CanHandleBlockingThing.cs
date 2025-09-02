// Rimworld 1.6 / C# 7.3
// Patch_RoofUtility_CanHandleBlockingThing.cs
using HarmonyLib;
using RimWorld;
using Verse;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(RoofUtility))]
    [HarmonyPatch(nameof(RoofUtility.CanHandleBlockingThing))]
    public static class Patch_RoofUtility_CanHandleBlockingThing
    {
        public static void Postfix(ref bool __result, Thing blocker, Pawn worker)
        {
            // Early outs
            if (!__result) return;                         // vanilla already rejected
            if (worker == null) return;                    // no worker
            if (blocker?.def?.plant?.IsTree != true) return; // non-tree blockers are vanilla

            // If pawn can't meet our tree-felling requirements, deny handling tree blockers.
            if (!worker.CanFellTrees())
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                {
                    // Cooldown to avoid spam when many roof cells hit the same blocker.
                    var key = $"RoofTreeBlock_{worker.ThingID}_{blocker.ThingID}";
                    if (SurvivalToolUtility.ShouldLogWithCooldown(key))
                    {
                        // Use safe labels (LabelShort is fine on both Pawn/Thing but guard anyway)
                        var w = worker.LabelShort ?? worker.LabelCap ?? "pawn";
                        var b = blocker.LabelShort ?? blocker.def?.label ?? "tree";
                        Log.Message($"[SurvivalTools] {w} cannot handle tree blocker {b} (missing tree-felling tools).");
                    }
                }
                __result = false;
            }
        }
    }
}
