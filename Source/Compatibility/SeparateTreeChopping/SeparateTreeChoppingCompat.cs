// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SeparateTreeChoppingCompat.cs
//
// Separate Tree Chopping compatibility module for SurvivalTools
// Provides integration and conflict resolution with the Separate Tree Chopping (Continued) mod

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SurvivalTools.Compat.SeparateTreeChopping
{
    /// <summary>
    /// Compatibility module for Separate Tree Chopping (Continued) mod.
    /// Handles conflict detection, resolution strategies, and feature coordination.
    /// </summary>
    public static class SeparateTreeChoppingCompat
    {
        private static bool? _isSeparateTreeChoppingActive;
        private static readonly object _lockObject = new object();

        #region Core Detection

        /// <summary>
        /// Check if Separate Tree Chopping (Continued) mod is currently active.
        /// </summary>
        public static bool IsSeparateTreeChoppingActive()
        {
            if (_isSeparateTreeChoppingActive.HasValue)
                return _isSeparateTreeChoppingActive.Value;

            lock (_lockObject)
            {
                if (_isSeparateTreeChoppingActive.HasValue)
                    return _isSeparateTreeChoppingActive.Value;

                try
                {
                    var activeMods = ModsConfig.ActiveModsInLoadOrder;
                    bool hasSeparateTreeMod = activeMods.Any(mod =>
                    {
                        string name = (mod?.Name ?? string.Empty).ToLowerInvariant();
                        string id = (mod?.PackageId ?? string.Empty).ToLowerInvariant();
                        return name.Contains("separate tree chopping") ||
                               name.Contains("separatetreechopping") ||
                               id.Contains("separatetreechopping") ||
                               id.Contains("treechopping");
                    });

                    _isSeparateTreeChoppingActive = hasSeparateTreeMod;

                    if (ST_Logging.IsDebugLoggingEnabled && ST_Logging.IsCompatLogging())
                        ST_Logging.LogCompatMessage($"Separate Tree Chopping detection: {_isSeparateTreeChoppingActive}");

                    return _isSeparateTreeChoppingActive.Value;
                }
                catch (Exception ex)
                {
                    ST_Logging.LogCompatError($"Exception during Separate Tree Chopping detection: {ex}");
                    _isSeparateTreeChoppingActive = false;
                    return false;
                }
            }
        }

        #endregion

        #region Conflict Detection and Resolution

        private static bool SurvivalToolsTreeFellingEnabled =>
            SurvivalTools.Settings != null && SurvivalTools.Settings.enableSurvivalToolTreeFelling;

        /// <summary>
        /// Check if there are conflicts between SurvivalTools tree felling and Separate Tree Chopping.
        /// </summary>
        public static bool HasTreeFellingConflict()
        {
            return IsSeparateTreeChoppingActive() && SurvivalToolsTreeFellingEnabled;
        }

        /// <summary>
        /// Get recommended resolution strategy for tree felling conflicts.
        /// </summary>
        public static TreeFellingConflictResolution GetRecommendedResolution()
        {
            return HasTreeFellingConflict()
                ? TreeFellingConflictResolution.DisableSurvivalToolsTreeFelling
                : TreeFellingConflictResolution.NoConflict;
        }

        /// <summary>
        /// Apply the recommended conflict resolution automatically.
        /// </summary>
        public static bool ApplyRecommendedResolution()
        {
            switch (GetRecommendedResolution())
            {
                case TreeFellingConflictResolution.NoConflict:
                    return true;

                case TreeFellingConflictResolution.DisableSurvivalToolsTreeFelling:
                    if (SurvivalTools.Settings != null)
                    {
                        SurvivalTools.Settings.enableSurvivalToolTreeFelling = false;
                        ST_Logging.LogCompatMessage("Auto-disabled SurvivalTools tree felling due to Separate Tree Chopping conflict");
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Describe current conflict state in plain text.
        /// </summary>
        private static string DescribeConflict()
        {
            if (!IsSeparateTreeChoppingActive())
                return "Separate Tree Chopping not active";

            return HasTreeFellingConflict()
                ? "Both mods attempt to handle tree felling (conflict possible)"
                : "No conflicts detected";
        }

        /// <summary>
        /// Check for all potential conflicts and return detailed information.
        /// </summary>
        public static List<string> CheckForConflicts()
        {
            var conflicts = new List<string>();

            if (!IsSeparateTreeChoppingActive())
                return conflicts;

            try
            {
                if (HasTreeFellingConflict())
                    conflicts.Add("Both SurvivalTools and Separate Tree Chopping handle tree felling — may cause duplicate jobs");

                var fellTreesWorkGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ST_FellTrees");
                if (fellTreesWorkGiver != null && HasTreeFellingConflict())
                    conflicts.Add("SurvivalTools FellTrees WorkGiver may overlap with Separate Tree Chopping");

                return conflicts;
            }
            catch (Exception ex)
            {
                ST_Logging.LogCompatError($"Exception checking Separate Tree Chopping conflicts: {ex}");
                conflicts.Add($"Exception during conflict check: {ex.Message}");
                return conflicts;
            }
        }

        #endregion

        #region Settings Integration

        public static bool ShouldAutoDisableSTTreeFelling()
        {
            return IsSeparateTreeChoppingActive() &&
                   GetRecommendedResolution() == TreeFellingConflictResolution.DisableSurvivalToolsTreeFelling;
        }

        public static string GetTreeFellingDisabledMessage()
        {
            return IsSeparateTreeChoppingActive()
                ? "Tree felling has been automatically disabled because Separate Tree Chopping is active."
                : null;
        }

        #endregion

        #region User Guidance

        public static List<string> GetUserRecommendations()
        {
            var recs = new List<string>();

            if (!IsSeparateTreeChoppingActive())
                return recs;

            recs.Add("Separate Tree Chopping detected.");

            if (HasTreeFellingConflict())
            {
                recs.Add("Recommendation: Disable 'Enable Tree Felling System' in SurvivalTools settings.");
                recs.Add("This allows Separate Tree Chopping to handle tree cutting without conflicts.");
                recs.Add("SurvivalTools tools (axes etc.) still give stat bonuses.");
            }
            else
            {
                recs.Add("No tree felling conflicts detected. Both mods should work together smoothly.");
            }

            return recs;
        }

        #endregion
    }

    /// <summary>
    /// Conflict resolution strategies for tree felling systems.
    /// </summary>
    public enum TreeFellingConflictResolution
    {
        NoConflict,
        DisableSurvivalToolsTreeFelling,
        DisableSeparateTreeChopping, // Not recommended
        UserChoice // Let user decide manually
    }
}
