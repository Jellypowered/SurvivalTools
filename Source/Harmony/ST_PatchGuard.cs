// RimWorld 1.6 / C# 7.3
// Source/Harmony/ST_PatchGuard.cs
// Phase 6-7: Patch guard to clean up legacy patches from older SurvivalTools versions.
// Removes old patches that are no longer needed or have been replaced by new systems.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;
using SurvivalTools.Assign;

namespace SurvivalTools.HarmonyStuff
{
    [StaticConstructorOnStartup]
    static class ST_PatchGuard
    {
        private static readonly HarmonyLib.Harmony _harmony = new HarmonyLib.Harmony("survivaltools.patchguard");

        static ST_PatchGuard()
        {
            Log.Message("[SurvivalTools.PatchGuard] Starting patch cleanup...");

            // Phase 6: Job assignment system (replaces legacy auto-equip patches)
            // Signature discovered: TryTakeOrderedJob(Job job, JobTag? tag, bool fromQueue)
            SweepWithFallback<Pawn_JobTracker>("TryTakeOrderedJob",
                new[] { typeof(Job), typeof(JobTag?), typeof(bool) },
                new[] { typeof(Job), typeof(bool) }, // Fallback for non-nullable
                allowedTypes: new[] { typeof(PreWork_AutoEquip) });

            // Phase 6: Job start system (add TryStartJob coverage)
            SweepWithFallback<Pawn_JobTracker>("TryStartJob",
                new[] { typeof(Job), typeof(JobTag?), typeof(bool) },
                new[] { typeof(Job), typeof(bool) }, // Fallback for non-nullable
                allowedTypes: new[] { typeof(PreWork_AutoEquip) });

            // Phase 7: Gear tab integration (replaces legacy gear UI patches)  
            Sweep<ITab_Pawn_Gear>("FillTab", Type.EmptyTypes,
                allowedTypes: new[] { typeof(ITab_Gear_ST) });

            // Phase 7: Clean up legacy gear tab DrawThingRow transpilers
            Sweep<ITab_Pawn_Gear>("DrawThingRow", new[] { typeof(Rect), typeof(Thing), typeof(bool) },
                allowedTypes: new Type[0]); // No allowed types - remove all legacy transpilers

            // Phase 5-6: Clean up legacy JobGiver_Work auto-tool patches
            Sweep<JobGiver_Work>("TryIssueJobPackage", new[] { typeof(Pawn), typeof(JobIssueParams) },
                allowedTypes: new Type[0]); // No allowed types - remove all legacy patches

            Log.Message("[SurvivalTools.PatchGuard] Patch cleanup complete.");
        }

        static void SweepWithFallback<TDecl>(string name, Type[] primarySig, Type[] fallbackSig, Type[] allowedTypes)
        {
            // Try primary signature first
            var orig = AccessTools.Method(typeof(TDecl), name, primarySig);
            if (orig != null)
            {
                Sweep<TDecl>(name, primarySig, allowedTypes);
                return;
            }

            // Fall back to alternate signature
            Log.Message($"[SurvivalTools.PatchGuard] Primary signature not found for {typeof(TDecl).Name}.{name}, trying fallback");
            Sweep<TDecl>(name, fallbackSig, allowedTypes);
        }

        static void Sweep<TDecl>(string name, Type[] sig, Type[] allowedTypes)
        {
            var orig = AccessTools.Method(typeof(TDecl), name, sig);
            if (orig == null)
            {
                Log.Warning($"[SurvivalTools.PatchGuard] Method {typeof(TDecl).Name}.{name} not found");
                return;
            }

            var info = Harmony.GetPatchInfo(orig);
            if (info == null)
            {
                Log.Message($"[SurvivalTools.PatchGuard] No patches found on {typeof(TDecl).Name}.{name}");
                return;
            }

            Log.Message($"[SurvivalTools.PatchGuard] Checking {typeof(TDecl).Name}.{name} - {info.Prefixes.Count} prefixes, {info.Postfixes.Count} postfixes");

            int removedCount = 0;

            // Remove any ST-owned prefixes/postfixes not in the allowlist
            foreach (var p in info.Prefixes)
            {
                if (TryUnpatchIfLegacy(orig, p, allowedTypes))
                    removedCount++;
            }
            foreach (var p in info.Postfixes)
            {
                if (TryUnpatchIfLegacy(orig, p, allowedTypes))
                    removedCount++;
            }

            if (removedCount > 0)
            {
                Log.Message($"[SurvivalTools.PatchGuard] Removed {removedCount} legacy patches from {typeof(TDecl).Name}.{name}");
            }
            else
            {
                Log.Message($"[SurvivalTools.PatchGuard] No legacy patches to remove from {typeof(TDecl).Name}.{name}");
            }
        }

        static bool TryUnpatchIfLegacy(MethodBase original, Patch patch, Type[] allowedTypes)
        {
            var m = patch.PatchMethod;
            if (m == null) return false;
            var dt = m.DeclaringType;
            if (dt == null) return false;

            // BULLETPROOF: Assembly validation + namespace check  
            var ourAssembly = typeof(ST_PatchGuard).Assembly;
            bool ours = (dt.Assembly == ourAssembly) &&
                       (dt.Namespace != null && dt.Namespace.StartsWith("SurvivalTools", StringComparison.Ordinal));
            bool allowed = Array.IndexOf(allowedTypes, dt) >= 0;

            if (ours && !allowed)
            {
                Log.Message($"[SurvivalTools.PatchGuard] Removing legacy patch: {dt.FullName}.{m.Name} (owner: {patch.owner})");
                // Unpatch by method (works regardless of harmonyId)
                _harmony.Unpatch(original: original, patch: m);
                return true;
            }
            else if (ours && allowed)
            {
                Log.Message($"[SurvivalTools.PatchGuard] Keeping allowed patch: {dt.FullName}.{m.Name} (owner: {patch.owner})");
            }

            return false;
        }
    }
}