// RimWorld 1.6 / C# 7.3
// Source/Harmony/WorkGiver_Gates.cs
// Phase 5: Authoritative WorkGiver-level gating via Harmony patches.
// Centralizes gating logic, eliminates scattered job checks, and provides clear failure reasons.
// Refactor code, KEEP.
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using SurvivalTools.Gating;

namespace SurvivalTools.HarmonyStuff
{
    [StaticConstructorOnStartup]
    static class WorkGiver_Gates
    {
        static readonly Harmony H = new Harmony("jellypowered.survivaltools.gating");

        static WorkGiver_Gates()
        {
            // Postfix JobOnThing/JobOnCell (authoritative)
            TryPatchPostfix(typeof(WorkGiver_Scanner), "JobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) }, nameof(Post_JobOnThing));
            TryPatchPostfix(typeof(WorkGiver_Scanner), "JobOnCell", new[] { typeof(Pawn), typeof(IntVec3), typeof(bool) }, nameof(Post_JobOnCell));

            // Optional early-out Prefix on HasJob*
            TryPatchPrefix(typeof(WorkGiver_Scanner), "HasJobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) }, nameof(Pre_HasJobOnThing));
            TryPatchPrefix(typeof(WorkGiver_Scanner), "HasJobOnCell", new[] { typeof(Pawn), typeof(IntVec3), typeof(bool) }, nameof(Pre_HasJobOnCell));

            // Invalidate caches when resolver bumps/settings change
            Compat.CompatAPI.OnAfterDefsLoaded(() => JobGate.ClearCaches());
        }

        static void TryPatchPostfix(System.Type t, string name, System.Type[] sig, string postfix)
        {
            try
            {
                var m = AccessTools.Method(t, name, sig);
                if (m != null)
                    H.Patch(m, postfix: new HarmonyMethod(typeof(WorkGiver_Gates), postfix));
            }
            catch
            {
                // Guard: if signature differs, skip patch (Normal mode still works)
            }
        }

        static void TryPatchPrefix(System.Type t, string name, System.Type[] sig, string prefix)
        {
            try
            {
                var m = AccessTools.Method(t, name, sig);
                if (m != null)
                    H.Patch(m, prefix: new HarmonyMethod(typeof(WorkGiver_Gates), prefix));
            }
            catch
            {
                // Guard: if signature differs, skip patch (Normal mode still works)
            }
        }

        // Postfix: if we created a job but gating says block in Hardcore/Nightmare, null it and add a reason
        static void Post_JobOnThing(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            if (__result == null) return;
            var settings = SurvivalTools.Settings;
            if (settings == null || (!settings.hardcoreMode && !settings.extraHardcoreMode)) return;
            if (JobGate.ShouldBlock(pawn, __instance.def, __result.def, forced, out var key, out var a1, out var a2))
            {
                __result = null;
                JobFailReason.Is(key.Translate(a1, a2), null);
            }
        }

        static void Post_JobOnCell(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, bool forced, ref Job __result)
        {
            if (__result == null) return;
            var settings = SurvivalTools.Settings;
            if (settings == null || (!settings.hardcoreMode && !settings.extraHardcoreMode)) return;
            if (JobGate.ShouldBlock(pawn, __instance.def, __result.def, forced, out var key, out var a1, out var a2))
            {
                __result = null;
                JobFailReason.Is(key.Translate(a1, a2), null);
            }
        }

        // Prefix early-out for perf; we can only pass WG here (no JobDef yet)
        static bool Pre_HasJobOnThing(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            var settings = SurvivalTools.Settings;
            if (settings == null || (!settings.hardcoreMode && !settings.extraHardcoreMode)) return true;
            if (JobGate.ShouldBlock(pawn, __instance.def, null, forced, out var key, out var a1, out var a2))
            {
                __result = false;
                JobFailReason.Is(key.Translate(a1, a2), null);
                return false;
            }
            return true;
        }

        static bool Pre_HasJobOnCell(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, bool forced, ref bool __result)
        {
            var settings = SurvivalTools.Settings;
            if (settings == null || (!settings.hardcoreMode && !settings.extraHardcoreMode)) return true;
            if (JobGate.ShouldBlock(pawn, __instance.def, null, forced, out var key, out var a1, out var a2))
            {
                __result = false;
                JobFailReason.Is(key.Translate(a1, a2), null);
                return false;
            }
            return true;
        }
    }
}

// -------------------------------------------------------------------------
// Strangler Pattern Kill List (Phase 5):
// - Remove now: (no deletes yet; comments only)
// - Any reflection-based fallback gating (e.g., old Patch_Pawn_JobTracker_ExtraHardcore)
// - Duplicated "missing tool" checks in individual JobDrivers
// - Per-job blocking logic outside the WorkGiver_Scanner patches
//
// Rationale:
// - These patches provide authoritative blocking at the WorkGiver level
// - Eliminates need for reflection-based job interception
// - Centralized gating logic with clear failure reasons
// -------------------------------------------------------------------------