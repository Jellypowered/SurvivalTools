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
using SurvivalTools.HarmonyStuff;

namespace SurvivalTools.HarmonyStuff
{
    [StaticConstructorOnStartup]
    static class ST_PatchGuard
    {
        private static readonly HarmonyLib.Harmony _harmony = new HarmonyLib.Harmony("survivaltools.patchguard");
        // Central allowlist of ST patch container types permitted to remain on hotspots.
        private static readonly Type[] _allowlistedTypes = new Type[]
        {
            typeof(PreWork_AutoEquip),
            typeof(ITab_Gear_ST),
            typeof(SurvivalTools.Assign.PostAddHooks),
            typeof(SurvivalTools.Assign.PostEquipHooks_AddEquipment),
            typeof(SurvivalTools.Assign.PostEquipHooks_AddEquipment.PostEquipHooks_NotifyAdded),
            typeof(SurvivalTools.UI.RightClickRescue.FloatMenu_PrioritizeWithRescue),
            typeof(SurvivalTools.UI.RightClickRescue.Patch_FloatMenuMakerMap_GetOptions)
        };

        static ST_PatchGuard()
        {
#if DEBUG
            Log.Message("[SurvivalTools.PatchGuard] Starting patch cleanup...");
#endif

            // Job start / ordered job hotspots (nullable + fallback signatures)
            SweepWithFallback<Pawn_JobTracker>("TryTakeOrderedJob",
                new[] { typeof(Job), typeof(JobTag?), typeof(bool) },
                new[] { typeof(Job), typeof(bool) },
                allowedTypes: _allowlistedTypes);

            SweepWithFallback<Pawn_JobTracker>("TryStartJob",
                new[] { typeof(Job), typeof(JobTag?), typeof(bool) },
                new[] { typeof(Job), typeof(bool) },
                allowedTypes: _allowlistedTypes);

            // RimWorld 1.6 uses StartJob (long signature) – add explicit sweep (non-fatal if absent)
            var startJobExactSig = new Type[]
            {
                typeof(Job),            // newJob
                typeof(JobCondition),    // lastJobEndCondition
                typeof(ThinkNode),       // jobGiver
                typeof(bool),            // resumeCurJobAfterwards
                typeof(bool),            // cancelBusyStances
                typeof(ThinkTreeDef),    // thinkTree
                typeof(JobTag?),         // tag
                typeof(bool),            // fromQueue
                typeof(bool),            // canReturnCurJobToPool
                typeof(bool?),           // keepCarryingThingOverride
                typeof(bool),            // continueSleeping
                typeof(bool),            // addToJobsThisTick
                typeof(bool)             // preToilReservationsCanFail
            };
            Sweep<Pawn_JobTracker>("StartJob", startJobExactSig, allowedTypes: _allowlistedTypes);

            // Gear tab primary patch point (FillTab) – only allow ITab_Gear_ST
            Sweep<ITab_Pawn_Gear>("FillTab", Type.EmptyTypes,
                allowedTypes: _allowlistedTypes);

            // DrawThingRow transpiler(s) should all be removed (legacy clutter)
            Sweep<ITab_Pawn_Gear>("DrawThingRow", new[] { typeof(Rect), typeof(Thing), typeof(bool) },
                allowedTypes: Array.Empty<Type>());

            // Work giver scanning – remove any lingering legacy auto-tool patches
            Sweep<JobGiver_Work>("TryIssueJobPackage", new[] { typeof(Pawn), typeof(JobIssueParams) },
                allowedTypes: Array.Empty<Type>());

            // Additional defensive sweeps (Phase 9): ensure no survivaltools legacy patches remain on generic hotspots
            // These are broader & only act on ST-owned patches not on the allowlist.
            TrySweepOptional<Pawn_EquipmentTracker>("TryDropEquipment", new[] { typeof(ThingWithComps), typeof(ThingWithComps).MakeByRefType(), typeof(IntVec3), typeof(bool) });
            TrySweepOptional<Thing>("Destroy", new[] { typeof(DestroyMode) });
            TrySweepOptionalStatic(typeof(ThingMaker), "MakeThing", new[] { typeof(ThingDef), typeof(ThingDef) });

            // Optional legacy HasJobOnThing/Cell gating sweep (owners / declaring types listed by user request)
            LegacyHasJobSweep();

            // Additional namespace-prefix based optional sweep for any lingering ST-owned HasJobOnThing patches
            OptionalNamespacePrefixHasJobOnThingSweep();

#if DEBUG
            Log.Message("[SurvivalTools.PatchGuard] Patch cleanup complete.");
            LogAllowlistSummary();
#endif
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
#if DEBUG
            if (Prefs.DevMode)
                Log.Message($"[SurvivalTools.PatchGuard] (Dev) Primary signature not found for {typeof(TDecl).Name}.{name}, trying fallback");
#endif
            Sweep<TDecl>(name, fallbackSig, allowedTypes);
        }

        static void Sweep<TDecl>(string name, Type[] sig, Type[] allowedTypes)
        {
            var orig = AccessTools.Method(typeof(TDecl), name, sig);
            if (orig == null)
            {
#if DEBUG
                if (Prefs.DevMode)
                    Log.Message($"[SurvivalTools.PatchGuard] (Dev) Method {typeof(TDecl).Name}.{name} not present (expected on this version?)");
#endif
                return;
            }

            var info = Harmony.GetPatchInfo(orig);
            if (info == null)
            {
#if DEBUG
                Log.Message($"[SurvivalTools.PatchGuard] No patches found on {typeof(TDecl).Name}.{name}");
#endif
                return;
            }

#if DEBUG
            Log.Message($"[SurvivalTools.PatchGuard] Checking {typeof(TDecl).Name}.{name} - {info.Prefixes.Count} prefixes, {info.Postfixes.Count} postfixes");
#endif

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

#if DEBUG
            if (removedCount > 0)
            {
                Log.Message($"[SurvivalTools.PatchGuard] Removed {removedCount} legacy patches from {typeof(TDecl).Name}.{name}");
            }
            else
            {
                Log.Message($"[SurvivalTools.PatchGuard] No legacy patches to remove from {typeof(TDecl).Name}.{name}");
            }
#endif
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
#if DEBUG
                Log.Message($"[SurvivalTools.PatchGuard] Removing legacy patch: {dt.FullName}.{m.Name} (owner: {patch.owner})");
#endif
                // Unpatch by method (works regardless of harmonyId)
                _harmony.Unpatch(original: original, patch: m);
                return true;
            }
            else if (ours && allowed)
            {
#if DEBUG
                Log.Message($"[SurvivalTools.PatchGuard] Keeping allowed patch: {dt.FullName}.{m.Name} (owner: {patch.owner})");
#endif
            }

            return false;
        }

        // Optional sweep that silently returns if method not found; used for broad defensive cleanup.
        static void TrySweepOptional<TDecl>(string name, Type[] sig)
        {
            var orig = AccessTools.Method(typeof(TDecl), name, sig);
            if (orig == null) return; // signature absent in this RimWorld build
            var info = Harmony.GetPatchInfo(orig);
            if (info == null) return;
            int removed = 0;
            foreach (var p in info.Prefixes) if (TryUnpatchIfLegacy(orig, p, _allowlistedTypes)) removed++;
            foreach (var p in info.Postfixes) if (TryUnpatchIfLegacy(orig, p, _allowlistedTypes)) removed++;
            foreach (var p in info.Transpilers) if (TryUnpatchIfLegacy(orig, p, _allowlistedTypes)) removed++;
#if DEBUG
            if (removed > 0)
                Log.Message($"[SurvivalTools.PatchGuard] Optional sweep removed {removed} legacy patches from {typeof(TDecl).Name}.{name}");
#endif
        }

        static void LogAllowlistSummary()
        {
            try
            {
                string allowed = string.Join(", ", _allowlistedTypes.Select(t => t.Name));
                Log.Message($"[SurvivalTools.PatchGuard] Allowlist enforced. Allowed patch containers: {allowed}");
            }
            catch { }
        }

        static void TrySweepOptionalStatic(Type declType, string name, Type[] sig)
        {
            if (declType == null) return;
            var orig = AccessTools.Method(declType, name, sig);
            if (orig == null) return;
            var info = Harmony.GetPatchInfo(orig);
            if (info == null) return;
            int removed = 0;
            foreach (var p in info.Prefixes) if (TryUnpatchIfLegacy(orig, p, _allowlistedTypes)) removed++;
            foreach (var p in info.Postfixes) if (TryUnpatchIfLegacy(orig, p, _allowlistedTypes)) removed++;
            foreach (var p in info.Transpilers) if (TryUnpatchIfLegacy(orig, p, _allowlistedTypes)) removed++;
#if DEBUG
            if (removed > 0)
                Log.Message($"[SurvivalTools.PatchGuard] Optional sweep removed {removed} legacy patches from {declType.Name}.{name}");
#endif
        }

        // Sweep for legacy HasJobOnThing / HasJobOnCell prefixes owned by prior gating systems.
        static void LegacyHasJobSweep()
        {
            try
            {
                var targets = new (Type type, string method, Type[] sig)[]
                {
                    (typeof(WorkGiver_Scanner), "HasJobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) }),
                    (typeof(WorkGiver_Scanner), "HasJobOnCell", new[] { typeof(Pawn), typeof(IntVec3), typeof(bool) })
                };

                string[] owners =
                {
                    "jellypowered.survivaltools.gating",
                    "SurvivalTools.Compat.ResearchReinventedGating"
                };
                string[] declaringTypeNames =
                {
                    "SurvivalTools.HarmonyStuff.WorkGiver_Gates",
                    "SurvivalTools.Compat.ResearchReinvented.RRPatches"
                };

                int totalRemoved = 0;
                foreach (var (type, method, sig) in targets)
                {
                    var orig = AccessTools.Method(type, method, sig);
                    if (orig == null) continue;
                    var info = Harmony.GetPatchInfo(orig);
                    if (info == null) continue;
                    foreach (var p in info.Prefixes.ToList())
                    {
                        try
                        {
                            var pm = p.PatchMethod;
                            var dt = pm?.DeclaringType;
                            if (pm == null || dt == null) continue;
                            bool ownerMatch = owners.Contains(p.owner);
                            bool declMatch = declaringTypeNames.Contains(dt.FullName);
                            if (ownerMatch || declMatch)
                            {
                                _harmony.Unpatch(orig, pm);
                                totalRemoved++;
                            }
                        }
                        catch { }
                    }
                }
#if DEBUG
                if (totalRemoved > 0)
                    Log.Message($"[SurvivalTools.PatchGuard] Legacy HasJob* sweep removed {totalRemoved} obsolete gating prefix(es).");
#endif
            }
            catch (Exception e)
            {
                Log.Warning("[SurvivalTools.PatchGuard] LegacyHasJobSweep error: " + e.Message);
            }
        }

        // New (Phase 10 refinement): broad optional sweep removing ST-owned HasJobOnThing patches whose owners / declaring types
        // start with specific namespace prefixes, excluding allowlisted patch containers.
        static void OptionalNamespacePrefixHasJobOnThingSweep()
        {
            try
            {
                var method = AccessTools.Method(typeof(WorkGiver_Scanner), "HasJobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
                if (method == null) return; // signature may differ in future versions
                var info = Harmony.GetPatchInfo(method);
                if (info == null) return;

                string[] nsPrefixes = { "Jelly.SurvivalTools", "SurvivalTools.HarmonyStuff" };
                var removedTypes = new HashSet<Type>();

                bool IsAllowlisted(Type t) => t != null && Array.IndexOf(_allowlistedTypes, t) >= 0;

                void Consider(Patch p)
                {
                    if (p == null) return;
                    var pm = p.PatchMethod; if (pm == null) return;
                    var dt = pm.DeclaringType; if (dt == null) return;
                    // Skip allowlist
                    if (IsAllowlisted(dt)) return;
                    string owner = p.owner ?? string.Empty;
                    string full = dt.FullName ?? string.Empty;
                    bool ownerMatch = nsPrefixes.Any(pref => owner.StartsWith(pref, StringComparison.Ordinal));
                    bool declMatch = nsPrefixes.Any(pref => full.StartsWith(pref, StringComparison.Ordinal));
                    if (ownerMatch || declMatch)
                    {
                        try
                        {
                            _harmony.Unpatch(method, pm);
                            removedTypes.Add(dt);
                        }
                        catch { /* ignore individual failures */ }
                    }
                }

                foreach (var p in info.Prefixes) Consider(p);
                foreach (var p in info.Postfixes) Consider(p);
                foreach (var p in info.Transpilers) Consider(p);

#if DEBUG
                if (removedTypes.Count > 0)
                {
                    try
                    {
                        var names = string.Join(", ", removedTypes.Select(t => t.FullName));
                        Log.Message($"[SurvivalTools.PatchGuard] Namespace sweep removed HasJobOnThing patches from: {names}");
                    }
                    catch { Log.Message("[SurvivalTools.PatchGuard] Namespace sweep removed HasJobOnThing patches (types list unavailable)"); }
                }
#endif
            }
            catch (Exception e)
            {
                Log.Warning("[SurvivalTools.PatchGuard] OptionalNamespacePrefixHasJobOnThingSweep error: " + e.Message);
            }
        }
    }
}