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
            // Postfix JobOnThing/JobOnCell (authoritative)
            TryPatchPostfix(h, typeof(WorkGiver_Scanner), "JobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) }, nameof(Post_JobOnThing));
            TryPatchPostfix(h, typeof(WorkGiver_Scanner), "JobOnCell", new[] { typeof(Pawn), typeof(IntVec3), typeof(bool) }, nameof(Post_JobOnCell));

            // Legacy early-out prefixes retired (job-level gating via JobOn* + JobGate only)
            // (Intentionally not patching HasJobOnThing / HasJobOnCell anymore.)

            // Invalidate caches when resolver bumps/settings change
            Compat.CompatAPI.OnAfterDefsLoaded(() => JobGate.ClearCaches());
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

        // (Removed legacy Pre_HasJobOnThing / Pre_HasJobOnCell prefix implementations)
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