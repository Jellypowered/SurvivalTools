// RimWorld 1.6 / C# 7.3
// Source/Harmony/Patch_WorkGiver_Scanner_ToolGate.cs
//
// Purpose: Prevents pawns from taking jobs from WorkGiver_Scanners unless they
// meet tool/stat requirements under Hardcore / Extra Hardcore modes.
// This is an early-phase gate, BEFORE TryGiveJob, so pawns won’t even consider
// jobs they lack the tools for.
//
// Future possibilities:
//  - Instead of silent blocking, we could feed into a visual system
//    (eg. tool icons over pawns, animated actions like swinging a hoe,
//    wrench-turning, microscope oscillation, etc.) to show WHY a pawn won’t work.
//  - Could also tie into tutorial concepts (teach players about tools).

using HarmonyLib;
using RimWorld;
using Verse;
using System.Collections.Generic;
using static SurvivalTools.ST_Logging;
using SurvivalTools.Helpers;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(WorkGiver_Scanner))]
    public static class Patch_WorkGiver_Scanner_ToolGate
    {
        // Reusable buffer to avoid per-loop List allocations
        private static readonly List<StatDef> _tmpStatBuffer = new List<StatDef>(1);

        [HarmonyPrefix]
        [HarmonyPatch(nameof(WorkGiver_Scanner.HasJobOnThing))]
        public static bool Prefix_HasJobOnThing(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            return CheckToolRequirements(__instance, pawn, ref __result);
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(WorkGiver_Scanner.HasJobOnCell))]
        public static bool Prefix_HasJobOnCell(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, bool forced, ref bool __result)
        {
            return CheckToolRequirements(__instance, pawn, ref __result);
        }

        private static bool CheckToolRequirements(WorkGiver_Scanner instance, Pawn pawn, ref bool result)
        {
            var settings = SurvivalTools.Settings;
            if (settings?.hardcoreMode != true || !pawn.CanUseSurvivalTools())
                return true;

            // Only gate jobs that are eligible and enabled in settings
            if (!SurvivalToolUtility.ShouldGateByDefault(instance.def))
                return true;

            // Respect settings toggle for WorkSpeedGlobal jobs
            if (settings.workSpeedGlobalJobGating != null &&
                settings.workSpeedGlobalJobGating.TryGetValue(instance.def.defName, out bool gated) &&
                !gated)
            {
                return true;
            }

            // Get required stats via centralized helper
            List<StatDef> requiredStats = StatGatingHelper.GetStatsForWorkGiver(instance.def);
            if (requiredStats.NullOrEmpty())
                return true;

            foreach (var stat in requiredStats)
            {
                if (StatGatingHelper.ShouldBlockJobForStat(stat, settings))
                {
                    // Avoid per-iteration allocations by reusing a buffer
                    _tmpStatBuffer.Clear();
                    _tmpStatBuffer.Add(stat);

                    if (!pawn.MeetsWorkGiverStatRequirements(_tmpStatBuffer, instance.def))
                    {
                        result = false;

                        if (IsDebugLoggingEnabled)
                        {
                            var key = $"ToolGate_{pawn.ThingID}_{instance.def.defName}_{stat.defName}";
                            if (ShouldLogWithCooldown(key))
                            {
                                Log.Message(
                                    $"[SurvivalTools.ToolGate] {pawn.LabelShort} blocked from job {instance.def.defName} due to missing tool for stat {stat.defName}."
                                );
                            }
                        }

                        return false; // block job
                    }
                }
            }

            return true; // allow job
        }
    }
}
