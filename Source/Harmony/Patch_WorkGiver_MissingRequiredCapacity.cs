// RimWorld 1.6 / C# 7.3
// Patch_WorkGiver_MissingRequiredCapacity.cs
using HarmonyLib;
using RimWorld;
using Verse;

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

            // Nothing to gate on.
            if (required == null || required.Count == 0) return;

            // Use the overload with WorkGiverDef context (helps consistency + any logging).
            if (!pawn.MeetsWorkGiverStatRequirements(required, wgDef))
            {
                __result = PawnCapacityDefOf.Manipulation;

                // Cooldowned, low-noise debug (optional).
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                {
                    var key = $"ST_MissingCap_{pawn.ThingID}_{wgDef.defName}";
                    if (SurvivalToolUtility.ShouldLogWithCooldown(key))
                        Log.Message($"[SurvivalTools] Blocking {wgDef.defName} for {pawn.LabelShort}: missing required tool/stat → Manipulation capacity gate.");
                }
            }
        }
    }
}
