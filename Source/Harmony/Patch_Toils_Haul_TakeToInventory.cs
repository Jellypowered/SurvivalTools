//RimWorld 1.6 / C# 7.3
// Source/Harmony/Patch_Toils_Haul_TakeToInventory.cs
using System;
using HarmonyLib;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;
using SurvivalTools.Helpers;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(Toils_Haul))]
    [HarmonyPatch(nameof(Toils_Haul.TakeToInventory))]
    [HarmonyPatch(new Type[] { typeof(TargetIndex), typeof(Func<int>) })]
    public static class Patch_Toils_Haul_TakeToInventory
    {
        public static void Postfix(Toil __result, TargetIndex ind)
        {
            if (__result == null) return; // defensive

            var origInit = __result.initAction;

            __result.initAction = () =>
            {
                // Run vanilla behavior first, but never trust it to be non-null
                try { origInit?.Invoke(); }
                catch (Exception e)
                {
                    // If vanilla throws, we don’t want our patch to swallow it silently.
                    LogError($"[SurvivalTools] Exception in Toils_Haul.TakeToInventory original initAction: {e}");
                }

                var actor = __result.actor;
                // Must be a valid pawn able to use tools
                if (actor == null || !PawnToolValidator.CanUseSurvivalTools(actor)) return;

                var job = actor.CurJob;
                // Only react to player-forced pickups
                if (job == null || job.playerForced != true) return;

                // Resolve the actual target
                LocalTargetInfo lti;
                try { lti = job.GetTarget(ind); }
                catch
                {
                    // Index out of range or mutated job — play it safe.
                    return;
                }

                var thing = lti.Thing;
                if (thing == null) return;

                // Only SurvivalTool items or tool-stuffs count
                bool isSurvivalTool = ToolClassification.IsSurvivalTool(thing);
                bool isToolStuff = thing.def != null && thing.def.IsToolStuff();
                if (!isSurvivalTool && !isToolStuff) return;

                // Verify it ended up in inventory after vanilla’s operation
                var inv = actor.inventory?.innerContainer;
                if (inv == null) return;

                // We only mark the exact Thing reference if it’s now in inventory.
                // (Avoid guessing among similar stacks to prevent marking the wrong item.)
                if (!inv.Contains(thing)) return;

                var tracker = actor.GetComp<Pawn_SurvivalToolAssignmentTracker>();
                var fh = tracker?.forcedHandler;
                if (fh == null) return;

                fh.SetForced(thing, true);

                if (IsDebugLoggingEnabled)
                {
                    var key = $"ST_ForcedPickup_{actor.ThingID}";
                    if (ShouldLogWithCooldown(key))
                    {
                        var what = isSurvivalTool ? "survival tool" : "tool-stuff";
                        LogDecision(key, $"[SurvivalTools] Marked {what} '{thing.Label}' as forced for {actor.LabelShort}.");
                    }
                }
            };
        }
    }
}
