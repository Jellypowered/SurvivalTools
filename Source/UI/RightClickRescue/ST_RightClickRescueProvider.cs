// RimWorld 1.6 / C# 7.3
// Source/UI/RightClickRescue/ST_RightClickRescueProvider.cs
// Phase 10: Registry for external WorkGiver worker subclasses eligible for right-click rescue logic.
// External mods can call CompatAPI.RegisterRightClickEligibleWGSubclass to add their custom scanners.

using System;
using System.Collections.Generic;
using Verse;
using System.Linq;

namespace SurvivalTools.UI.RightClickRescue
{
    internal static class ST_RightClickRescueProvider
    {
        // Hash set of WorkGiver (any subclass). Previously restricted to WorkGiver_Scanner.
        private static readonly HashSet<Type> _eligibleWorkerSubclasses = new HashSet<Type>();
        // Per-click rejection logging (dev mode) so we don't spam the log for repeated ineligible checks.
        private static readonly HashSet<Type> _rejectionLoggedThisClick = new HashSet<Type>();
        private static int _rejectionScopeTick;

        internal static void BeginClickScope(int currentTick)
        {
            try
            {
                if (currentTick != _rejectionScopeTick)
                {
                    _rejectionScopeTick = currentTick;
                    _rejectionLoggedThisClick.Clear();
                }
            }
            catch { }
        }

        /// <summary>
        /// Register a WorkGiver (any subclass of RimWorld.WorkGiver) as right-click rescue eligible.
        /// Ignores nulls or unrelated types. Idempotent. DevOnce log for first registration per type.
        /// </summary>
        public static void RegisterWorkerSubclass(Type t)
        {
            try
            {
                if (t == null) return;
                if (!typeof(RimWorld.WorkGiver).IsAssignableFrom(t)) return; // broadened guard (was WorkGiver_Scanner)
                if (_eligibleWorkerSubclasses.Add(t))
                {
                    ST_Logging.DevOnce($"RightClick.Reg.{t.FullName}", $"[ST.RightClick] Registered rescue-eligible WG: {t.FullName}");
                }
            }
            catch { }
        }

        /// <summary>
        /// True if the provided worker type is (or derives from / base of) any registered eligible type.
        /// Accepts any RimWorld.WorkGiver subclass.
        /// </summary>
        internal static bool IsEligible(Type workerType)
        {
            if (workerType == null) return false;
            if (!typeof(RimWorld.WorkGiver).IsAssignableFrom(workerType)) return false; // ignore non-WorkGiver types
            // Requirement: eligible when any registeredType.IsAssignableFrom(workerType).
            foreach (var registered in _eligibleWorkerSubclasses)
            {
                if (registered == null) continue;
                if (registered.IsAssignableFrom(workerType))
                    return true;
            }

            // Dev-mode per-click rejection log (once per worker type per click scope)
            try
            {
                var settings = SurvivalToolsMod.Settings;
                if (Prefs.DevMode && settings != null && settings.debugLogging)
                {
                    int now = Find.TickManager?.TicksGame ?? 0;
                    BeginClickScope(now); // ensure scope freshness if caller forgot
                    if (!_rejectionLoggedThisClick.Contains(workerType))
                    {
                        _rejectionLoggedThisClick.Add(workerType);
                        Verse.Log.Message($"[ST.RightClick] Ineligible WorkGiver (no registered base match): {workerType.FullName}");
                    }
                }
            }
            catch { }

            return false;
        }

        public static void LogSummaryOnce()
        {
            try
            {
                var preview = string.Join(", ", _eligibleWorkerSubclasses.Take(8).Select(tt => tt.FullName));
                ST_Logging.DevOnce("RightClick.Summary", $"[ST.RightClick] Eligible subclasses: {_eligibleWorkerSubclasses.Count}" + (_eligibleWorkerSubclasses.Count > 0 ? $" (showing up to 8) {preview}" : string.Empty));
            }
            catch { }
        }

        internal static IEnumerable<Type> DebugEnumerateRegistered() => _eligibleWorkerSubclasses;

        internal static IEnumerable<string> GetRegisteredSubclassNames()
        {
            foreach (var t in _eligibleWorkerSubclasses) yield return t.FullName;
        }
    }
}
