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
    static class WorkGiver_Gates
    {
        internal static void Init(HarmonyLib.Harmony h)
        {
            if (h == null) return;
            // Prefix HasJobOnThing/HasJobOnCell to prevent vanilla from considering impossible work
            TryPatchPrefix(h, typeof(WorkGiver_Scanner), "HasJobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) }, nameof(Pre_HasJobOnThing));
            TryPatchPrefix(h, typeof(WorkGiver_Scanner), "HasJobOnCell", new[] { typeof(Pawn), typeof(IntVec3), typeof(bool) }, nameof(Pre_HasJobOnCell));

            // Postfix JobOnThing/JobOnCell (authoritative, fallback for complex cases)
            TryPatchPostfix(h, typeof(WorkGiver_Scanner), "JobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) }, nameof(Post_JobOnThing));
            TryPatchPostfix(h, typeof(WorkGiver_Scanner), "JobOnCell", new[] { typeof(Pawn), typeof(IntVec3), typeof(bool) }, nameof(Post_JobOnCell));

            // Invalidate caches when resolver bumps/settings change
            Compat.CompatAPI.OnAfterDefsLoaded(() => JobGate.ClearCaches());
        }

        static void TryPatchPrefix(HarmonyLib.Harmony h, System.Type t, string name, System.Type[] sig, string prefix)
        {
            try
            {
                var m = AccessTools.Method(t, name, sig);
                if (m != null)
                    h.Patch(m, prefix: new HarmonyMethod(typeof(WorkGiver_Gates), prefix));
            }
            catch
            {
                // Guard: if signature differs, skip patch (Normal mode still works)
            }
        }

        static void TryPatchPostfix(HarmonyLib.Harmony h, System.Type t, string name, System.Type[] sig, string postfix)
        {
            try
            {
                var m = AccessTools.Method(t, name, sig);
                if (m != null)
                    h.Patch(m, postfix: new HarmonyMethod(typeof(WorkGiver_Gates), postfix));
            }
            catch
            {
                // Guard: if signature differs, skip patch (Normal mode still works)
            }
        }

        // Prefix: Early-out to prevent vanilla from considering work when pawn lacks required tools.
        // This solves the "frozen pawn" issue by making vanilla think there's no work at this location.
        static bool Pre_HasJobOnThing(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced)
        {
            var settings = SurvivalToolsMod.Settings;
            if (settings == null || (!settings.hardcoreMode && !settings.extraHardcoreMode))
                return true; // Normal mode: allow vanilla to proceed

            // Quick check: would this WorkGiver be blocked for this pawn?
            // We use null for the Job since we don't have it yet, but we can check the WorkGiver's stats
            if (JobGate.ShouldBlock(pawn, __instance.def, (JobDef)null, forced, out var key, out var a1, out var a2))
            {
                // Block: tell vanilla there's no work here
                return false;
            }

            return true; // Allow vanilla to check for work
        }

        static bool Pre_HasJobOnCell(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, bool forced)
        {
            var settings = SurvivalToolsMod.Settings;
            if (settings == null || (!settings.hardcoreMode && !settings.extraHardcoreMode))
                return true; // Normal mode: allow vanilla to proceed

            // Quick check: would this WorkGiver be blocked for this pawn?
            if (JobGate.ShouldBlock(pawn, __instance.def, (JobDef)null, forced, out var key, out var a1, out var a2))
            {
                // Block: tell vanilla there's no work here
                return false;
            }

            return true; // Allow vanilla to check for work
        }

        // Postfix: if we created a job but gating says block in Hardcore/Nightmare, null it and add a reason
        static void Post_JobOnThing(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            if (__result == null) return;
            var settings = SurvivalToolsMod.Settings;
            if (settings == null || (!settings.hardcoreMode && !settings.extraHardcoreMode)) return;
            // Pass Job instance (not just JobDef) for target-aware stat resolution
            if (JobGate.ShouldBlock(pawn, __instance.def, __result, forced, out var key, out var a1, out var a2))
            {
                __result = null;
                JobFailReason.Is(key.Translate(a1, a2), null);
            }
        }

        static void Post_JobOnCell(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, bool forced, ref Job __result)
        {
            if (__result == null) return;
            var settings = SurvivalToolsMod.Settings;
            if (settings == null || (!settings.hardcoreMode && !settings.extraHardcoreMode)) return;
            // Pass Job instance (not just JobDef) for target-aware stat resolution
            if (JobGate.ShouldBlock(pawn, __instance.def, __result, forced, out var key, out var a1, out var a2))
            {
                __result = null;
                JobFailReason.Is(key.Translate(a1, a2), null);
            }
        }

        // (Postfix still needed as fallback for complex cases where job specifics matter)
    }
}

// -------------------------------------------------------------------------
// Phase 11: Combined Prefix + Postfix Gating Strategy
// 
// PROBLEM: Pawns freeze when they lack tools because vanilla work giver keeps 
// assigning impossible jobs (HasJobOnThing returns true, but JobOnThing is nulled).
//
// SOLUTION: Two-layer gating system:
// 1. PREFIX on HasJobOnThing/Cell: Early-out to tell vanilla "no work exists here"
//    when pawn lacks required tools for the WorkGiver. This prevents job assignment
//    loops and frozen pawns.
//
// 2. POSTFIX on JobOnThing/Cell: Fallback for complex cases where the Job's specific
//    target matters (e.g., tree vs plant). Nulls jobs that pass the WorkGiver check
//    but should still be blocked based on job-specific context.
//
// This approach:
// - Maintains compatibility (doesn't override vanilla work giver flow)
// - Prevents frozen pawns (vanilla sees "no work" instead of "failed job")
// - Preserves rescue system (tool acquisition is still queued)
// - Handles edge cases (job-level postfix provides final authority)
// -------------------------------------------------------------------------