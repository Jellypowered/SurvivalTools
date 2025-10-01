// RimWorld 1.6 / C# 7.3
// Source/UI/RightClickRescue/Patch_FloatMenuMakerMap_GetOptions.cs
// Phase 11.5 LEGACY: Simple fallback postfix for provider system failures.
//
// MODERN SYSTEM:
//   - Provider_STPrioritizeWithRescue (primary path via RimWorld 1.6 provider system)
//   - FloatMenu_PrioritizeWithRescue (comprehensive postfix with mod-tagging, STC, RR, dedup)
//
// This legacy fallback is redundant because:
//   1. Provider system is now stable (no longer experimental)
//   2. Modern postfix provides complete fallback coverage
//   3. Modern postfix has all features: mod-tagging, dedup logic, STC integration, RR support
//
// Phase 11.5 guards added - set STRIP_11_5_OLD_FLOATMENU=true to disable.

using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SurvivalTools.UI.RightClickRescue
{
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    public static class Patch_FloatMenuMakerMap_GetOptions
    {
        static void Prefix(List<Pawn> selectedPawns, Vector3 clickPos, ref FloatMenuContext context)
        {
            // Phase 11.9: Dead code removed. Modern system: Provider_STPrioritizeWithRescue + FloatMenu_PrioritizeWithRescue.
            // No-op shim kept for Harmony patch stability.
        }

        static void Postfix(List<Pawn> selectedPawns, Vector3 clickPos, ref FloatMenuContext context, ref List<FloatMenuOption> __result)
        {
            // Phase 11.9: Dead code removed. Modern system: FloatMenu_PrioritizeWithRescue (comprehensive postfix).
            // No-op shim kept for Harmony patch stability.
        }
    }
}
