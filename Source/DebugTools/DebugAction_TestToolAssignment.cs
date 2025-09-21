// RimWorld 1.6 / C# 7.3
// Source/DebugTools/DebugAction_TestToolAssignment.cs
//
// Debug action to manually test tool assignment system

using System;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;
using SurvivalTools.Assign;

namespace SurvivalTools.DebugTools
{
    public static class DebugAction_TestToolAssignment
    {
        [DebugAction("SurvivalTools", "Test Tool Assignment", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestToolAssignment()
        {
            try
            {
                Log.Warning("[SurvivalTools.Debug] Testing tool assignment system...");

                // Find a colonist
                var colonist = Find.CurrentMap?.mapPawns?.FreeColonists?.FirstOrDefault();
                if (colonist == null)
                {
                    Log.Warning("[SurvivalTools.Debug] No colonists found on current map");
                    return;
                }

                Log.Warning($"[SurvivalTools.Debug] Testing with colonist: {colonist.LabelShort}");

                // Test if AssignmentSearch is accessible
                bool result = AssignmentSearch.TryUpgradeFor(
                    colonist,
                    ST_StatDefOf.TreeFellingSpeed,
                    0.1f,
                    25f,
                    500,
                    AssignmentSearch.QueuePriority.Append
                );

                Log.Warning($"[SurvivalTools.Debug] AssignmentSearch.TryUpgradeFor returned: {result}");
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools.Debug] Test failed: {ex}");
            }
        }

        [DebugAction("SurvivalTools", "Check Harmony Patches", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void CheckHarmonyPatches()
        {
            try
            {
                Log.Warning("[SurvivalTools.Debug] Checking Harmony patches...");

                var harmony = new HarmonyLib.Harmony("Jelly.SurvivalToolsReborn");
                var patched = HarmonyLib.Harmony.GetAllPatchedMethods().ToList();

                Log.Warning($"[SurvivalTools.Debug] Total patched methods in game: {patched.Count}");

                // Check for our specific patch
                var targetMethod = typeof(Verse.AI.Pawn_JobTracker).GetMethod("TryTakeOrderedJob");
                if (targetMethod == null)
                {
                    Log.Warning("[SurvivalTools.Debug] TryTakeOrderedJob method not found!");
                    return;
                }

                var patchInfo = HarmonyLib.Harmony.GetPatchInfo(targetMethod);
                if (patchInfo == null)
                {
                    Log.Warning("[SurvivalTools.Debug] No patches found on TryTakeOrderedJob");
                    return;
                }

                Log.Warning($"[SurvivalTools.Debug] TryTakeOrderedJob has {patchInfo.Prefixes.Count} prefixes, {patchInfo.Postfixes.Count} postfixes");

                foreach (var prefix in patchInfo.Prefixes)
                {
                    Log.Warning($"[SurvivalTools.Debug] Prefix: {prefix.owner} - {prefix.PatchMethod.DeclaringType?.Name}.{prefix.PatchMethod.Name} (Priority: {prefix.priority})");
                }

                // Check if our patch is there
                bool ourPatchFound = patchInfo.Prefixes.Any(p => p.owner == "Jelly.SurvivalToolsReborn" || p.PatchMethod.DeclaringType?.Name == "PreWork_AutoEquip");
                Log.Warning($"[SurvivalTools.Debug] Our patch found: {ourPatchFound}");
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools.Debug] Harmony check failed: {ex}");
            }
        }
    }
}