// RimWorld 1.6 / C# 7.3
// Source/Compatibility/ResearchReinvented/ResearchReinventedCompat.cs
//
// Research Reinvented compatibility module for SurvivalTools
// Delegates reflection + stat handling to RRReflectionAPI.

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Compat.ResearchReinvented
{
    /// <summary>
    /// Compatibility module for Research Reinvented (PeteTimesSix).
    /// Handles detection, initialization, and high-level integration.
    /// Detailed reflection + stat handling lives in RRReflectionAPI.
    /// </summary>
    public static class ResearchReinventedCompat
    {
        private static bool? _isActive;
        private static bool _initialized;
        private static readonly object _lock = new object();

        #region Detection

        /// <summary>
        /// True if Research Reinvented is loaded & active.
        /// </summary>
        public static bool IsActive
        {
            get
            {
                if (_isActive.HasValue) return _isActive.Value;
                lock (_lock)
                {
                    if (_isActive.HasValue) return _isActive.Value;
                    try
                    {
                        _isActive = ModLister.AllInstalledMods.Any(mod =>
                            mod.Active &&
                            (mod.PackageId.IndexOf("researchreinvented", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             mod.Name.IndexOf("Research Reinvented", StringComparison.OrdinalIgnoreCase) >= 0));

                        if (IsCompatLogging())
                            LogCompat($"[SurvivalTools] RR detection: {_isActive}");
                    }
                    catch (Exception e)
                    {
                        LogError($"[SurvivalTools] RR detection failed: {e}");
                        _isActive = false;
                    }
                    return _isActive.Value;
                }
            }
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Called once on startup if RR is active.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;

                try
                {
                    if (!IsActive)
                    {
                        if (IsCompatLogging())
                            LogCompat("[SurvivalTools] RR not active, skipping initialization.");
                        return;
                    }

                    if (IsCompatLogging())
                        LogCompat("[SurvivalTools] Initializing RR compatibility...");

                    RRReflectionAPI.Initialize();

                    if (IsCompatLogging())
                        LogCompat("[SurvivalTools] RR compatibility initialization complete.");
                }
                catch (Exception e)
                {
                    LogError($"[SurvivalTools] RR compatibility init failed: {e}");
                }
                finally
                {
                    _initialized = true;
                }
            }
        }

        #endregion

        #region Settings integration

        /// <summary>
        /// True if both RR is active and ST settings allow integration.
        /// </summary>
        public static bool IsEnabled =>
            IsActive && RRSettings.IsRRCompatibilityEnabled;

        #endregion

        #region Diagnostics

        /// <summary>
        /// Return a structured snapshot of RR integration state.
        /// </summary>
        public static string GetDiagnosticInfo()
        {
            try
            {
                return string.Join(Environment.NewLine, new[]
                {
                    $"Research Reinvented Active: {IsActive}",
                    $"RR Compatibility Enabled: {IsEnabled}",
                    $"Compatibility Stats: {RRReflectionAPI.GetAllResearchStats().Count}"
                });
            }
            catch (Exception e)
            {
                return $"[SurvivalTools] RR diagnostic failed: {e}";
            }
        }

        #endregion

        #region Legacy stubs (kept for future reference)

        /*
        /// <summary>
        /// Get all survival tool ThingDefs that are affected by research requirements.
        /// </summary>
        public static List<ThingDef> GetResearchGatedTools()
        {
            try
            {
                return DefDatabase<ThingDef>.AllDefs
                    .Where(def => def.GetModExtension<SurvivalToolProperties>() != null)
                    .Where(def => def.researchPrerequisites?.Any() == true)
                    .ToList();
            }
            catch (Exception e)
            {
                LogError($"[SurvivalTools] Error getting research-gated tools: {e}");
                return new List<ThingDef>();
            }
        }

        /// <summary>
        /// Check if a specific tool is available given current research progress.
        /// Integrates with RR's research system when available.
        /// </summary>
        public static bool IsToolAvailableForResearch(ThingDef toolDef, Pawn researcher = null)
        {
            if (toolDef?.GetModExtension<SurvivalToolProperties>() == null)
                return true; // Not a survival tool

            if (!IsEnabled)
                return true; // RR not active, use vanilla research

            try
            {
                // Placeholder: integrate with RR research system if needed
                return true;
            }
            catch (Exception e)
            {
                LogError($"[SurvivalTools] Error checking tool research availability: {e}");
                return true; // Default to available on error
            }
        }
        */

        #endregion
    }
}
