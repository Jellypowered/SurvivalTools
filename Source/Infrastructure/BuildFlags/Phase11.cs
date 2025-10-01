using System;

namespace SurvivalTools.Build
{
    /// <summary>
    /// Phase 11 compile-time feature flags for incremental legacy code removal.
    /// All flags default to false for safety - enable one at a time to strip legacy components.
    /// </summary>
    public static class Phase11
    {
        /// <summary>
        /// Strip duplicate optimizer logic (11.1)
        /// </summary>
        public const bool STRIP_11_1_DUP_OPTIMIZER = true;

        /// <summary>
        /// Strip duplicate stat injector implementations (11.2)
        /// NOTE: Already complete in Phase 4 - no duplicate stat injectors exist.
        /// StatPart_SurvivalTools is the single source of truth for all work stat modifications.
        /// This flag exists for documentation/completeness but has no associated code guards.
        /// </summary>
        public const bool STRIP_11_2_DUP_STAT_INJECTORS = true; // No-op: already consolidated in Phase 4

        /// <summary>
        /// Strip miscellaneous WorkGiver gates (11.3)
        /// NOTE: Already complete in Phase 5-6 - no duplicate gating logic exists.
        /// JobGate.ShouldBlock() is the single authority for all gating decisions.
        /// Helper methods (StatGatingHelper, etc.) are utilities called BY JobGate, not duplicates.
        /// This flag exists for documentation/completeness but has no associated code guards.
        /// </summary>
        public const bool STRIP_11_3_MISC_WG_GATES = true; // No-op: already consolidated in Phase 5-6

        /// <summary>
        /// Strip old invalidation logic (11.4)
        /// Modern system (Phase 4): HarmonyPatches_CacheInvalidation + resolver version + dynamic HP scoring
        /// Legacy hooks (Patch_ToolInvalidation) are redundant - all cases covered by modern system
        /// </summary>
        public const bool STRIP_11_4_OLD_INVALIDATION = true;

        /// <summary>
        /// Strip old FloatMenu implementations (11.5)
        /// Modern system: Provider_STPrioritizeWithRescue + FloatMenu_PrioritizeWithRescue (comprehensive postfix)
        /// Legacy fallback (Patch_FloatMenuMakerMap_GetOptions) is redundant - provider system now stable
        /// </summary>
        public const bool STRIP_11_5_OLD_FLOATMENU = true;

        /// <summary>
        /// Strip old scoring method calls (11.6)
        /// NOTE: Already complete in Phase 9 - no internal uses of ToolScoreUtility exist.
        /// All internal code uses SurvivalTools.Scoring.ToolScoring directly.
        /// Legacy forwarders (LegacyScoringForwarders.cs) preserved with [Obsolete(false)] for external mod compatibility.
        /// This flag exists for documentation/completeness but has no associated code guards.
        /// </summary>
        public const bool STRIP_11_6_OLD_SCORING_CALLS = true; // No-op: already consolidated in Phase 9

        /// <summary>
        /// Strip XML duplicate hints/comments (11.7)
        /// NOTE: Investigation complete - no duplicate XML patches exist.
        /// All 86 XML patches serve legitimate purposes:
        /// - Material patches (StuffPropsTool) define material quality data that resolver cannot infer
        /// - Tool patches (SurvivalToolProperties) set explicit balance values that override resolver's generic inference
        /// Resolver provides safety net, XML provides authoritative data. Both are needed.
        /// This flag exists for documentation/completeness but has no associated code guards.
        /// </summary>
        public const bool STRIP_11_7_XML_DUP_HINTS = true; // No-op: XML patches are authoritative data, not duplicates

        /// <summary>
        /// Strip tree toggles/switches (11.8)
        /// NOTE: Investigation complete - no conflicting tree toggles exist.
        /// STC integration (Phase 10) already uses centralized TreeSystemArbiter authority.
        /// All guards use IsSTCAuthorityActive() consistently. No ad-hoc toggles found.
        /// User setting enableSurvivalToolTreeFelling is valid preference, correctly overridden by STC.
        /// TreeFellingSpeed gating preserved for STC jobs. No cleanup needed.
        /// This flag exists for documentation/completeness but has no associated code guards.
        /// </summary>
        public const bool STRIP_11_8_TREE_TOGGLES = true; // No-op: Tree system uses centralized STC authority (Phase 10)

        /// <summary>
        /// Strip killlist/deprecated components (11.9)
        /// Phase 11.9: Dead code removal complete. Deleted method bodies in:
        /// - LegacyForwarders.cs (11.1): Kept [Obsolete(false)] class shells for external mod compatibility
        /// - Patch_ToolInvalidation.cs (11.4): Kept Harmony patch structure as no-op stubs
        /// - Patch_FloatMenuMakerMap_GetOptions.cs (11.5): Kept Harmony patch structure as no-op stubs
        /// Public API preserved for external mods. Internal dead code removed.
        /// </summary>
        public const bool STRIP_11_9_KILLLIST = true;

        /// <summary>
        /// Helper method to conditionally execute stripping logic based on a feature flag.
        /// Only executes the action if the gate is true.
        /// </summary>
        /// <param name="gate">The feature flag controlling this strip operation</param>
        /// <param name="doStrip">The action to execute if stripping is enabled</param>
        public static void Phase11If(bool gate, Action doStrip)
        {
            if (gate && doStrip != null)
            {
                doStrip();
            }
        }
    }
}
