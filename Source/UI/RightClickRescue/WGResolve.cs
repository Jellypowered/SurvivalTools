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
        internal static WorkGiverDef ByWorkerTypes(params string[] typeNames)
        {
            if (typeNames == null) return null;
            foreach (var n in typeNames)
            {
                if (string.IsNullOrEmpty(n)) continue;
                // Allow fully qualified or short names (try RimWorld. prefix first as most vanilla workers live there)
                var t = AccessTools.TypeByName("RimWorld." + n) ?? AccessTools.TypeByName(n);
                if (t == null) continue;
                try
                {
                    // Some mod environments may have different accessibility; use simple scan with reflection fallback.
                    foreach (var def in DefDatabase<WorkGiverDef>.AllDefs)
                    {
                        if (def == null) continue;
                        try
                        {
                            // Use instantiated worker's runtime type; safe across versions even if field name changes.
                            var worker = def.Worker; // this creates the worker on demand
                            if (worker != null && worker.GetType() == t)
                                return def;
                        }
                        catch { /* ignore */ }
                    }
                }
                catch { /* best-effort */ }
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
