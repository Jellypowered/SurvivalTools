using UnityEngine;
using Verse;
using RimWorld;

namespace SurvivalTools
{
    public class SurvivalToolsSettings : ModSettings
    {
        public bool hardcoreMode;
        public bool toolMapGen = true;
        public bool toolLimit = true;
        public float toolDegradationFactor = 1f;
        public bool toolOptimization = true;

        public bool autoTool = true;

        public bool debugLogging = false;
        public bool pickupFromStorageOnly = false;

        // No-tool penalty multiplier (normal mode)
        public float noToolStatFactor = 0.5f;

        // Exclude temporary pawns (quest rewards, etc.) from auto tool pickup
        public bool excludeTemporaryPawns = true;

        // Allow pacifists to equip survival tools (even if IsWeapon)
        public bool allowPacifistEquip = true;

        // Extra Hardcore Mode - requires tools for all work
        public bool extraHardcoreMode = false;

        // Individual optional tool requirements (only shown when extra hardcore is enabled)
        public bool requireCleaningTools = true;
        public bool requireButcheryTools = true;
        public bool requireMedicalTools = true;

        // Cached availability of optional tool types (performance optimization)
        private bool? _hasCleaningToolsCache = null;
        private bool? _hasButcheryToolsCache = null;
        private bool? _hasMedicalToolsCache = null;
        private bool _cacheInitialized = false;

        public bool ToolDegradationEnabled => toolDegradationFactor > 0.001f;

        /// <summary>
        /// Gets the effective tool degradation factor, including extra hardcore mode bonus
        /// </summary>
        public float EffectiveToolDegradationFactor
        {
            get
            {
                float factor = toolDegradationFactor;

                // Apply hardcore mode 50% increase
                if (hardcoreMode)
                    factor *= 1.5f;

                // Apply extra hardcore mode additional 25% increase (total 87.5% increase)
                if (extraHardcoreMode)
                    factor *= 1.25f;

                return factor;
            }
        }

        /// <summary>
        /// Initialize the cache for optional tool availability (called once after game load)
        /// </summary>
        public void InitializeOptionalToolCache()
        {
            if (_cacheInitialized) return;

            _hasCleaningToolsCache = SurvivalToolUtility.ToolsExistForStat(ST_StatDefOf.CleaningSpeed);
            _hasButcheryToolsCache = SurvivalToolUtility.ToolsExistForStat(ST_StatDefOf.ButcheryFleshSpeed) ||
                                   SurvivalToolUtility.ToolsExistForStat(ST_StatDefOf.ButcheryFleshEfficiency);
            _hasMedicalToolsCache = SurvivalToolUtility.ToolsExistForStat(ST_StatDefOf.MedicalOperationSpeed) ||
                                  SurvivalToolUtility.ToolsExistForStat(ST_StatDefOf.MedicalSurgerySuccessChance);
            _cacheInitialized = true;

            if (SurvivalToolUtility.IsDebugLoggingEnabled)
                Log.Message($"[SurvivalTools.Settings] Optional tool cache initialized: Cleaning={_hasCleaningToolsCache}, Butchery={_hasButcheryToolsCache}, Medical={_hasMedicalToolsCache}");
        }

        /// <summary>
        /// Reset the cache (for mod loading/unloading scenarios)
        /// </summary>
        public void ResetOptionalToolCache()
        {
            _hasCleaningToolsCache = null;
            _hasButcheryToolsCache = null;
            _hasMedicalToolsCache = null;
            _cacheInitialized = false;
        }

        // Public accessors for cached availability
        public bool HasCleaningTools => _hasCleaningToolsCache ?? false;
        public bool HasButcheryTools => _hasButcheryToolsCache ?? false;
        public bool HasMedicalTools => _hasMedicalToolsCache ?? false;

        /// <summary>
        /// Whether any optional tool types are available (determines if extra hardcore option should be shown)
        /// </summary>
        public bool HasAnyOptionalTools => HasCleaningTools || HasButcheryTools || HasMedicalTools;

        /// <summary>
        /// Check if a specific stat should be required in extra hardcore mode
        /// </summary>
        public bool IsStatRequiredInExtraHardcore(StatDef stat)
        {
            if (!extraHardcoreMode) return false;

            if (stat == ST_StatDefOf.CleaningSpeed)
                return requireCleaningTools && HasCleaningTools;
            if (stat == ST_StatDefOf.ButcheryFleshSpeed || stat == ST_StatDefOf.ButcheryFleshEfficiency)
                return requireButcheryTools && HasButcheryTools;
            if (stat == ST_StatDefOf.MedicalOperationSpeed || stat == ST_StatDefOf.MedicalSurgerySuccessChance)
                return requireMedicalTools && HasMedicalTools;

            return false;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref hardcoreMode, nameof(hardcoreMode), false);
            Scribe_Values.Look(ref toolMapGen, nameof(toolMapGen), true);
            Scribe_Values.Look(ref toolLimit, nameof(toolLimit), true);
            Scribe_Values.Look(ref toolDegradationFactor, nameof(toolDegradationFactor), 1f);
            Scribe_Values.Look(ref toolOptimization, nameof(toolOptimization), true);
            Scribe_Values.Look(ref debugLogging, nameof(debugLogging), false);
            Scribe_Values.Look(ref pickupFromStorageOnly, nameof(pickupFromStorageOnly), false);
            Scribe_Values.Look(ref autoTool, nameof(autoTool), true);
            Scribe_Values.Look(ref noToolStatFactor, nameof(noToolStatFactor), 0.5f);
            Scribe_Values.Look(ref excludeTemporaryPawns, nameof(excludeTemporaryPawns), true);
            Scribe_Values.Look(ref allowPacifistEquip, nameof(allowPacifistEquip), true);

            // Extra Hardcore Mode settings
            Scribe_Values.Look(ref extraHardcoreMode, nameof(extraHardcoreMode), false);
            Scribe_Values.Look(ref requireCleaningTools, nameof(requireCleaningTools), true);
            Scribe_Values.Look(ref requireButcheryTools, nameof(requireButcheryTools), true);
            Scribe_Values.Look(ref requireMedicalTools, nameof(requireMedicalTools), true);

            base.ExposeData();
        }

        #region Settings Window
        public void DoSettingsWindowContents(Rect inRect)
        {
            var prevAnchor = Text.Anchor;
            var prevFont = Text.Font;
            var prevColor = GUI.color;
            try
            {
                var listing = new Listing_Standard();
                listing.Begin(inRect);

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;

                listing.Gap();
                if (Prefs.DevMode)
                {
                    bool debugLoggingBefore = debugLogging;
                    listing.CheckboxLabeled("Settings_DebugLogging".Translate(), ref debugLogging, "Settings_DebugLogging_Tooltip".Translate());
                    if (debugLogging != debugLoggingBefore)
                    {
                        SurvivalToolUtility.InvalidateDebugLoggingCache();
                    }
                    listing.Gap();
                }

                // Hardcore (red like Merciless)
                GUI.color = new Color(1f, 0.2f, 0.2f);
                listing.CheckboxLabeled("Settings_HardcoreMode".Translate(), ref hardcoreMode, "Settings_HardcoreMode_Tooltip".Translate());
                GUI.color = prevColor;

                // Extra Hardcore Mode (even redder, only shown if hardcore is enabled and optional tools exist)
                if (hardcoreMode && HasAnyOptionalTools)
                {
                    listing.Gap(6f);

                    // Indented extra hardcore checkbox
                    var extraHardcoreRect = listing.GetRect(Text.LineHeight);
                    extraHardcoreRect.x += 20f; // Indent to show it's a sub-option
                    extraHardcoreRect.width -= 20f;

                    GUI.color = new Color(1f, 0.1f, 0.1f); // Even more red
                    var prevExtraHardcore = extraHardcoreMode;
                    Widgets.CheckboxLabeled(extraHardcoreRect, "Settings_ExtraHardcoreMode".Translate(), ref extraHardcoreMode);

                    // Add tooltip for extra hardcore checkbox (always show, regardless of checked state)
                    if (Mouse.IsOver(extraHardcoreRect))
                        TooltipHandler.TipRegion(extraHardcoreRect, "Settings_ExtraHardcoreMode_Tooltip".Translate());

                    // Auto-enable individual options when extra hardcore is first enabled
                    if (!prevExtraHardcore && extraHardcoreMode)
                    {
                        requireCleaningTools = HasCleaningTools;
                        requireButcheryTools = HasButcheryTools;
                        requireMedicalTools = HasMedicalTools;
                    }

                    // Individual optional tool requirement checkboxes (only if extra hardcore is enabled)
                    if (extraHardcoreMode)
                    {
                        listing.Gap(4f);

                        // Further indented individual options
                        var individualRect = listing.GetRect(Text.LineHeight * 3 + 8f); // Space for 3 checkboxes
                        individualRect.x += 40f; // Double indent
                        individualRect.width -= 40f;

                        var individualListing = new Listing_Standard();
                        individualListing.Begin(individualRect);

                        if (HasCleaningTools)
                            individualListing.CheckboxLabeled("Settings_RequireCleaningTools".Translate(), ref requireCleaningTools, "Settings_RequireCleaningTools_Tooltip".Translate());

                        if (HasButcheryTools)
                            individualListing.CheckboxLabeled("Settings_RequireButcheryTools".Translate(), ref requireButcheryTools, "Settings_RequireButcheryTools_Tooltip".Translate());

                        if (HasMedicalTools)
                            individualListing.CheckboxLabeled("Settings_RequireMedicalTools".Translate(), ref requireMedicalTools, "Settings_RequireMedicalTools_Tooltip".Translate());

                        individualListing.End();
                    }

                    GUI.color = prevColor;
                }

                listing.Gap();
                listing.CheckboxLabeled("Settings_ToolMapGen".Translate(), ref toolMapGen, "Settings_ToolMapGen_Tooltip".Translate());
                listing.Gap();
                listing.CheckboxLabeled("Settings_ToolLimit".Translate(), ref toolLimit, "Settings_ToolLimit_Tooltip".Translate());
                listing.Gap();
                listing.CheckboxLabeled("Settings_PickupFromStorageOnly".Translate(), ref pickupFromStorageOnly, "Settings_PickupFromStorageOnly_Tooltip".Translate());
                listing.Gap();

                // No-tool penalty slider
                var noToolLabel = "Settings_NoToolPenalty".Translate();
                listing.Label(noToolLabel + ": " + noToolStatFactor.ToStringPercent());
                noToolStatFactor = listing.Slider(noToolStatFactor, 0f, 1f);
                noToolStatFactor = Mathf.Clamp(Mathf.Round(noToolStatFactor * 100f) / 100f, 0f, 1f);

                listing.Gap();
                // Degradation slider
                var degrLabel = "Settings_ToolDegradationRate".Translate();
                listing.Label(degrLabel + ": " + toolDegradationFactor.ToStringByStyle(ToStringStyle.FloatTwo, ToStringNumberSense.Factor));
                toolDegradationFactor = listing.Slider(toolDegradationFactor, 0f, 2f);
                toolDegradationFactor = Mathf.Clamp(Mathf.Round(toolDegradationFactor * 100f) / 100f, 0f, 2f);

                listing.Gap();
                listing.CheckboxLabeled("Settings_AutoTool".Translate(), ref autoTool, "Settings_AutoTool_Tooltip".Translate());
                listing.Gap();
                listing.CheckboxLabeled("Settings_ExcludeTemporaryPawns".Translate(), ref excludeTemporaryPawns, "Settings_ExcludeTemporaryPawns_Tooltip".Translate());
                listing.Gap();
                listing.CheckboxLabeled("Settings_AllowPacifistEquip".Translate(), ref allowPacifistEquip, "Settings_AllowPacifistEquip_Tooltip".Translate());
                listing.GapLine();

                listing.Gap(6f);


                listing.End();
            }
            finally
            {
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
                GUI.color = prevColor;
            }
        }
        #endregion
        #region Resets


        private void ResetAllToDefaults()
        {
            hardcoreMode = false;
            toolMapGen = true;
            toolLimit = true;
            toolDegradationFactor = 1f;
            toolOptimization = true;
            autoTool = true;
            debugLogging = false;
            pickupFromStorageOnly = false;
            noToolStatFactor = 0.5f;
            excludeTemporaryPawns = true;
            allowPacifistEquip = true;

            // Extra hardcore settings
            extraHardcoreMode = false;
            requireCleaningTools = true;
            requireButcheryTools = true;
            requireMedicalTools = true;
        }
        #endregion

    }
    #region Settings Handler
    public class SurvivalTools : Mod
    {
        public static SurvivalToolsSettings Settings;

        public SurvivalTools(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SurvivalToolsSettings>();
        }

        public override string SettingsCategory() => "SurvivalToolsSettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Ensure cache is initialized before showing settings
            Settings.InitializeOptionalToolCache();
            Settings.DoSettingsWindowContents(inRect);
        }

        /// <summary>
        /// Initialize settings cache (called from StaticConstructorClass)
        /// </summary>
        public static void InitializeSettings()
        {
            if (Settings != null)
            {
                Settings.InitializeOptionalToolCache();
            }
        }
    }
    #endregion
}
