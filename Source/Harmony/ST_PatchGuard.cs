using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
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
            Sweep<Pawn_JobTracker>("TryTakeOrderedJob", new[] { typeof(Job), typeof(JobTag?), typeof(bool) },
                allowedTypes: new[] { typeof(PreWork_AutoEquip) });

            // Phase 7: Gear tab integration (replaces legacy gear UI patches)
            Sweep<ITab_Pawn_Gear>("FillTab", Type.EmptyTypes,
                allowedTypes: new[] { typeof(ITab_Gear_ST) });

            // TODO: Add other collision points as they arise during refactor
            // Example pattern:
            // Sweep<SomeClass>("SomeMethod", new[] { typeof(SomeParam) },
            //     allowedTypes: new[] { typeof(NewPhaseImplementation) });

            Log.Message("[SurvivalTools.PatchGuard] Patch cleanup complete.");
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

            // Heuristic: any patch from our mod namespace but not in the allowlist = legacy
            bool ours = dt.Namespace != null && dt.Namespace.StartsWith("SurvivalTools", StringComparison.Ordinal);
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