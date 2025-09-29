// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SeparateTreeChopping/STCHelpers_Phase10.cs
// Phase 10: Bulk WG mapping + right-click eligibility for tree chopping (Separate Tree Chopping + vanilla).

using System;
using HarmonyLib;
// (Legacy namespace removed) conflict logic now local in this file
using System.Linq; // needed for Any()
using System.Collections.Generic; // List<string>
using RimWorld;
using Verse;

namespace SurvivalTools.Compatibility.SeparateTreeChopping
{
    internal static class STCHelpers
    {
        private static readonly string PkgIdGuess = "saucysalad.separatetreechopping"; // safe guess

        internal static bool Active =>
            ModsConfig.ActiveModsInLoadOrder.Any(m =>
                (m.PackageId != null && m.PackageId.Equals(PkgIdGuess, StringComparison.OrdinalIgnoreCase)) ||
                (m.Name != null && m.Name.IndexOf("Separate Tree Chopping", StringComparison.OrdinalIgnoreCase) >= 0)) ||
            AccessTools.TypeByName("SeparateTreeChopping.WorkGiver_ChopTrees") != null;

        internal static void Initialize()
        {
            try
            {
                if (!Active) return; // no-op when mod absent

                // Tree WorkGiver mapping & right-click eligibility is now owned centrally by TreeWorkGiverMappings
                // based on TreeSystemArbiter.Authority. STCHelpers no longer maps or registers here to avoid duplicates.

                // Integrate legacy conflict auto-resolution logic (formerly in SeparateTreeChoppingCompatibilityModule)
                if (SeparateTreeChoppingConflict.ShouldAutoDisableSTTreeFelling())
                {
#if DEBUG
                    if (ST_Logging.IsCompatLogging() && ST_Logging.IsDebugLoggingEnabled)
                        Log.Message("[SurvivalTools Compat][STC] Auto-resolving tree felling overlap (disabling ST tree felling)");
#endif
                    if (!SeparateTreeChoppingConflict.ApplyRecommendedResolution())
                        Log.Warning("[SurvivalTools Compat][STC] Failed to auto-resolve tree felling conflict.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SurvivalTools][SeparateTreeChopping] Initialize error: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Phase 10 internal conflict logic (migrated from legacy SeparateTreeChoppingHelpers).
    /// </summary>
    internal static class SeparateTreeChoppingConflict
    {
        private static bool? _active;
        private static readonly object _lock = new object();

        internal static bool IsSeparateTreeChoppingActive()
        {
            if (_active.HasValue) return _active.Value;
            lock (_lock)
            {
                if (_active.HasValue) return _active.Value;
                try
                {
                    var activeMods = ModsConfig.ActiveModsInLoadOrder;
                    bool has = activeMods.Any(mod =>
                    {
                        string name = (mod?.Name ?? string.Empty).ToLowerInvariant();
                        string id = (mod?.PackageId ?? string.Empty).ToLowerInvariant();
                        return name.Contains("separate tree chopping") ||
                               name.Contains("separatetreechopping") ||
                               id.Contains("separatetreechopping") ||
                               id.Contains("treechopping");
                    });
                    _active = has;
                    if (ST_Logging.IsDebugLoggingEnabled && ST_Logging.IsCompatLogging())
                        ST_Logging.LogCompatMessage($"[STC] detection: {_active}");
                }
                catch (Exception ex)
                {
                    ST_Logging.LogCompatError("[STC] detection exception: " + ex);
                    _active = false;
                }
                return _active.Value;
            }
        }

        private static bool SurvivalToolsTreeFellingEnabled =>
            SurvivalToolsMod.Settings != null && SurvivalToolsMod.Settings.enableSurvivalToolTreeFelling;

        internal static bool HasTreeFellingConflict() => IsSeparateTreeChoppingActive() && SurvivalToolsTreeFellingEnabled;

        internal static TreeFellingConflictResolution GetRecommendedResolution() =>
            HasTreeFellingConflict() ? TreeFellingConflictResolution.DisableSurvivalToolsTreeFelling : TreeFellingConflictResolution.NoConflict;

        internal static bool ApplyRecommendedResolution()
        {
            switch (GetRecommendedResolution())
            {
                case TreeFellingConflictResolution.NoConflict:
                    return true;
                case TreeFellingConflictResolution.DisableSurvivalToolsTreeFelling:
                    if (SurvivalToolsMod.Settings != null)
                    {
                        SurvivalToolsMod.Settings.enableSurvivalToolTreeFelling = false;
                        ST_Logging.LogCompatMessage("[STC] Auto-disabled SurvivalTools tree felling (conflict)");
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        internal static bool ShouldAutoDisableSTTreeFelling() =>
            IsSeparateTreeChoppingActive() && GetRecommendedResolution() == TreeFellingConflictResolution.DisableSurvivalToolsTreeFelling;

        internal static List<string> GetUserRecommendations()
        {
            var recs = new List<string>();
            if (!IsSeparateTreeChoppingActive()) return recs;
            recs.Add("Separate Tree Chopping detected.");
            if (HasTreeFellingConflict())
            {
                recs.Add("Recommendation: Disable 'Enable Tree Felling System' in SurvivalTools settings.");
                recs.Add("Let STC handle tree cutting; SurvivalTools tools still provide stat bonuses.");
            }
            else recs.Add("No tree felling conflicts detected.");
            return recs;
        }

        internal static List<string> CheckForConflicts()
        {
            var conflicts = new List<string>();
            if (!IsSeparateTreeChoppingActive()) return conflicts;
            try
            {
                if (HasTreeFellingConflict())
                    conflicts.Add("Both SurvivalTools and STC handle tree felling – duplicate jobs possible");
                var fellTrees = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ST_FellTrees");
                if (fellTrees != null && HasTreeFellingConflict())
                    conflicts.Add("ST_FellTrees WorkGiver may overlap with STC");
            }
            catch (Exception ex)
            {
                ST_Logging.LogCompatError("[STC] conflict check exception: " + ex);
                conflicts.Add("Exception: " + ex.Message);
            }
            return conflicts;
        }
    }

    // Legacy public enum kept for API stability
    public enum TreeFellingConflictResolution
    {
        NoConflict,
        DisableSurvivalToolsTreeFelling,
        DisableSeparateTreeChopping,
        UserChoice
    }

    // Obsolete shim preserving old name (other mods / future reflection safety)
    [Obsolete("Use SeparateTreeChoppingConflict (Phase 10) instead.")]
    public static class SeparateTreeChoppingHelpers
    {
        public static bool IsSeparateTreeChoppingActive() => SeparateTreeChoppingConflict.IsSeparateTreeChoppingActive();
        public static bool HasTreeFellingConflict() => SeparateTreeChoppingConflict.HasTreeFellingConflict();
        public static TreeFellingConflictResolution GetRecommendedResolution() => SeparateTreeChoppingConflict.GetRecommendedResolution();
        public static bool ApplyRecommendedResolution() => SeparateTreeChoppingConflict.ApplyRecommendedResolution();
        public static bool ShouldAutoDisableSTTreeFelling() => SeparateTreeChoppingConflict.ShouldAutoDisableSTTreeFelling();
        public static List<string> GetUserRecommendations() => SeparateTreeChoppingConflict.GetUserRecommendations();
        public static List<string> CheckForConflicts() => SeparateTreeChoppingConflict.CheckForConflicts();
        public static string GetTreeFellingDisabledMessage() => IsSeparateTreeChoppingActive() ? "Tree felling auto-disabled because STC is active." : null;
    }
}
