// RimWorld 1.6 / C# 7.3
// Source/Compatibility/ResearchReinvented/RRSettings.cs
//
// ResearchReinvented-specific settings integration.
// Extends SurvivalToolsSettings with RR-specific options.

using RimWorld;
using Verse;

namespace SurvivalTools.Compat.ResearchReinvented
{
    /// <summary>
    /// RR-specific settings that integrate with the main SurvivalTools settings.
    /// Only visible when RR is detected.
    /// </summary>
    public static class RRSettings
    {
        /// <summary>
        /// Check if RR compatibility should be enabled.
        /// Default: ON when RR detected, OFF otherwise.
        /// </summary>
        public static bool IsRRCompatibilityEnabled
        {
            get
            {
                if (!RRReflectionAPI.IsResearchReinventedActive()) return false;
                return SurvivalTools.Settings?.enableRRCompatibility ?? true;
            }
        }

        /// <summary>
        /// Check if Research stats should be required in Extra Hardcore mode.
        /// </summary>
        public static bool TreatResearchAsRequiredInExtraHardcore =>
            SurvivalTools.Settings?.rrResearchRequiredInExtraHardcore ?? false;

        /// <summary>
        /// Check if Field Research stats should be required in Extra Hardcore mode.
        /// </summary>
        public static bool TreatFieldResearchAsRequiredInExtraHardcore =>
            SurvivalTools.Settings?.rrFieldResearchRequiredInExtraHardcore ?? false;

        /// <summary>
        /// Check if a stat should be treated as required based on RR settings and Extra Hardcore mode.
        /// Integrates with existing IsStatRequiredInExtraHardcore system.
        /// </summary>
        public static bool IsRRStatRequiredInExtraHardcore(StatDef stat)
        {
            if (!IsRRCompatibilityEnabled || stat == null) return false;

            var settings = SurvivalTools.Settings;
            if (settings?.extraHardcoreMode != true) return false;

            // ResearchSpeed
            if (stat == RRReflectionAPI.GetResearchSpeedStat())
                return TreatResearchAsRequiredInExtraHardcore;

            // FieldResearchSpeedMultiplier
            if (stat == RRReflectionAPI.GetFieldResearchSpeedStat())
                return TreatFieldResearchAsRequiredInExtraHardcore;

            return false;
        }

        /// <summary>
        /// Check if a stat should be treated as optional (default behavior for RR stats).
        /// </summary>
        public static bool IsRRStatOptional(StatDef stat)
        {
            if (!IsRRCompatibilityEnabled || stat == null) return false;

            return stat == RRReflectionAPI.GetResearchSpeedStat()
                || stat == RRReflectionAPI.GetFieldResearchSpeedStat();
        }

        /// <summary>
        /// Get the effective requirement level for an RR stat.
        /// Respects Extra Hardcore settings and user preferences.
        /// </summary>
        public static StatRequirementLevel GetRRStatRequirementLevel(StatDef stat)
        {
            if (!IsRRCompatibilityEnabled || stat == null)
                return StatRequirementLevel.None;

            if (IsRRStatRequiredInExtraHardcore(stat))
                return StatRequirementLevel.Required;

            if (IsRRStatOptional(stat))
                return StatRequirementLevel.Optional;

            return StatRequirementLevel.None;
        }
    }

    /// <summary>
    /// Requirement levels for stats in the tool system.
    /// </summary>
    public enum StatRequirementLevel
    {
        None,      // Not required, no effect
        Optional,  // Improves efficiency but not required
        Required   // Blocks job if tool unavailable
    }
}
