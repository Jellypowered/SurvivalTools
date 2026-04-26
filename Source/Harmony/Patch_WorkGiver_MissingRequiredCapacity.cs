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
using SurvivalTools.Helpers;
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
                        bool HasImprovingToolFor(StatDef checkStat)
                        {
                            if (checkStat == null || !checkStat.RequiresSurvivalTool()) return false;
                            Scoring.ToolScoring.GetBestTool(pawn, checkStat, out float score);
                            return score > 0.001f;
                        }

                        bool HasToolForRequiredStatOrEquivalent(StatDef requiredStat)
                        {
                            if (requiredStat == null) return true;
                            if (HasImprovingToolFor(requiredStat)) return true;

                            // Keep butchery-family equivalence consistent with MeetsWorkGiverStatRequirements.
                            if (requiredStat == ST_StatDefOf.ButcheryFleshSpeed || requiredStat == ST_StatDefOf.ButcheryFleshEfficiency)
                            {
                                if (HasImprovingToolFor(ST_StatDefOf.ButcheryFleshSpeed)) return true;
                                if (HasImprovingToolFor(ST_StatDefOf.ButcheryFleshEfficiency)) return true;
                            }

                            return false;
                        }

                        var settings = SurvivalToolsMod.Settings;
                        var missingStats = string.Join(", ",
                            required.Where(stat => stat != null)
                                    .Where(stat => stat.RequiresSurvivalTool())
                                    .Where(stat => settings != null && StatGatingHelper.ShouldBlockJobForStat(stat, settings, pawn))
                                    .Where(stat => !HasToolForRequiredStatOrEquivalent(stat))
                                    .Select(stat => stat.defName));

                        LogDecision(key, $"[SurvivalTools] Blocking {wgDef.defName} for {pawn.LabelShort}: " +
                                    $"missing required tool/stat(s) [{missingStats}] → Manipulation capacity gate.");
                    }
                }
            }
        }
    }
}
