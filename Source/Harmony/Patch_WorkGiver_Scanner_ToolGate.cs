// RimWorld 1.6 / C# 7.3
// Source/Harmony/Patch_WorkGiver_Scanner_ToolGate.cs
//
// LEGACY SYSTEM: This file exists from the original codebase.
// It has been DISABLED and replaced by the refactored gating system.
// Kept for compatibility and to avoid breaking references.
//
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(WorkGiver_Scanner))]
    public static class Patch_WorkGiver_Scanner_ToolGate
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(WorkGiver_Scanner.HasJobOnThing))]
        public static bool Prefix_HasJobOnThing(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            // LEGACY STUB: Always allow - new system handles gating
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(WorkGiver_Scanner.HasJobOnCell))]
        public static bool Prefix_HasJobOnCell(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, bool forced, ref bool __result)
        {
            // LEGACY STUB: Always allow - new system handles gating
            return true;
        }
    }
}
