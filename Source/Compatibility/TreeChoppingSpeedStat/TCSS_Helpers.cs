// RimWorld 1.6 / C# 7.3
// Source/Compatibility/TreeChoppingSpeedStat/TCSS_Helpers.cs
// Detection and authority helpers for Tree Chopping Speed Stat (TCSS) mod integration.

using System;
using System.Linq;
using HarmonyLib;
using Verse;

namespace SurvivalTools.Compat.TCSS
{
    internal static class TCSS_Helpers
    {
        public static string ModName => "Tree Chopping Speed Stat";
        public static string ShortName => "TCSS";

        private static bool? _active;
        private static bool _smokeLogged;

        /// <summary>
        /// Returns true if TCSS mod is present and enabled.
        /// Uses type probe first (stable), then fallback to packageId/name search.
        /// </summary>
        public static bool IsActive()
        {
            if (_active.HasValue) return _active.Value;

            try
            {
                // Type probe: look for a known stable TCSS type
                var tcssType = AccessTools.TypeByName("TreeChopSpeed.WorkTypeDefOf");
                if (tcssType != null)
                {
                    _active = true;
                    LogSmokeTest();
                    return true;
                }

                // Fallback: packageId/name search
                var mods = LoadedModManager.RunningModsListForReading;
                if (mods != null)
                {
                    bool found = mods.Any(m =>
                    {
                        if (m == null) return false;
                        string name = (m.Name ?? string.Empty).ToLowerInvariant();
                        string id = (m.PackageId ?? string.Empty).ToLowerInvariant();
                        return name.Contains("tree chopping speed") ||
                               name.Contains("treechoppingspeed") ||
                               name.Contains("tcss") ||
                               id.Contains("treechoppingspeed") ||
                               id.Contains("tcss");
                    });

                    _active = found;
                    LogSmokeTest();
                    return found;
                }

                _active = false;
                return false;
            }
            catch (Exception ex)
            {
                TCSS_Debug.LogCompatWarning($"[{ShortName}] Detection exception: {ex.Message}");
                _active = false;
                return false;
            }
        }

        /// <summary>
        /// Returns true when TCSS is active AND STC is NOT active (TCSS has authority).
        /// If both TCSS and STC are detected, STC wins and this returns false.
        /// </summary>
        public static bool IsAuthorityActive()
        {
            if (!IsActive()) return false;

            // Check STC authority via TreeSystemArbiter
            try
            {
                var auth = SurvivalTools.Compatibility.TreeStack.TreeSystemArbiter.Authority;
                bool stcActive = auth == SurvivalTools.Compatibility.TreeStack.TreeAuthority.SeparateTreeChopping;

                if (stcActive)
                {
                    // Log once per session that both are detected and STC wins
                    if (!_smokeLogged && TCSS_Debug.IsCompatLogging())
                    {
                        TCSS_Debug.LogCompatWarning($"[{ShortName}] Both TCSS and STC detected; STC takes authority (TCSS inactive).");
                    }
                    return false;
                }

                // TCSS is active and STC is not active → TCSS has authority
                return true;
            }
            catch (Exception ex)
            {
                TCSS_Debug.LogCompatWarning($"[{ShortName}] Authority check exception: {ex.Message}");
                return false;
            }
        }

        private static void LogSmokeTest()
        {
            if (_smokeLogged) return;
            _smokeLogged = true;

            if (TCSS_Debug.IsCompatLogging())
            {
                bool authority = IsAuthorityActive();
                TCSS_Debug.LogCompat($"[{ShortName}] Detected: Active={_active}, Authority={authority}");
            }
        }
    }
}
