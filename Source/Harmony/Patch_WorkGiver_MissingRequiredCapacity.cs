// RimWorld 1.6 / C# 7.3
// Source/Harmony/Patch_WorkGiver_MissingRequiredCapacity.cs
// Legacy patch to gate work givers based on required tool stats.
// Replaced by integrated tool stat checks in WorkGiver_Scanner.CanWorkOnThing.
// Retained for compatibility with mods that patch or replace CanWorkOnThing.
// KEEP for now, may be useful for debugging or future features.
// Note: This patch runs after vanilla checks, so it only adds additional gating.
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(WorkGiver))]
    [HarmonyPatch(nameof(WorkGiver.MissingRequiredCapacity))]
    public static class Patch_WorkGiver_MissingRequiredCapacity
    {
        public static void Postfix(WorkGiver __instance, ref PawnCapacityDef __result, Pawn pawn)
        {
            // If vanilla already blocked, or no pawn/def, leave it alone.
            if (__result != null || pawn == null) return;

            var wgDef = __instance?.def;
            var ext = wgDef?.GetModExtension<WorkGiverExtension>();
            var required = ext?.requiredStats;

            // Only gate jobs that are eligible by default
            if (!SurvivalToolUtility.ShouldGateByDefault(wgDef)) return;

            // Nothing to gate on.
            if (required == null || required.Count == 0) return;

            // Use the overload with WorkGiverDef context (helps consistency + any logging).
            if (!pawn.MeetsWorkGiverStatRequirements(required, wgDef))
            {
                __result = PawnCapacityDefOf.Manipulation;

                if (IsDebugLoggingEnabled)
                {
                    var key = $"MissingToolCapacity_{pawn.ThingID}_{wgDef.defName}";
                    if (ShouldLogWithCooldown(key))
                    {
                        var missingStats = string.Join(", ",
                            required.Where(stat => stat != null)
                                    .Where(stat => pawn.GetBestSurvivalTool(stat) == null)
                                    .Select(stat => stat.defName));

                        LogDecision(key, $"[SurvivalTools] Blocking {wgDef.defName} for {pawn.LabelShort}: " +
                                    $"missing required tool/stat(s) [{missingStats}] → Manipulation capacity gate.");
                    }
                }
            }
        }
    }
}
