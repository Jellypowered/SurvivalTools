// RimWorld 1.6 / C# 7.3
// Source/SurvivalToolsMod.cs (was SurvivalToolsSettings.cs before Mod class rename to avoid namespace collision)
using System;
using UnityEngine;
using Verse;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using SurvivalTools.Compat;
using SurvivalTools.Compat.ResearchReinvented;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;
using SurvivalTools.Compatibility.TreeStack;

namespace SurvivalTools
{
    public enum DifficultyMode
    {
        Normal,
        Hardcore,
        Nightmare
    }

    public class SurvivalToolsSettings : ModSettings
    {
        // UI Layout constants
        private const float JOB_LABEL_WIDTH = 140f;

        // Difficulty mode change event
        public static event System.Action DifficultyModeChanged;

        internal static void RaiseDifficultyChanged()
        {
            var h = DifficultyModeChanged;
            if (h != null) h();
        }

        public DifficultyMode CurrentMode
        {
            get
            {
                if (extraHardcoreMode) return DifficultyMode.Nightmare;
                if (hardcoreMode) return DifficultyMode.Hardcore;
                return DifficultyMode.Normal;
            }
        }

        public bool hardcoreMode;
        public bool toolMapGen = true;
        public bool toolLimit = true;
        public float toolDegradationFactor = 1f;
        // Legacy (Phase <=5) flags kept for save/backward compatibility; folded into enableAssignments.
        public bool toolOptimization = true; // legacy serialized field (do not remove)
        public bool autoTool = true; // legacy serialized field (do not remove)

        // Logging toggles (only effective in DEBUG builds)
        public bool debugLogging = false;
        public bool compatLogging = false;

        public bool pickupFromStorageOnly = false;
        public bool allowPacifistEquip = true;
        public bool extraHardcoreMode = false;

        // Normal mode penalty settings
        public float noToolStatFactorNormal = 0.4f;
        // Enable quality-based tool scaling (multiplies tool factors by quality curve)
        public bool useQualityToolScaling = true;
        public bool enableNormalModePenalties = true;

        // Tree felling system toggle
        public bool enableSurvivalToolTreeFelling = true;

        // Individual optional tool requirements
        public bool requireCleaningTools = true;
        public bool requireButcheryTools = true;
        public bool requireMedicalTools = true;

        // ResearchReinvented compatibility
        public bool enableRRCompatibility = true;
        public bool rrResearchRequiredInExtraHardcore = false;
        public bool rrFieldResearchRequiredInExtraHardcore = false;

        // QoL: upgrade suggestion toggle
        public bool showUpgradeSuggestions = true;

        // Visual feedback: denial/slow motes toggle
        public bool showDenialMotes = false; // When true, spawn text motes for blocked/slow tool-gated jobs

        // Gating alert settings  
        public bool showGatingAlert = true; // Show alert when pawns are blocked by tool gating
        public int toolGateAlertMinTicks = 1500; // Phase 8: minimum visibility duration for gating alert

        // Gating enforcer settings
        public bool enforceOnModeChange = true; // Cancel now-invalid jobs when difficulty changes

        // Phase 6: Assignment system settings
        public bool enableAssignments = true; // Enable pre-work auto-equip for better tools
        public float assignMinGainPct = 0.1f; // Minimum gain percentage to trigger assignment (10%)
        public float assignSearchRadius = 25f; // Maximum search radius for tool assignment
        public int assignPathCostBudget = 500; // Maximum path cost budget for tool retrieval
        public bool assignRescueOnGate = true; // Auto-assign any better tool when gated (rescue mode)
                                               // Right-click rescue float menu (Hardcore/Nightmare) toggle
        public bool enableRightClickRescue = true;

        // Phase 12: Powered tool battery system
        public bool enablePoweredTools = true; // Enable battery system for powered tools
        public bool enableNuclearHazards = false; // Enable nuclear battery explosion hazards
        public bool autoSwapBatteries = true; // Automatically swap batteries when low
        public float autoSwapThreshold = 0.15f; // Threshold for auto-swap (15% charge)

        // Cached availability of optional tool types
        private bool? _hasCleaningToolsCache = null;
        private bool? _hasButcheryToolsCache = null;
        private bool? _hasMedicalToolsCache = null;
        private bool _cacheInitialized = false;

        public bool ToolDegradationEnabled => toolDegradationFactor > 0.001f;
        public bool TreeFellingSystemEnabled => enableSurvivalToolTreeFelling;

        public float EffectiveToolDegradationFactor
        {
            get
            {
                float factor = toolDegradationFactor;
                if (hardcoreMode) factor *= 1.5f;
                if (extraHardcoreMode) factor *= 1.25f;
                return factor;
            }
        }

        public void InitializeOptionalToolCache()
        {
            if (_cacheInitialized) return;

            _hasCleaningToolsCache = SurvivalToolUtility.ToolsExistForStat(ST_StatDefOf.CleaningSpeed);
            _hasButcheryToolsCache = SurvivalToolUtility.ToolsExistForStat(ST_StatDefOf.ButcheryFleshSpeed) ||
                                     SurvivalToolUtility.ToolsExistForStat(ST_StatDefOf.ButcheryFleshEfficiency);
            _hasMedicalToolsCache = SurvivalToolUtility.ToolsExistForStat(ST_StatDefOf.MedicalOperationSpeed) ||
                                    SurvivalToolUtility.ToolsExistForStat(ST_StatDefOf.MedicalSurgerySuccessChance);
            _cacheInitialized = true;

            if (IsDebugLoggingEnabled)
                LogDebug($"[SurvivalTools] Optional tool cache initialized: Cleaning={_hasCleaningToolsCache}, Butchery={_hasButcheryToolsCache}, Medical={_hasMedicalToolsCache}", "Settings_OptionalToolCacheInitialized");
        }

        public void ResetOptionalToolCache()
        {
            _hasCleaningToolsCache = null;
            _hasButcheryToolsCache = null;
            _hasMedicalToolsCache = null;
            _cacheInitialized = false;
        }

        public bool HasCleaningTools => _hasCleaningToolsCache ?? false;
        public bool HasButcheryTools => _hasButcheryToolsCache ?? false;
        public bool HasMedicalTools => _hasMedicalToolsCache ?? false;
        public bool HasAnyOptionalTools => HasCleaningTools || HasButcheryTools || HasMedicalTools;

        public bool IsStatRequiredInExtraHardcore(StatDef stat)
        {
            if (!extraHardcoreMode) return false;

            if (stat == ST_StatDefOf.CleaningSpeed) return requireCleaningTools;
            if (stat == ST_StatDefOf.ButcheryFleshSpeed || stat == ST_StatDefOf.ButcheryFleshEfficiency)
                return requireButcheryTools && HasButcheryTools;
            if (stat == ST_StatDefOf.MedicalOperationSpeed || stat == ST_StatDefOf.MedicalSurgerySuccessChance)
                return requireMedicalTools && HasMedicalTools;

            if (RRHelpers.Settings.IsRRCompatibilityEnabled && RRHelpers.Settings.IsRRStatRequiredInExtraHardcore(stat))
                return true;

            return false;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref hardcoreMode, nameof(hardcoreMode), false);
            Scribe_Values.Look(ref toolMapGen, nameof(toolMapGen), true);
            Scribe_Values.Look(ref toolLimit, nameof(toolLimit), true);
            Scribe_Values.Look(ref toolDegradationFactor, nameof(toolDegradationFactor), 1f);
#pragma warning disable CS0612 // obsolete legacy fields (migration support)
            Scribe_Values.Look(ref toolOptimization, nameof(toolOptimization), true); // legacy
            Scribe_Values.Look(ref debugLogging, nameof(debugLogging), false);
            Scribe_Values.Look(ref compatLogging, nameof(compatLogging), false);
            Scribe_Values.Look(ref pickupFromStorageOnly, nameof(pickupFromStorageOnly), false);
            Scribe_Values.Look(ref allowPacifistEquip, nameof(allowPacifistEquip), true);
            Scribe_Values.Look(ref autoTool, nameof(autoTool), true); // legacy
#pragma warning restore CS0612
            Scribe_Values.Look(ref enableSurvivalToolTreeFelling, nameof(enableSurvivalToolTreeFelling), true);
            Scribe_Values.Look(ref extraHardcoreMode, nameof(extraHardcoreMode), false);
            Scribe_Values.Look(ref requireCleaningTools, nameof(requireCleaningTools), true);
            Scribe_Values.Look(ref requireButcheryTools, nameof(requireButcheryTools), true);
            Scribe_Values.Look(ref requireMedicalTools, nameof(requireMedicalTools), true);
            Scribe_Values.Look(ref noToolStatFactorNormal, nameof(noToolStatFactorNormal), 0.4f);
            Scribe_Values.Look(ref useQualityToolScaling, nameof(useQualityToolScaling), true);
            Scribe_Values.Look(ref enableNormalModePenalties, nameof(enableNormalModePenalties), true);
            Scribe_Values.Look(ref enableRRCompatibility, nameof(enableRRCompatibility), true);
            Scribe_Values.Look(ref rrResearchRequiredInExtraHardcore, nameof(rrResearchRequiredInExtraHardcore), false);
            Scribe_Values.Look(ref rrFieldResearchRequiredInExtraHardcore, nameof(rrFieldResearchRequiredInExtraHardcore), false);
            Scribe_Values.Look(ref showUpgradeSuggestions, nameof(showUpgradeSuggestions), true);
            Scribe_Values.Look(ref showDenialMotes, nameof(showDenialMotes), false);
            Scribe_Values.Look(ref showGatingAlert, nameof(showGatingAlert), true);
            Scribe_Values.Look(ref toolGateAlertMinTicks, nameof(toolGateAlertMinTicks), 1500);
            Scribe_Values.Look(ref enforceOnModeChange, nameof(enforceOnModeChange), true);
            Scribe_Values.Look(ref enableRightClickRescue, nameof(enableRightClickRescue), true);

            // Phase 12: Powered tools
            Scribe_Values.Look(ref enablePoweredTools, nameof(enablePoweredTools), true);
            Scribe_Values.Look(ref enableNuclearHazards, nameof(enableNuclearHazards), false);
            Scribe_Values.Look(ref autoSwapBatteries, nameof(autoSwapBatteries), true);
            Scribe_Values.Look(ref autoSwapThreshold, nameof(autoSwapThreshold), 0.15f);

            // Phase 11.10: workSpeedGlobalJobGating serialization removed

            // Initialize showGatingAlert based on mode if not set (first run or reset)
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Set default based on current mode - on for Hardcore/Nightmare, off for Normal
                if (!hardcoreMode && !extraHardcoreMode)
                {
                    showGatingAlert = false; // Default off in Normal mode
                }

                // Legacy migration: if enableAssignments not explicitly disabled but any legacy flags true, keep it enabled.
#pragma warning disable CS0612
                if (enableAssignments && (toolOptimization == false && autoTool == false))
                {
                    // Both legacy flags disabled -> treat as an opt-out request pre-refactor
                    enableAssignments = false;
                }
                else if (!enableAssignments && (toolOptimization || autoTool))
                {
                    // Legacy user had feature on; modern flag somehow off -> enable to honor intent
                    enableAssignments = true;
                }
#pragma warning restore CS0612
            }

            base.ExposeData();
        }

        #region Settings Window
        private Vector2 mainScrollPosition; // Add scroll position for main window

        public void DoSettingsWindowContents(Rect inRect)
        {
            var prevAnchor = Text.Anchor;
            var prevFont = Text.Font;
            var prevColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                // Reserve space at the bottom for the pinned Enhanced Settings button so it's always visible
                const float bottomReserved = 60f; // button height + padding

                // Calculate content height for scrolling
                float contentHeight = CalculateMainWindowContentHeight();

                // Scroll area (exclude reserved bottom area)
                var scrollRect = new Rect(inRect.x, inRect.y, inRect.width, Mathf.Max(0f, inRect.height - bottomReserved));

                // Inner view rect: width accounts for vertical scrollbar, height is at least the visible scroll area
                var viewRect = new Rect(0, 0, scrollRect.width - 20f, Mathf.Max(scrollRect.height, contentHeight));

                Widgets.BeginScrollView(scrollRect, ref mainScrollPosition, viewRect);
                var listing = new Listing_Standard();
                listing.Begin(viewRect);
                DrawSettingsContent(listing);
                listing.End();
                Widgets.EndScrollView();

                // Draw pinned Enhanced Settings button centered in the reserved bottom area
                var buttonWidth = 280f;
                var buttonHeight = 45f;
                var buttonArea = new Rect(inRect.x, inRect.yMax - bottomReserved, inRect.width, bottomReserved);
                var buttonRect = new Rect(
                    buttonArea.x + (buttonArea.width - buttonWidth) / 2f,
                    buttonArea.y + (buttonArea.height - buttonHeight) / 2f,
                    buttonWidth,
                    buttonHeight
                );

                var originalColor = GUI.color;
                GUI.color = new Color(1f, 0.3f, 0.3f); // Red color
                if (Widgets.ButtonText(buttonRect, "Settings_OpenEnhancedButton".Translate()))
                {
                    InitializeOptionalToolCache();
                    var existingWindow = Find.WindowStack.Windows.OfType<SurvivalToolsResizableSettingsWindow>().FirstOrDefault();
                    if (existingWindow != null)
                    {
                        Find.WindowStack.TryRemove(existingWindow);
                    }

                    var window = new SurvivalToolsResizableSettingsWindow(this);
                    Find.WindowStack.Add(window);
                }
                GUI.color = originalColor;
            }
            finally
            {
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
                GUI.color = prevColor;
            }
        }

        /// <summary>
        /// Calculate the total height needed for the main settings window content
        /// </summary>
        private float CalculateMainWindowContentHeight()
        {
            float height = 50f; // Base spacing

            // Basic Settings Section
            height += 35f; // Section header
            height += 25f * 7; // Checkboxes (hardcore mode, tools in ruins, tool limit, etc.)

            // Normal Mode Work Speed Settings
            height += 35f; // Section header  
            height += 80f; // Description text area
            height += 25f; // Enable penalties checkbox
            height += 40f; // Penalty slider
            height += 25f; // Tool degradation slider

            // Enhanced Settings Button
            height += 40f; // Button height + spacing

            // Debug Settings
            height += 35f; // Section header
            height += 50f; // Debug content area

            // Add much more padding to ensure all content is accessible
            height += 300f; // Increased from 100f to 300f for better scrolling

            return height;
        }

        /// <summary>
        /// Draw all the settings content in the scrollable area
        /// </summary>
        public void DrawSettingsContent(Listing_Standard listing)
        {
            var prevColor = GUI.color;
            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;

            // Basic Settings Section Header
            Text.Font = GameFont.Medium;
            GUI.color = Color.cyan;
            listing.Label("Settings_BasicSection".Translate());
            GUI.color = prevColor;
            Text.Font = prevFont;
            listing.Gap();

            // Hardcore Mode section
            GUI.color = new Color(1f, 0.2f, 0.2f);
            var prevHardcoreMode = hardcoreMode;
            var prevExtraHardcoreMode = extraHardcoreMode;
            listing.CheckboxLabeled("Settings_HardcoreMode".Translate(), ref hardcoreMode, "Settings_HardcoreMode_Tooltip".Translate());

            // Auto-disable Extra Hardcore Mode when Hardcore Mode is disabled
            if (prevHardcoreMode && !hardcoreMode && extraHardcoreMode)
            {
                extraHardcoreMode = false;
            }

            // Detect mode changes and fire event + enforcer
            var oldMode = prevExtraHardcoreMode ? DifficultyMode.Nightmare : prevHardcoreMode ? DifficultyMode.Hardcore : DifficultyMode.Normal;
            var newMode = CurrentMode;
            if (oldMode != newMode)
            {
                SurvivalToolsSettings.RaiseDifficultyChanged();
                if (enforceOnModeChange && newMode != DifficultyMode.Normal)
                {
                    try
                    {
                        var cancelled = Gating.GatingEnforcer.EnforceAllRunningJobs(true);
                        // Optional quiet enforcement - no messages by default
                    }
                    catch (System.Exception ex)
                    {
                        LogWarning($"[SurvivalTools] Gating enforcer failed on mode change: {ex.Message}");
                    }
                }
            }

            // Validate existing jobs when hardcore mode is enabled or changed
            if (!prevHardcoreMode && hardcoreMode)
            {
                SurvivalToolValidation.ValidateExistingJobs("Hardcore mode enabled");
            }

            GUI.color = prevColor;

            // General settings
            listing.CheckboxLabeled("Settings_ToolMapGen".Translate(), ref toolMapGen, "Settings_ToolMapGen_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_ToolLimit".Translate(), ref toolLimit, "Settings_ToolLimit_Tooltip".Translate());
            // Unified modern toggle: enableAssignments (legacy flags displayed read-only if differing)
            bool assignToggle = enableAssignments;
            listing.CheckboxLabeled("Settings_EnableAssignments".Translate(), ref assignToggle, "Settings_EnableAssignments_Tooltip".Translate());
            if (assignToggle != enableAssignments)
            {
                enableAssignments = assignToggle;
#pragma warning disable CS0612
                toolOptimization = assignToggle;
                autoTool = assignToggle;
#pragma warning restore CS0612
            }
#pragma warning disable CS0612
            if (toolOptimization != enableAssignments || autoTool != enableAssignments)
            {
                listing.Label("(Legacy flags: toolOptimization=" + toolOptimization + ", autoTool=" + autoTool + ")");
            }
#pragma warning restore CS0612
            // Tree felling toggle (force disabled when Separate Tree Chopping owns authority)
            if (TreeSystemArbiter.Authority == TreeAuthority.SeparateTreeChopping)
            {
                if (enableSurvivalToolTreeFelling) enableSurvivalToolTreeFelling = false; // hard override regardless of saved preference
                var savedColorTF = GUI.color;
                GUI.color = Color.gray;
                var label = "Settings_EnableTreeFelling".Translate();
                listing.Label(label + " (STC override)");
                Text.Font = GameFont.Tiny;
                listing.Label("Separate Tree Chopping detected – internal tree felling system disabled.");
                Text.Font = GameFont.Small;
                GUI.color = savedColorTF;
            }
            else
            {
                listing.CheckboxLabeled("Settings_EnableTreeFelling".Translate(), ref enableSurvivalToolTreeFelling, "Settings_EnableTreeFelling_Tooltip".Translate());
            }
            listing.CheckboxLabeled("Settings_PickupFromStorageOnly".Translate(), ref pickupFromStorageOnly, "Settings_PickupFromStorageOnly_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_AllowPacifistEquip".Translate(), ref allowPacifistEquip, "Settings_AllowPacifistEquip_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_ShowUpgradeSuggestions".Translate(), ref showUpgradeSuggestions, "Settings_ShowUpgradeSuggestions_Tooltip".Translate());
            //listing.CheckboxLabeled("Settings_ShowDenialMotes".Translate(), ref showDenialMotes, "Settings_ShowDenialMotes_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_ShowGatingAlert".Translate(), ref showGatingAlert, "Settings_ShowGatingAlert_Tooltip".Translate());
            if (showGatingAlert)
            {
                // Sticky alert minimum visibility slider (0–5000 ticks)
                int prev = toolGateAlertMinTicks;
                var label = "Settings_ToolGateAlertMinTicks".Translate();
                listing.Label($"{label}: {toolGateAlertMinTicks} ticks (~{(toolGateAlertMinTicks / 60f):F1}s)");
                float sliderVal = listing.Slider(toolGateAlertMinTicks, 0f, 5000f);
                toolGateAlertMinTicks = Mathf.Clamp(Mathf.RoundToInt(sliderVal / 50f) * 50, 0, 5000); // snap to 50‑tick increments for stability
                if (toolGateAlertMinTicks != prev && IsDebugLoggingEnabled)
                {
                    LogDebug($"[SurvivalTools] toolGateAlertMinTicks changed -> {toolGateAlertMinTicks}", "Settings.Change.AlertMinTicks");
                }
                // Helper/tooltip style small text
                GUI.color = Color.gray; Text.Font = GameFont.Tiny;
                listing.Label("Settings_ToolGateAlertMinTicks_Tooltip".Translate());
                GUI.color = prevColor; Text.Font = GameFont.Small;
            }
            listing.CheckboxLabeled("Settings_EnforceOnModeChange".Translate(), ref enforceOnModeChange, "Settings_EnforceOnModeChange_Tooltip".Translate());
            // Right-click rescue toggle (only relevant / shown during Hardcore or Nightmare)
            if (hardcoreMode || extraHardcoreMode)
            {
                listing.CheckboxLabeled("Settings_EnableRightClickRescue".Translate(), ref enableRightClickRescue, "Settings_EnableRightClickRescue_Tooltip".Translate());
            }
            listing.Gap();

            // Normal Mode Penalty Settings
            listing.GapLine();
            Text.Font = GameFont.Medium;
            GUI.color = Color.cyan;
            listing.Label("Settings_NormalModeSection".Translate());
            GUI.color = prevColor;
            Text.Font = prevFont;

            // Explanation section
            GUI.color = new Color(0.8f, 0.9f, 1f); // Light blue background
            Text.Font = GameFont.Tiny;
            var explanationRect = listing.GetRect(60f);
            Widgets.DrawBoxSolid(explanationRect, new Color(0.1f, 0.1f, 0.2f, 0.3f));
            var textRect = explanationRect.ContractedBy(6f);
            Widgets.Label(textRect, "Settings_NormalMode_Explanation".Translate());
            GUI.color = prevColor;
            Text.Font = prevFont;
            listing.Gap(4f);

            // Enable/disable penalties entirely
            listing.CheckboxLabeled("Settings_EnableNormalModePenalties".Translate(), ref enableNormalModePenalties,
                "Settings_EnableNormalModePenalties_Tooltip".Translate());

            // Penalty severity slider (only show when penalties are enabled)
            if (enableNormalModePenalties)
            {
                var penaltyLabel = "Settings_NormalModeToolPenalty".Translate();
                var penaltyPercent = (1f - noToolStatFactorNormal) * 100f;
                listing.Label($"{penaltyLabel}: {penaltyPercent:F0}% slower without tools");
                noToolStatFactorNormal = 1f - (listing.Slider(1f - noToolStatFactorNormal, 0f, 0.8f));
                noToolStatFactorNormal = Mathf.Clamp(Mathf.Round(noToolStatFactorNormal * 100f) / 100f, 0.2f, 1f);

                // Helper text
                GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                listing.Label("Settings_NormalModePenalties_HelperText".Translate());
                GUI.color = prevColor;
                Text.Font = prevFont;
            }
            else
            {
                // Show what this means when disabled
                GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                listing.Label("Settings_NormalModePenalties_Disabled".Translate());
                GUI.color = prevColor;
                Text.Font = prevFont;
            }
            listing.Gap();

            // Quality-based tool scaling toggle
            listing.CheckboxLabeled("Settings_QualityToolScaling".Translate(), ref useQualityToolScaling, "Settings_QualityToolScaling_Tooltip".Translate());

            // Degradation slider
            var degrLabel = "Settings_ToolDegradationRate".Translate();
            listing.Label(degrLabel + ": " + toolDegradationFactor.ToStringByStyle(ToStringStyle.FloatTwo, ToStringNumberSense.Factor));
            toolDegradationFactor = listing.Slider(toolDegradationFactor, 0f, 2f);
            toolDegradationFactor = Mathf.Clamp(Mathf.Round(toolDegradationFactor * 100f) / 100f, 0f, 2f);
            listing.Gap();

            // Debug logging section (Dev mode only)
            if (Prefs.DevMode)
            {
                listing.GapLine();
                Text.Font = GameFont.Medium;
                GUI.color = Color.yellow;
                listing.Label("Settings_DebugSection".Translate());
                GUI.color = prevColor;
                Text.Font = prevFont;

                bool debugLoggingBefore = debugLogging;
                listing.CheckboxLabeled("Settings_DebugLogging".Translate(), ref debugLogging, "Settings_DebugLogging_Tooltip".Translate());
                if (debugLogging != debugLoggingBefore)
                {
                    InvalidateDebugLoggingCache();
                }

                // Show compatibility logging option only when debug logging is enabled
                if (debugLogging)
                {
                    listing.CheckboxLabeled("Settings_CompatLogging".Translate(), ref compatLogging, "Settings_CompatLogging_Tooltip".Translate());
                }
            }

            listing.GapLine();
            listing.Gap(6f);

            // Restore original styling (button is pinned below the scroll area)
            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
            GUI.color = prevColor;
        }

        #endregion Settings Window

        // Phase 11.10: WorkSpeedGlobal category removed - no longer gating those jobs

        #region Resets


        private void ResetAllToDefaults()
        {
            hardcoreMode = false;
            toolMapGen = true;
            toolLimit = true;
            toolDegradationFactor = 1f;
#pragma warning disable CS0612
            toolOptimization = true;
            autoTool = true;
#pragma warning restore CS0612
            debugLogging = false;

            // Extra hardcore settings
            extraHardcoreMode = false;
            requireCleaningTools = true;
            requireButcheryTools = true;
            requireMedicalTools = true;
        }
        #endregion

    }
    #region Settings Handler
    public class SurvivalToolsMod : Mod
    {
        public static SurvivalToolsMod Instance { get; private set; }
        public static SurvivalToolsSettings Settings => Instance?._settings;
        private readonly SurvivalToolsSettings _settings;

        public SurvivalToolsMod(ModContentPack content) : base(content)
        {
            Instance = this;
            _settings = GetSettings<SurvivalToolsSettings>();
        }

        public override string SettingsCategory() => "SurvivalToolsSettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Ensure we have an instance (paranoia)
            if (Instance == null)
            {
                Instance = this; // Paranoia safeguard
            }

            // If your basic/enhanced UI needs the caches, prime them here
            _settings.InitializeOptionalToolCache();

            // Draw the real settings UI (the one with checkboxes + “Open Enhanced Settings” button)
            _settings.DoSettingsWindowContents(inRect);
        }

        /// <summary>
        /// Initialize settings cache (called from StaticConstructorClass)
        /// </summary>
        public static void InitializeSettings() => Settings?.InitializeOptionalToolCache();
    }
    #endregion

    /// <summary>
    /// Enhanced settings window with scrollable content and improved job table
    /// </summary>
    public class SurvivalToolsResizableSettingsWindow : Window
    {
        private readonly SurvivalToolsSettings settings;
        private Vector2 scrollPosition = Vector2.zero;
        private Vector2 enhancedScrollPosition = Vector2.zero; // For job table scrolling

        // Window size - fixed, no resizing
        private readonly Vector2 windowSize = new Vector2(800f, 700f);

        public SurvivalToolsResizableSettingsWindow(SurvivalToolsSettings settings)
        {
            this.settings = settings;

            // Window properties
            doCloseButton = true;
            doCloseX = true;
            draggable = true;
            resizeable = false;

            // Set window size and position
            windowRect = new Rect(
                (Verse.UI.screenWidth - windowSize.x) / 2f,
                (Verse.UI.screenHeight - windowSize.y) / 2f,
                windowSize.x,
                windowSize.y
            );
        }

        public override Vector2 InitialSize => windowSize;

        protected override float Margin => 20f;

        public override void PreClose()
        {
            // Save settings when window closes
            settings.Write();

            // Apply conditional feature registration based on updated settings
            ConditionalRegistration.ResetConditionals();
            ConditionalRegistration.ApplyTreeFellingConditionals();

            base.PreClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            var prevAnchor = Text.Anchor;
            var prevFont = Text.Font;
            var prevColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;

                // Content area (no title, starts at top)
                var contentRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);

                // Calculate dynamic content height
                float contentHeight = CalculateDynamicContentHeight(contentRect.width);

                // Ensure the inner view rect height is at least the visible view height so bottom controls remain reachable
                var viewRect = new Rect(0, 0, contentRect.width - 20f, Mathf.Max(contentRect.height, contentHeight));

                Widgets.BeginScrollView(contentRect, ref scrollPosition, viewRect);

                var listing = new Listing_Standard();
                listing.Begin(viewRect);

                // Draw all settings content
                DrawDynamicSettingsContent(listing, contentRect.width);

                listing.End();
                Widgets.EndScrollView();
            }
            finally
            {
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
                GUI.color = prevColor;
            }
        }

        private float CalculateDynamicContentHeight(float availableWidth)
        {
            // Calculate content height for enhanced window (extra hardcore settings only)
            // Phase 11.10: Job table removed with WorkSpeedGlobal system
            // This ensures content scales properly with window size

            float baseHeight = 50f; // Base spacing (reduced since no title area)

            // Pacifist equipping checkbox
            baseHeight += 30f;

            // Extra hardcore settings (when enabled and applicable)
            if (settings.hardcoreMode && settings.HasAnyOptionalTools)
            {
                baseHeight += 35f; // Extra hardcore mode checkbox

                if (settings.extraHardcoreMode)
                {
                    // Individual optional tool requirement checkboxes
                    if (settings.HasCleaningTools) baseHeight += 30f;
                    if (settings.HasButcheryTools) baseHeight += 30f;
                    if (settings.HasMedicalTools) baseHeight += 30f;

                    // Research Reinvented compatibility (if detected)
                    if (RRHelpers.IsRRActive)
                    {
                        baseHeight += 60f; // RR section header + enable checkbox
                        if (settings.enableRRCompatibility)
                        {
                            baseHeight += 60f; // Two RR option checkboxes
                        }
                    }
                }
            }

            // Add extra space for the Close button at the bottom (RimWorld automatic close button)
            baseHeight += 80f; // Space for close button + padding

            return baseHeight + 100f; // Extra padding for scrolling comfort
        }

        private void DrawDynamicSettingsContent(Listing_Standard listing, float availableWidth)
        {
            var prevColor = GUI.color;

            listing.Gap();

            // Pacifist Tool Equipping
            GUI.color = Color.white;
            listing.CheckboxLabeled("Settings_AllowPacifistEquip".Translate(), ref settings.allowPacifistEquip, "Settings_AllowPacifistEquip_Tooltip".Translate());

            listing.Gap();

            // Extra Hardcore Mode section (only shown if hardcore is enabled and optional tools exist)
            if (settings.hardcoreMode && settings.HasAnyOptionalTools)
            {
                // Extra hardcore checkbox
                GUI.color = new Color(1f, 0.1f, 0.1f); // Even more red than hardcore
                var prevExtraHardcore = settings.extraHardcoreMode;
                listing.CheckboxLabeled("Settings_ExtraHardcoreMode".Translate(), ref settings.extraHardcoreMode, "Settings_ExtraHardcoreMode_Tooltip".Translate());

                // Auto-enable individual options when extra hardcore is first enabled
                if (!prevExtraHardcore && settings.extraHardcoreMode)
                {
                    settings.requireCleaningTools = settings.HasCleaningTools;
                    settings.requireButcheryTools = settings.HasButcheryTools;
                    settings.requireMedicalTools = settings.HasMedicalTools;
                    Helpers.SurvivalToolValidation.ValidateExistingJobs("Extra hardcore mode enabled");
                }

                // Detect extra hardcore mode changes and fire enforcer
                if (prevExtraHardcore != settings.extraHardcoreMode)
                {
                    var newMode = settings.CurrentMode;
                    SurvivalToolsSettings.RaiseDifficultyChanged();
                    if (settings.enforceOnModeChange && newMode != DifficultyMode.Normal)
                    {
                        try
                        {
                            var cancelled = Gating.GatingEnforcer.EnforceAllRunningJobs(true);
                            // Optional quiet enforcement - no messages by default
                        }
                        catch (System.Exception ex)
                        {
                            LogWarning($"[SurvivalTools] Gating enforcer failed on extra hardcore mode change: {ex.Message}");
                        }
                    }
                }

                // Individual optional tool requirement checkboxes (only if extra hardcore is enabled)
                if (settings.extraHardcoreMode)
                {
                    listing.Gap(4f);

                    // Indented individual options
                    if (settings.HasCleaningTools)
                    {
                        var cleaningRect = listing.GetRect(Text.LineHeight);
                        cleaningRect.x += 20f;
                        cleaningRect.width -= 20f;
                        Widgets.CheckboxLabeled(cleaningRect, "Settings_RequireCleaningTools".Translate(), ref settings.requireCleaningTools);
                        if (Mouse.IsOver(cleaningRect))
                            TooltipHandler.TipRegion(cleaningRect, "Settings_RequireCleaningTools_Tooltip".Translate());
                    }

                    if (settings.HasButcheryTools)
                    {
                        var butcheryRect = listing.GetRect(Text.LineHeight);
                        butcheryRect.x += 20f;
                        butcheryRect.width -= 20f;
                        Widgets.CheckboxLabeled(butcheryRect, "Settings_RequireButcheryTools".Translate(), ref settings.requireButcheryTools);
                        if (Mouse.IsOver(butcheryRect))
                            TooltipHandler.TipRegion(butcheryRect, "Settings_RequireButcheryTools_Tooltip".Translate());
                    }

                    if (settings.HasMedicalTools)
                    {
                        var medicalRect = listing.GetRect(Text.LineHeight);
                        medicalRect.x += 20f;
                        medicalRect.width -= 20f;
                        Widgets.CheckboxLabeled(medicalRect, "Settings_RequireMedicalTools".Translate(), ref settings.requireMedicalTools);
                        if (Mouse.IsOver(medicalRect))
                            TooltipHandler.TipRegion(medicalRect, "Settings_RequireMedicalTools_Tooltip".Translate());
                    }

                    // ResearchReinvented compatibility settings (only show if RR is detected)
                    if (RRHelpers.IsRRActive)
                    {
                        listing.Gap(6f);
                        listing.Label("Research Reinvented Compatibility");

                        var rrEnableRect = listing.GetRect(Text.LineHeight);
                        rrEnableRect.x += 20f;
                        rrEnableRect.width -= 20f;
                        Widgets.CheckboxLabeled(rrEnableRect, "Enable Research Reinvented compatibility", ref settings.enableRRCompatibility);
                        if (Mouse.IsOver(rrEnableRect))
                            TooltipHandler.TipRegion(rrEnableRect, "When enabled, integrates with Research Reinvented mod features and tool requirements.");

                        if (settings.enableRRCompatibility)
                        {
                            var rrResearchRect = listing.GetRect(Text.LineHeight);
                            rrResearchRect.x += 40f;
                            rrResearchRect.width -= 40f;
                            Widgets.CheckboxLabeled(rrResearchRect, "Require research tools in Extra Hardcore", ref settings.rrResearchRequiredInExtraHardcore);
                            if (Mouse.IsOver(rrResearchRect))
                                TooltipHandler.TipRegion(rrResearchRect, "When enabled, pawns need research tools to perform research tasks in Extra Hardcore mode.");

                            var rrFieldResearchRect = listing.GetRect(Text.LineHeight);
                            rrFieldResearchRect.x += 40f;
                            rrFieldResearchRect.width -= 40f;
                            Widgets.CheckboxLabeled(rrFieldResearchRect, "Require field research tools in Extra Hardcore", ref settings.rrFieldResearchRequiredInExtraHardcore);
                            if (Mouse.IsOver(rrFieldResearchRect))
                                TooltipHandler.TipRegion(rrFieldResearchRect, "When enabled, pawns need field research tools to perform field research tasks in Extra Hardcore mode.");
                        }
                    }
                }

                GUI.color = prevColor;
                listing.GapLine();
                listing.Gap(12f);

                // Phase 12: Powered tools settings
                listing.Label("Powered Tools (Battery System)");
                listing.Gap(4f);

                var poweredToolsRect = listing.GetRect(Text.LineHeight);
                Widgets.CheckboxLabeled(poweredToolsRect, "Enable powered tools battery system", ref settings.enablePoweredTools);
                if (Mouse.IsOver(poweredToolsRect))
                    TooltipHandler.TipRegion(poweredToolsRect, "When enabled, powered tools (drills, etc.) use batteries. Empty tools function at reduced efficiency and don't satisfy required checks in Hardcore/Nightmare.");

                if (settings.enablePoweredTools)
                {
                    var autoSwapRect = listing.GetRect(Text.LineHeight);
                    autoSwapRect.x += 20f;
                    autoSwapRect.width -= 20f;
                    Widgets.CheckboxLabeled(autoSwapRect, "Auto-swap batteries when low", ref settings.autoSwapBatteries);
                    if (Mouse.IsOver(autoSwapRect))
                        TooltipHandler.TipRegion(autoSwapRect, "Automatically swap to a charged battery when current battery drops below threshold.");

                    if (settings.autoSwapBatteries)
                    {
                        // Threshold slider
                        var thresholdRect = listing.GetRect(Text.LineHeight * 2);
                        thresholdRect.x += 40f;
                        thresholdRect.width -= 40f;

                        Rect labelRect = new Rect(thresholdRect.x, thresholdRect.y, thresholdRect.width, Text.LineHeight);
                        Widgets.Label(labelRect, $"Auto-swap threshold: {settings.autoSwapThreshold.ToStringPercent()}");

                        Rect sliderRect = new Rect(thresholdRect.x, thresholdRect.y + Text.LineHeight, thresholdRect.width, Text.LineHeight);
                        settings.autoSwapThreshold = Widgets.HorizontalSlider(sliderRect, settings.autoSwapThreshold, 0.05f, 0.5f, true);
                        settings.autoSwapThreshold = UnityEngine.Mathf.Round(settings.autoSwapThreshold * 20f) / 20f; // Round to 5% increments
                    }

                    var nuclearHazardRect = listing.GetRect(Text.LineHeight);
                    nuclearHazardRect.x += 20f;
                    nuclearHazardRect.width -= 20f;
                    Widgets.CheckboxLabeled(nuclearHazardRect, "Enable nuclear battery hazards", ref settings.enableNuclearHazards);
                    if (Mouse.IsOver(nuclearHazardRect))
                        TooltipHandler.TipRegion(nuclearHazardRect, "When enabled, destroyed nuclear batteries create small explosions.");
                }

                listing.GapLine();
                listing.Gap(12f);
            }

            // Add some spacing after the job table to prevent Close button overlap
            listing.Gap();
            listing.Gap();
            listing.Gap();
        }
    }
}
