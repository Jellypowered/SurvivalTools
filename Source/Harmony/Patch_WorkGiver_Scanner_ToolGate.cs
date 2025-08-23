using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using SurvivalTools;
using System.Collections.Generic;

namespace SurvivalTools.HarmonyStuff
{
    // Gate job assignment on tool availability in Hardcore
    [HarmonyPatch(typeof(WorkGiver_Scanner))]
    public static class Patch_WorkGiverScanner_ToolGate
    {
        // HasJobOnThing
        [HarmonyPrefix]
        [HarmonyPatch(nameof(WorkGiver_Scanner.HasJobOnThing))]
        public static bool Prefix_HasJobOnThing(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            if (!ShouldGate(__instance, pawn, out var requiredStats, out var jobDef))
                return true;

            if (HasRequiredToolOrCanAcquire(pawn, __instance.def, requiredStats))
                return true;

            // Block job
            __result = false;
            DLog(pawn, __instance.def, jobDef, "Gate: missing required tool and no acquirable tool (thing scan).");
            return false;
        }

        // HasJobOnCell
        [HarmonyPrefix]
        [HarmonyPatch(nameof(WorkGiver_Scanner.HasJobOnCell))]
        public static bool Prefix_HasJobOnCell(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, bool forced, ref bool __result)
        {
            if (!ShouldGate(__instance, pawn, out var requiredStats, out var jobDef))
                return true;

            if (HasRequiredToolOrCanAcquire(pawn, __instance.def, requiredStats))
                return true;

            __result = false;
            DLog(pawn, __instance.def, jobDef, "Gate: missing required tool and no acquirable tool (cell scan).");
            return false;
        }

        // ---------------- helpers ----------------

        private static bool ShouldGate(WorkGiver_Scanner scanner, Pawn pawn, out List<StatDef> requiredStats, out JobDef jobDef)
        {
            requiredStats = null;
            jobDef = pawn?.jobs?.curJob?.def;

            // Hardcore only; also make sure pawn can even use survival tools
            var s = SurvivalTools.Settings;
            if (s == null || !s.hardcoreMode) return false;
            if (pawn == null || !pawn.CanUseSurvivalTools()) return false;

            var wg = scanner?.def;
            if (wg == null) return false;

            // Prefer WG extension, else fall back to job-based mapping.
            requiredStats = SurvivalToolUtility.RelevantStatsFor(wg, jobDef);
            if (requiredStats == null || requiredStats.Count == 0)
                return false; // nothing tool-gated here → let vanilla proceed

            // If pawn fails vanilla stat gates, let vanilla handle it (don’t double-filter here)
            if (!pawn.MeetsWorkGiverStatRequirements(requiredStats))
                return false;

            return true;
        }

        private static bool HasRequiredToolOrCanAcquire(Pawn pawn, WorkGiverDef wg, List<StatDef> requiredStats)
        {
            // Already has a helpful tool?
            for (int i = 0; i < requiredStats.Count; i++)
                if (pawn.HasSurvivalToolFor(requiredStats[i]))
                    return true;

            // If autoTool is enabled, allow the job if a helpful tool is reachable now
            var s = SurvivalTools.Settings;
            if (s != null && s.autoTool)
            {
                var best = FindBestWrapper(pawn, wg, requiredStats);
                if (best != null) return true;
            }

            return false;
        }

        // Small indirection so we can call your existing finder without making it public.
        private static SurvivalTool FindBestWrapper(Pawn pawn, WorkGiverDef wg, List<StatDef> stats)
        {
            return Patch_JobGiver_Work_TryIssueJobPackage_AutoTool
                   .Patch_JobGiver_Work_TryIssueJobPackage_AutoTool_FindBest
                   .FindBestHelpfulTool(pawn, wg, stats);
        }

        private static void DLog(Pawn pawn, WorkGiverDef wg, JobDef jobDef, string msg)
        {
            if (SurvivalTools.Settings != null && SurvivalTools.Settings.debugLogging)
                Log.Message($"[SurvivalTools.ToolGate] {pawn?.LabelShort ?? "null"} | WGD='{wg?.defName ?? "null"}' | Job='{jobDef?.defName ?? "null"}' | {msg}");
        }
    }
}
