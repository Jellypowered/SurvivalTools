// RimWorld 1.6 / C# 7.3
// Source/Harmony/Patch_WorkGiver_Scanner_ToolGate.cs
using System.Linq;
//
// Purpose: Prevents pawns from taking jobs from WorkGiver_Scanners unless they
// meet tool/stat requirements under Hardcore / Extra Hardcore modes.
// This is an early-phase gate, BEFORE TryGiveJob, so pawns wonâ€™t even consider
// jobs they lack the tools for.
//
// Future possibilities:
//  - Instead of silent blocking, we could feed into a visual system
//    (eg. tool icons over pawns, animated actions like swinging a hoe,
//    wrench-turning, microscope oscillation, etc.) to show WHY a pawn wonâ€™t work.
//  - Could also tie into tutorial concepts (teach players about tools).

using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using System.Collections.Generic;
using static SurvivalTools.ST_Logging;
using SurvivalTools.Helpers;
using System.Reflection;
using System;

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

            // Get required stats via centralized helper FIRST
            var requiredStats = StatGatingHelper.GetStatsForWorkGiver(instance.def);
            if (requiredStats.NullOrEmpty())
                return true;

            // Only gate jobs that are eligible by default...
            // ...but if ShouldGateByDefault misses a WG, still gate when we positively identified CORE blocking stats
            bool coreStatsPresent = requiredStats.Any(StatFilters.ShouldBlockJobForMissingStat);
            if (!SurvivalToolUtility.ShouldGateByDefault(instance.def) && !coreStatsPresent)
                return true;

            // Respect per-WG toggle for WorkSpeedGlobal jobs (if present)
            if (settings.workSpeedGlobalJobGating != null &&
                settings.workSpeedGlobalJobGating.TryGetValue(instance.def.defName, out bool gated) &&
                !gated)
            {
                return true;
            }

            foreach (var stat in requiredStats)
            {
                if (StatGatingHelper.ShouldBlockJobForStat(stat, settings, pawn))
                {
                    result = false;
                    if (IsDebugLoggingEnabled)
                    {
                        ST_Logging.LogToolGateEvent(pawn, null, stat, "missing tool");
                    }
                    return false;
                }
            }

            return true;
        }

        // -----------------------------------------------------------------
        // Runtime: focused BuildRoof gating to prevent roofing jobs being assigned
        // to pawns that lack ConstructionSpeed tools in extra-hardcore mode.
        // We patch concrete WorkGiver_BuildRoof methods at startup so checks run
        // earlier than generic Scanner logic and avoid assigning doomed jobs.
        // -----------------------------------------------------------------
        static Patch_WorkGiver_Scanner_ToolGate()
        {
            try
            {
                var t = AccessTools.TypeByName("WorkGiver_BuildRoof") ?? AccessTools.TypeByName("WorkGiver_BuildRoofs");
                if (t == null) return;

                var harmony = new Harmony("com.jellypowered.survivaltools.buildroofgate");

                // Patch HasJobOnCell(Pawn, IntVec3, bool)
                var hasCell = AccessTools.Method(t, "HasJobOnCell", new Type[] { typeof(Pawn), typeof(IntVec3), typeof(bool) });
                if (hasCell != null)
                {
                    try
                    {
                        var prefix = new HarmonyMethod(typeof(Patch_WorkGiver_Scanner_ToolGate).GetMethod(nameof(Prefix_BuildRoof_HasJobOnCell), BindingFlags.NonPublic | BindingFlags.Static));
                        harmony.Patch(hasCell, prefix: prefix);
                    }
                    catch { }
                }

                // Patch JobOnCell(Pawn, IntVec3) if present to avoid creating a job at all
                var jobOnCell = AccessTools.Method(t, "JobOnCell", new Type[] { typeof(Pawn), typeof(IntVec3) });
                if (jobOnCell != null)
                {
                    try
                    {
                        var prefix2 = new HarmonyMethod(typeof(Patch_WorkGiver_Scanner_ToolGate).GetMethod(nameof(Prefix_BuildRoof_JobOnCell), BindingFlags.NonPublic | BindingFlags.Static));
                        harmony.Patch(jobOnCell, prefix: prefix2);
                    }
                    catch { }
                }
            }
            catch { /* best-effort */ }
        }

        // Prefix for concrete BuildRoof.HasJobOnCell to deny job assignment early in extra-hardcore.
        private static bool Prefix_BuildRoof_HasJobOnCell(object __instance, Pawn pawn, IntVec3 c, bool forced, ref bool __result)
        {
            try
            {
                if (pawn == null) return true; // let original handle null

                var settings = SurvivalTools.Settings;
                // Only enforce stricter gating in extra-hardcore mode
                if (settings == null || settings.extraHardcoreMode != true) return true;

                // Respect mechanoid/quest/other blacklists
                if (!PawnToolValidator.CanUseSurvivalTools(pawn)) return true;

                // Centralized helper: ShouldBlockBuildRoof returns whether we should block and a logKey
                try
                {
                    if (StatGatingHelper.ShouldBlockBuildRoof(pawn, out string lk, c))
                    {
                        __result = false;
                        try { if (ShouldLogWithCooldown(lk)) ST_Logging.LogToolGateEvent(pawn, null, StatDefOf.ConstructionSpeed, "missing construction tool"); } catch { }
                        return false;
                    }
                }
                catch { }
            }
            catch { }
            return true; // allow original
        }

        // Prefix for BuildRoof.JobOnCell to avoid creating the job when pawn lacks tool
        private static bool Prefix_BuildRoof_JobOnCell(object __instance, Pawn pawn, IntVec3 c, ref Job __result)
        {
            try
            {
                if (pawn == null) return true;
                var settings = SurvivalTools.Settings;
                if (settings == null || settings.extraHardcoreMode != true) return true;
                if (!PawnToolValidator.CanUseSurvivalTools(pawn)) return true;

                try
                {
                    if (StatGatingHelper.ShouldBlockBuildRoof(pawn, out string lk2, c))
                    {
                        __result = null;
                        try { if (ShouldLogWithCooldown(lk2)) ST_Logging.LogToolGateEvent(pawn, null, StatDefOf.ConstructionSpeed, "missing construction tool"); } catch { }
                        return false;
                    }
                }
                catch { }
            }
            catch { }
            return true;
        }
    }
}
