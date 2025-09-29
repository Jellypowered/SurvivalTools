// RimWorld 1.6 / C# 7.3
// Source/UI/RightClickRescue/WGResolve.cs
// Helper utilities to robustly resolve WorkGiverDef and JobDef instances by worker type or defName.
// Safe fallbacks (return null) instead of throwing, to keep rescue scanners resilient across mod loads.

using System;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;

namespace SurvivalTools.UI.RightClickRescue
{
    internal static class WGResolve
    {
        // Cache worker runtime type name -> WorkGiverDef for fast repeated lookups
        private static readonly System.Collections.Generic.Dictionary<string, WorkGiverDef> _workerTypeCache = new System.Collections.Generic.Dictionary<string, WorkGiverDef>();
        private static bool _cacheBuilt;
        private static void BuildCacheIfNeeded()
        {
            if (_cacheBuilt) return;
            _cacheBuilt = true;
            try
            {
                foreach (var def in DefDatabase<WorkGiverDef>.AllDefs)
                {
                    if (def == null) continue;
                    WorkGiver worker = null;
                    try { worker = def.Worker; } catch { }
                    if (worker == null) continue;
                    var t = worker.GetType();
                    var full = t.FullName; var name = t.Name;
                    if (!string.IsNullOrEmpty(full) && !_workerTypeCache.ContainsKey(full)) _workerTypeCache[full] = def;
                    if (!string.IsNullOrEmpty(name) && !_workerTypeCache.ContainsKey(name)) _workerTypeCache[name] = def;
                }
            }
            catch { /* best effort */ }
        }
        internal static WorkGiverDef ByWorkerTypes(params string[] typeNames)
        {
            if (typeNames == null) return null;
            BuildCacheIfNeeded();
            foreach (var n in typeNames)
            {
                if (string.IsNullOrEmpty(n)) continue;
                // Direct cache hit by short or fully qualified name first
                if (_workerTypeCache.TryGetValue(n, out var cached)) return cached;
                var maybeFull = "RimWorld." + n;
                if (_workerTypeCache.TryGetValue(maybeFull, out cached)) return cached;

                // Fallback: resolve Type then attempt cache by type full/name after instantiation
                var t = AccessTools.TypeByName(maybeFull) ?? AccessTools.TypeByName(n);
                if (t == null) continue;
                try
                {
                    foreach (var def in DefDatabase<WorkGiverDef>.AllDefs)
                    {
                        if (def == null) continue;
                        WorkGiver worker = null; try { worker = def.Worker; } catch { }
                        if (worker == null) continue;
                        var wt = worker.GetType();
                        if (wt == t)
                        {
                            var full = wt.FullName; var name = wt.Name;
                            if (!string.IsNullOrEmpty(full)) _workerTypeCache[full] = def;
                            if (!string.IsNullOrEmpty(name)) _workerTypeCache[name] = def;
                            return def;
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        internal static JobDef Job(string defName, Func<JobDef> ofFallback = null)
        {
            if (string.IsNullOrEmpty(defName)) return ofFallback?.Invoke();
            try { return DefDatabase<JobDef>.GetNamedSilentFail(defName) ?? ofFallback?.Invoke(); }
            catch { return ofFallback?.Invoke(); }
        }

        internal static JobDef Of(Func<JobDef> of)
        {
            if (of == null) return null;
            try { return of(); } catch { return null; }
        }
    }
}
