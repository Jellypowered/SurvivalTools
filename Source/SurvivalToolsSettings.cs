// Rimworld 1.6 / C# 7.3
// Source/SurvivalToolsSettings.cs
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
        public bool toolOptimization = true;
        public bool autoTool = true;

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
        public bool showDenialMotes = true; // When true, spawn text motes for blocked/slow tool-gated jobs

        // Gating alert settings  
        public bool showGatingAlert = true; // Show alert when pawns are blocked by tool gating

        // Gating enforcer settings
        public bool enforceOnModeChange = true; // Cancel now-invalid jobs when difficulty changes

        // Job gating
        public Dictionary<string, bool> workSpeedGlobalJobGating = new Dictionary<string, bool>();

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
                LogDebug($"[SurvivalTools.Settings] Optional tool cache initialized: Cleaning={_hasCleaningToolsCache}, Butchery={_hasButcheryToolsCache}, Medical={_hasMedicalToolsCache}", "Settings_OptionalToolCacheInitialized");
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
            Scribe_Values.Look(ref toolOptimization, nameof(toolOptimization), true);
            Scribe_Values.Look(ref debugLogging, nameof(debugLogging), false);
            Scribe_Values.Look(ref compatLogging, nameof(compatLogging), false);
            Scribe_Values.Look(ref pickupFromStorageOnly, nameof(pickupFromStorageOnly), false);
            Scribe_Values.Look(ref allowPacifistEquip, nameof(allowPacifistEquip), true);
            Scribe_Values.Look(ref autoTool, nameof(autoTool), true);
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
            Scribe_Values.Look(ref showDenialMotes, nameof(showDenialMotes), true);
            Scribe_Values.Look(ref showGatingAlert, nameof(showGatingAlert), true);
            Scribe_Values.Look(ref enforceOnModeChange, nameof(enforceOnModeChange), true);

            Scribe_Collections.Look(ref workSpeedGlobalJobGating, nameof(workSpeedGlobalJobGating), LookMode.Value, LookMode.Value);
            if (workSpeedGlobalJobGating == null)
                workSpeedGlobalJobGating = new Dictionary<string, bool>();

            // Initialize showGatingAlert based on mode if not set (first run or reset)
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Set default based on current mode - on for Hardcore/Nightmare, off for Normal
                if (!hardcoreMode && !extraHardcoreMode)
                {
                    showGatingAlert = false; // Default off in Normal mode
                }
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

            // Add some extra padding for safety
            height += 100f;

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
                        Log.Warning($"[SurvivalTools] Gating enforcer failed on mode change: {ex.Message}");
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
            listing.CheckboxLabeled("Settings_AutoTool".Translate(), ref autoTool, "Settings_AutoTool_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_ToolOptimization".Translate(), ref toolOptimization, "Settings_ToolOptimization_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_EnableTreeFelling".Translate(), ref enableSurvivalToolTreeFelling, "Settings_EnableTreeFelling_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_PickupFromStorageOnly".Translate(), ref pickupFromStorageOnly, "Settings_PickupFromStorageOnly_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_AllowPacifistEquip".Translate(), ref allowPacifistEquip, "Settings_AllowPacifistEquip_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_ShowUpgradeSuggestions".Translate(), ref showUpgradeSuggestions, "Settings_ShowUpgradeSuggestions_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_ShowDenialMotes".Translate(), ref showDenialMotes, "Settings_ShowDenialMotes_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_ShowGatingAlert".Translate(), ref showGatingAlert, "Settings_ShowGatingAlert_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_EnforceOnModeChange".Translate(), ref enforceOnModeChange, "Settings_EnforceOnModeChange_Tooltip".Translate());
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

        /// <summary>
        /// Draw button to open WorkSpeedGlobal job configuration window
        /// </summary>
        public void DrawWorkSpeedGlobalConfigButton(Listing_Standard listing)
        {
            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;
            var prevColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            listing.Gap(6f);

            // Create button rect
            const float buttonHeight = 30f;
            const float buttonWidth = 250f;
            var buttonRect = new Rect(
                (listing.ColumnWidth - buttonWidth) / 2f, // Center horizontally
                listing.CurHeight,
                buttonWidth,
                buttonHeight
            );

            // Draw blue WorkSpeedGlobal config button
            GUI.color = new Color(0.3f, 0.5f, 1f); // Blue color

            if (Widgets.ButtonText(buttonRect, "WorkSpeedGlobal_OpenConfigButton".Translate()))
            {
                // Close any existing WorkSpeedGlobal window first
                var existingWindow = Find.WindowStack.Windows.OfType<WorkSpeedGlobalConfigWindow>().FirstOrDefault();
                if (existingWindow != null)
                {
                    Find.WindowStack.TryRemove(existingWindow);
                }

                var window = new WorkSpeedGlobalConfigWindow(this);
                Find.WindowStack.Add(window);
            }

            GUI.color = prevColor;

            // Advance listing position manually since we drew outside of listing
            listing.Gap(buttonHeight + 6f);

            // Restore original styling
            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
        }

        /// <summary>
        /// Draws a display showing how different jobs behave in the current mode
        /// </summary>
        private void DrawJobStatusDisplay(Listing_Standard listing)
        {
            // Header
            var headerFont = GameFont.Medium;
            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;

            Text.Font = headerFont;
            Text.Anchor = TextAnchor.MiddleCenter;

            // Create a rect for the centered header with tooltip
            var titleRect = listing.GetRect(Text.LineHeight + 4f);
            var headerText = "JobTable_SectionTitle".Translate();
            Widgets.Label(titleRect, headerText);

            // Add tooltip to the header
            if (Mouse.IsOver(titleRect))
                TooltipHandler.TipRegion(titleRect, "JobTable_SectionTooltip".Translate());

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;

            listing.Gap(6f);

            // Get all survival tool work givers and their stats
            var jobCategories = GetJobCategories();

            if (jobCategories.Count == 0)
            {
                listing.Label("JobTable_NoJobsFound".Translate());
                return;
            }

            // Determine which mode(s) to show based on current settings
            var showNormal = !hardcoreMode && !extraHardcoreMode;
            var showHardcore = hardcoreMode && !extraHardcoreMode;
            var showExtraHardcore = extraHardcoreMode;

            // Calculate number of active columns and their widths
            var activeColumns = 0;
            if (showNormal) activeColumns++;
            if (showHardcore) activeColumns++;
            if (showExtraHardcore) activeColumns++;

            if (activeColumns == 0)
            {
                listing.Label("JobTable_NoActiveMode".Translate());
                return;
            }

            const float columnSpacing = 12f; // Increased spacing between columns
            const float minColumnWidth = 200f; // Increased significantly to provide much more room for content

            // Calculate dynamic job label width based on actual content
            var measureFont = Text.Font;
            Text.Font = GameFont.Medium; // Use same font as rendering

            var jobLabelText = "JobTable_JobType".Translate();
            var dynamicJobLabelWidth = Mathf.Max(Text.CalcSize(jobLabelText).x + 50f, 180f); // Increased padding and minimum significantly

            // Check all job categories for max width needed
            foreach (var category in jobCategories)
            {
                dynamicJobLabelWidth = Mathf.Max(dynamicJobLabelWidth, Text.CalcSize(category.Name).x + 50f);
            }

            // Also check the column headers to ensure they fit with more generous padding
            var extraHardcoreHeaderWidth = Text.CalcSize("JobTable_ExtraHardcore".Translate()).x + 80f; // Increased padding significantly
            var requiredTextWidth = Text.CalcSize("JobTable_Required".Translate()).x + 80f;
            var gatedTextWidth = Text.CalcSize("JobTable_GatedExample".Translate()).x + 80f; // Account for worst case gated text with extra padding
            var minNeededColumnWidth = Mathf.Max(extraHardcoreHeaderWidth, Mathf.Max(requiredTextWidth, gatedTextWidth));

            Text.Font = measureFont; // Restore font

            var availableWidth = listing.ColumnWidth - dynamicJobLabelWidth;
            var calculatedColumnWidth = activeColumns == 1 ? availableWidth :
                              (availableWidth - (columnSpacing * (activeColumns - 1))) / activeColumns;

            // Ensure column width is sufficient for the text content - use the larger of calculated or needed width
            var columnWidth = Mathf.Max(calculatedColumnWidth, Mathf.Max(minColumnWidth, minNeededColumnWidth));

            // Calculate total table height for border
            var tableHeight = (Text.LineHeight + 10f) + (Text.LineHeight * 1.5f * jobCategories.Count) + 8f; // Header + rows with bigger font spacing + padding
            var tableBorderRect = listing.GetRect(tableHeight);

            // Draw outer table border
            var tableBorderColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
            var borderThickness = 2f;
            var savedColor = GUI.color;

            // Draw border frame
            GUI.color = tableBorderColor;
            // Top border
            GUI.DrawTexture(new Rect(tableBorderRect.x, tableBorderRect.y, tableBorderRect.width, borderThickness), BaseContent.WhiteTex);
            // Bottom border
            GUI.DrawTexture(new Rect(tableBorderRect.x, tableBorderRect.yMax - borderThickness, tableBorderRect.width, borderThickness), BaseContent.WhiteTex);
            // Left border
            GUI.DrawTexture(new Rect(tableBorderRect.x, tableBorderRect.y, borderThickness, tableBorderRect.height), BaseContent.WhiteTex);
            // Right border
            GUI.DrawTexture(new Rect(tableBorderRect.xMax - borderThickness, tableBorderRect.y, borderThickness, tableBorderRect.height), BaseContent.WhiteTex);

            // Create inner content area (inside the border)
            var innerTableRect = new Rect(tableBorderRect.x + borderThickness + 4f, tableBorderRect.y + borderThickness + 4f,
                                        tableBorderRect.width - (borderThickness * 2) - 8f, tableBorderRect.height - (borderThickness * 2) - 8f);

            // Column headers with enhanced styling - increased height for better fonts
            var headerRect = new Rect(innerTableRect.x, innerTableRect.y, innerTableRect.width, Text.LineHeight + 8f); // Increased from +4f to +8f

            // Draw header background
            var headerBgColor = new Color(0.1f, 0.1f, 0.1f, 0.6f); // Dark background for header
            var originalColor = GUI.color;
            GUI.color = headerBgColor;
            GUI.DrawTexture(headerRect, BaseContent.WhiteTex);

            // Draw header bottom border
            var borderRect = new Rect(headerRect.x, headerRect.yMax - 2f, headerRect.width, 2f);
            var borderColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
            GUI.color = borderColor;
            GUI.DrawTexture(borderRect, BaseContent.WhiteTex);

            var currentX = headerRect.x + dynamicJobLabelWidth; // Offset for job labels

            // Job Types label
            var jobHeaderRect = new Rect(headerRect.x, headerRect.y + 2f, dynamicJobLabelWidth, headerRect.height - 4f);
            GUI.color = Color.white;
            var currentFont = Text.Font;
            Text.Font = GameFont.Small; // Changed from Tiny to Small for better visibility
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(jobHeaderRect, "JobTable_JobTypes".Translate());
            Text.Font = currentFont;
            Text.Anchor = TextAnchor.MiddleLeft;

            if (showNormal)
            {
                var normalRect = new Rect(currentX, headerRect.y + 2f, columnWidth, headerRect.height - 4f);
                GUI.color = Color.green;
                Text.Font = GameFont.Small; // Ensure consistent font size
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(normalRect, "JobTable_NormalMode".Translate());
                Text.Font = currentFont;
                Text.Anchor = TextAnchor.MiddleLeft;
                currentX += columnWidth + columnSpacing;
            }

            if (showHardcore)
            {
                var hardcoreRect = new Rect(currentX, headerRect.y + 2f, columnWidth, headerRect.height - 4f);
                GUI.color = Color.yellow;
                Text.Font = GameFont.Small; // Ensure consistent font size
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(hardcoreRect, "JobTable_HardcoreMode".Translate());
                Text.Font = currentFont;
                Text.Anchor = TextAnchor.MiddleLeft;
                currentX += columnWidth + columnSpacing;
            }

            if (showExtraHardcore)
            {
                var extraHardcoreRect = new Rect(currentX, headerRect.y + 2f, columnWidth, headerRect.height - 4f);
                GUI.color = Color.red;
                Text.Font = GameFont.Small; // Ensure consistent font size
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(extraHardcoreRect, "JobTable_ExtraHardcore".Translate());
                Text.Font = currentFont;
                Text.Anchor = TextAnchor.MiddleLeft;
            }

            GUI.color = Color.white;

            // Draw job categories with alternating row colors inside the bordered table
            var currentRowY = headerRect.yMax + 4f; // Start below the header
            var rowHeight = Text.LineHeight * 1.5f; // Increased height for bigger fonts
            for (int i = 0; i < jobCategories.Count; i++)
            {
                var category = jobCategories[i];
                var rowRect = new Rect(innerTableRect.x, currentRowY, innerTableRect.width, rowHeight);
                DrawJobCategoryRowInTable(rowRect, category, dynamicJobLabelWidth, columnWidth, columnSpacing, showNormal, showHardcore, showExtraHardcore, i);
                currentRowY += rowHeight;
            }

            // Restore saved color
            GUI.color = savedColor;
        }

        public void DrawJobCategoryRowInTable(Rect rowRect, JobCategory category, float jobLabelWidth, float columnWidth, float columnSpacing, bool showNormal, bool showHardcore, bool showExtraHardcore, int rowIndex)
        {
            // Draw alternating row background
            if (rowIndex % 2 == 1) // Every other row (1, 3, 5...)
            {
                var bgColor = new Color(0.2f, 0.2f, 0.2f, 0.3f); // Subtle dark background
                var prevColor = GUI.color;
                GUI.color = bgColor;
                GUI.DrawTexture(rowRect, BaseContent.WhiteTex);
                GUI.color = prevColor;
            }

            // Job category name (leftmost, bigger font for consistency)
            var prevFont = Text.Font;
            Text.Font = GameFont.Medium;
            var jobLabelRect = new Rect(rowRect.x, rowRect.y, jobLabelWidth, rowRect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(jobLabelRect, category.Name);
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = prevFont;

            // Draw vertical separator after job label
            float sepX = jobLabelRect.xMax + (columnSpacing * 0.5f);
            GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
            GUI.DrawTexture(new Rect(sepX - 0.5f, rowRect.y, 1f, rowRect.height), BaseContent.WhiteTex);
            GUI.color = Color.white;

            // Draw columns based on which modes are active
            var currentX = rowRect.x + jobLabelWidth + columnSpacing; // Start after job label

            // Set bigger font for column values
            var prevColumnFont = Text.Font;
            Text.Font = GameFont.Medium;

            if (showNormal)
            {
                var normalRect = new Rect(currentX, rowRect.y, columnWidth, rowRect.height);
                GUI.color = Color.green;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(normalRect, GetModeStatusText(category, GameMode.Normal));
                Text.Anchor = TextAnchor.MiddleLeft;
                currentX += columnWidth + columnSpacing;

                // Draw vertical separator after normal mode column (if more columns follow)
                if (showHardcore || showExtraHardcore)
                {
                    float normalSepX = currentX - (columnSpacing * 0.5f);
                    GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                    GUI.DrawTexture(new Rect(normalSepX - 0.5f, rowRect.y, 1f, rowRect.height), BaseContent.WhiteTex);
                    GUI.color = Color.white;
                }
            }

            if (showHardcore)
            {
                var hardcoreRect = new Rect(currentX, rowRect.y, columnWidth, rowRect.height);
                GUI.color = GetModeColor(category, GameMode.Hardcore);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(hardcoreRect, GetModeStatusText(category, GameMode.Hardcore));
                Text.Anchor = TextAnchor.MiddleLeft;
                currentX += columnWidth + columnSpacing;

                // Draw vertical separator after hardcore mode column (if extra hardcore follows)
                if (showExtraHardcore)
                {
                    float hardcoreSepX = currentX - (columnSpacing * 0.5f);
                    GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                    GUI.DrawTexture(new Rect(hardcoreSepX - 0.5f, rowRect.y, 1f, rowRect.height), BaseContent.WhiteTex);
                    GUI.color = Color.white;
                }
            }

            if (showExtraHardcore)
            {
                var extraHardcoreRect = new Rect(currentX, rowRect.y, columnWidth, rowRect.height);
                GUI.color = GetModeColor(category, GameMode.ExtraHardcore);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(extraHardcoreRect, GetModeStatusText(category, GameMode.ExtraHardcore));
                Text.Anchor = TextAnchor.MiddleLeft;
            }

            // Reset font and settings
            Text.Font = prevColumnFont;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        public List<JobCategory> GetJobCategories()
        {
            var categories = new List<JobCategory>();

            // Get all WorkGivers with tool requirements
            var toolWorkGivers = DefDatabase<WorkGiverDef>.AllDefsListForReading
                .Where(wg => wg.HasModExtension<WorkGiverExtension>())
                .ToList();

            // Group by stat type to create logical categories
            var statGroups = new Dictionary<string, List<StatDef>>();

            foreach (var wg in toolWorkGivers)
            {
                var requiredStats = wg.GetModExtension<WorkGiverExtension>()?.requiredStats;
                if (requiredStats?.Any() == true)
                {
                    foreach (var stat in requiredStats)
                    {
                        var categoryName = GetStatCategoryName(stat);
                        if (!statGroups.ContainsKey(categoryName))
                            statGroups[categoryName] = new List<StatDef>();

                        if (!statGroups[categoryName].Contains(stat))
                            statGroups[categoryName].Add(stat);
                    }
                }
            }

            // Add compatibility stats from active mods
            var compatStats = CompatAPI.GetAllCompatibilityStats();
            foreach (var stat in compatStats)
            {
                var categoryName = GetStatCategoryName(stat);
                if (!statGroups.ContainsKey(categoryName))
                    statGroups[categoryName] = new List<StatDef>();

                if (!statGroups[categoryName].Contains(stat))
                    statGroups[categoryName].Add(stat);
            }

            // Add WorkSpeedGlobal category if there are any configured jobs
            if (WorkSpeedGlobalHelper.GetWorkSpeedGlobalJobs().Any())
            {
                var workSpeedGlobalStat = ST_StatDefOf.WorkSpeedGlobal;
                if (workSpeedGlobalStat != null)
                {
                    var categoryName = GetStatCategoryName(workSpeedGlobalStat);
                    if (!statGroups.ContainsKey(categoryName))
                        statGroups[categoryName] = new List<StatDef>();

                    if (!statGroups[categoryName].Contains(workSpeedGlobalStat))
                        statGroups[categoryName].Add(workSpeedGlobalStat);
                }
            }

            // Create job categories
            foreach (var group in statGroups)
            {
                categories.Add(new JobCategory
                {
                    Name = group.Key,
                    Stats = group.Value,
                    IsOptional = IsOptionalJobCategory(group.Key, group.Value)
                });
            }

            return categories.OrderBy(c => c.Name).ToList();
        }

        private string GetStatCategoryName(StatDef stat)
        {
            if (stat == ST_StatDefOf.DiggingSpeed) return "Mining";
            if (stat == StatDefOf.ConstructionSpeed) return "Construction";
            if (stat == ST_StatDefOf.TreeFellingSpeed) return "Tree Cutting";
            if (stat == ST_StatDefOf.PlantHarvestingSpeed) return "Plant Work";
            if (stat == ST_StatDefOf.SowingSpeed) return "Sowing";
            if (stat == ST_StatDefOf.ResearchSpeed) return "Research";
            if (stat == ST_StatDefOf.CleaningSpeed) return "Cleaning";
            if (stat == ST_StatDefOf.MedicalOperationSpeed || stat == ST_StatDefOf.MedicalSurgerySuccessChance) return "Medical";
            if (stat == ST_StatDefOf.ButcheryFleshSpeed || stat == ST_StatDefOf.ButcheryFleshEfficiency) return "Butchery";
            if (stat == ST_StatDefOf.MaintenanceSpeed) return "Maintenance";
            if (stat == ST_StatDefOf.DeconstructionSpeed) return "Deconstruction";

            // WorkSpeedGlobal stat handling
            if (stat == ST_StatDefOf.WorkSpeedGlobal) return "General Work Speed";

            // Research Reinvented compatibility
            if (stat?.defName == "ResearchSpeed") return "Research Reinvented";
            if (stat?.defName == "FieldResearchSpeedMultiplier") return "Field Research";

            return stat.LabelCap.ToString() ?? stat.defName;
        }

        private bool IsOptionalJobCategory(string categoryName, List<StatDef> stats)
        {
            // Cleaning and butchery are always optional (never required even in hardcore)
            if (categoryName == "Cleaning" || categoryName == "Butchery") return true;

            // Medical is optional in extra hardcore mode based on settings
            if (categoryName == "Medical") return true;

            // General Work Speed is configurable - some jobs are gated, some aren't
            if (categoryName == "General Work Speed") return true;

            // Research Reinvented categories - treat as core research work, not optional
            if (categoryName == "Reinvented Research" || categoryName == "Field Research") return false;

            return false;
        }

        private string GetModeStatusText(JobCategory category, GameMode mode)
        {
            switch (mode)
            {
                case GameMode.Normal:
                    return "JobTable_Enhanced".Translate(); // Tools provide bonuses but aren't required

                case GameMode.Hardcore:
                    // Special handling for General Work Speed based on job gating
                    if (category.Name == "General Work Speed")
                    {
                        var gatedJobs = WorkSpeedGlobalHelper.GetWorkSpeedGlobalJobs().Count(j => workSpeedGlobalJobGating.GetValueOrDefault(j.defName, true));
                        var totalJobs = WorkSpeedGlobalHelper.GetWorkSpeedGlobalJobs().Count();
                        if (gatedJobs == totalJobs) return "JobTable_Required".Translate();
                        if (gatedJobs == 0) return "JobTable_Enhanced".Translate();
                        return "JobTable_GatedFormat".Translate(gatedJobs, totalJobs);
                    }
                    if (category.IsOptional && (category.Name == "Cleaning" || category.Name == "Butchery"))
                        return "JobTable_Enhanced".Translate(); // Still optional in hardcore
                    return "JobTable_Required".Translate(); // Tools are required to do the job

                case GameMode.ExtraHardcore:
                    // Special handling for General Work Speed based on job gating
                    if (category.Name == "General Work Speed")
                    {
                        var gatedJobs = WorkSpeedGlobalHelper.GetWorkSpeedGlobalJobs().Count(j => workSpeedGlobalJobGating.GetValueOrDefault(j.defName, true));
                        var totalJobs = WorkSpeedGlobalHelper.GetWorkSpeedGlobalJobs().Count();
                        if (gatedJobs == totalJobs) return "JobTable_Required".Translate();
                        if (gatedJobs == 0) return "JobTable_Enhanced".Translate();
                        return "JobTable_GatedFormat".Translate(gatedJobs, totalJobs);
                    }
                    if (category.Name == "Cleaning" && !requireCleaningTools) return "JobTable_Enhanced".Translate();
                    if (category.Name == "Butchery" && !requireButcheryTools) return "JobTable_Enhanced".Translate();
                    if (category.Name == "Medical" && !requireMedicalTools) return "JobTable_Enhanced".Translate();
                    return "JobTable_Required".Translate(); // Tools are required based on settings

                default:
                    return "JobTable_Status_Unknown".Translate();
            }
        }

        private Color GetModeColor(JobCategory category, GameMode mode)
        {
            var status = GetModeStatusText(category, mode);
            // Compare with localized equivalents
            if (status == "JobTable_Enhanced".Translate()) return Color.green;
            if (status == "JobTable_Required".Translate()) return Color.yellow;
            if (status == "JobTable_Blocked".Translate()) return Color.red;
            return Color.white;
        }

        private enum GameMode
        {
            Normal,
            Hardcore,
            ExtraHardcore
        }

        public class JobCategory
        {
            public string Name { get; set; }
            public List<StatDef> Stats { get; set; } = new List<StatDef>();
            public bool IsOptional { get; set; }
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
            // Ensure we have an instance (paranoia)
            if (Settings == null)
                Settings = GetSettings<SurvivalToolsSettings>();

            // If your basic/enhanced UI needs the caches, prime them here
            Settings.InitializeOptionalToolCache();

            // Draw the real settings UI (the one with checkboxes + “Open Enhanced Settings” button)
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
                (UI.screenWidth - windowSize.x) / 2f,
                (UI.screenHeight - windowSize.y) / 2f,
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
            // Calculate content height for enhanced window (job table + extra hardcore only)
            // This ensures content scales properly with window size

            float baseHeight = 50f; // Base spacing (reduced since no title area)

            // Extra hardcore settings (when enabled and applicable)
            if (settings.hardcoreMode && settings.HasAnyOptionalTools && settings.extraHardcoreMode)
            {
                baseHeight += 35f * 4; // Extra hardcore options
            }

            // Job requirements table - this is the main content
            float tableWidth = availableWidth - 40f; // Account for margins
            float jobCount = CountVisibleJobs();
            float rowHeight = 30f; // Increased for bigger fonts
            float headerHeight = 60f; // Header + tooltip space
            baseHeight += headerHeight + (jobCount * rowHeight) + 40f; // Table + padding

            // Add extra space for the Close button at the bottom (RimWorld automatic close button)
            baseHeight += 60f; // Space for close button + padding

            return baseHeight + 100f; // Extra padding
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
                            Log.Warning($"[SurvivalTools] Gating enforcer failed on extra hardcore mode change: {ex.Message}");
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
            }

            // WorkSpeedGlobal Job Configuration Button
            settings.DrawWorkSpeedGlobalConfigButton(listing);

            // Job Requirements Table - centered and properly sized for the window
            DrawCenteredJobTable(listing, availableWidth);

            // Add some spacing after the job table to prevent Close button overlap
            listing.Gap();
            listing.Gap();
            listing.Gap();
        }

        /// <summary>
        /// Draw the job requirements table centered and properly sized for the enhanced window
        /// </summary>
        private void DrawCenteredJobTable(Listing_Standard listing, float availableWidth)
        {
            // ---- Title ----
            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;
            var prevColor = GUI.color;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;

            var titleRect = listing.GetRect(Text.LineHeight + 4f);
            Widgets.Label(titleRect, "JobTable_SectionTitle".Translate());
            if (Mouse.IsOver(titleRect))
                TooltipHandler.TipRegion(titleRect, "JobTable_SectionTooltip".Translate());

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;

            listing.Gap(6f);

            // ---- Data ----
            var jobCategories = settings.GetJobCategories();
            if (jobCategories.Count == 0)
            {
                var noJobsRect = listing.GetRect(Text.LineHeight);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(noJobsRect, "JobTable_NoJobsFound".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            bool showNormal = !settings.hardcoreMode && !settings.extraHardcoreMode;
            bool showHardcore = settings.hardcoreMode && !settings.extraHardcoreMode;
            bool showExtraHardcore = settings.extraHardcoreMode;

            int activeColumns = (showNormal ? 1 : 0) + (showHardcore ? 1 : 0) + (showExtraHardcore ? 1 : 0);
            if (activeColumns == 0)
            {
                listing.Label("No active mode selected.");
                return;
            }

            // ---- Measure widths (use the same font we render with) ----
            var measureFont = Text.Font;
            Text.Font = GameFont.Medium;

            string jobLabelText = "JobTable_JobType".Translate();
            string normalHeaderText = "JobTable_Normal".Translate();
            string hardcoreHeaderText = "JobTable_Hardcore".Translate();
            string extraHardcoreHeaderText = "JobTable_ExtraHardcore".Translate();

            float jobLabelWidth = Mathf.Max(Text.CalcSize(jobLabelText).x + 30f, 140f);
            float columnWidth = 100f;

            if (showNormal) columnWidth = Mathf.Max(columnWidth, Text.CalcSize(normalHeaderText).x + 40f);
            if (showHardcore) columnWidth = Mathf.Max(columnWidth, Text.CalcSize(hardcoreHeaderText).x + 40f);
            if (showExtraHardcore) columnWidth = Mathf.Max(columnWidth, Text.CalcSize(extraHardcoreHeaderText).x + 40f);

            string reqText = "JobTable_Required".Translate();
            string enhText = "JobTable_Enhanced".Translate();

            foreach (var cat in jobCategories)
            {
                jobLabelWidth = Mathf.Max(jobLabelWidth, Text.CalcSize(cat.Name).x + 30f);
                columnWidth = Mathf.Max(columnWidth, Text.CalcSize(reqText).x + 20f);
                columnWidth = Mathf.Max(columnWidth, Text.CalcSize(enhText).x + 20f);
            }

            Text.Font = measureFont;

            // ---- Table geometry ----
            const float pad = 4f;
            const float columnSpacing = 8f;
            float totalSpacing = (activeColumns - 1) * columnSpacing;
            float totalTableWidth = jobLabelWidth + (columnWidth * activeColumns) + totalSpacing;

            float headerHeight = Text.LineHeight + 10f;
            float rowHeight = Text.LineHeight * 1.5f;

            // Reserve space for the pinned header
            float pinnedHeaderSpace = headerHeight + 6f; // Extra space for visual separation

            // Calculate maximum content height for scrolling (available space minus header and padding)
            float maxContentHeight = 300f; // Maximum height before scrolling
            float contentHeight = rowHeight * jobCategories.Count;
            bool needsScrolling = contentHeight > maxContentHeight;
            float actualContentHeight = needsScrolling ? maxContentHeight : contentHeight;

            float totalTableHeight = pinnedHeaderSpace + actualContentHeight + (pad * 2f);

            // Reserve vertical space from the listing and center horizontally
            Rect reserved = listing.GetRect(totalTableHeight);
            float tableStartX = Mathf.Max(0f, (availableWidth - totalTableWidth) / 2f);

            // Outer frame
            Rect tableRect = new Rect(reserved.x + tableStartX, reserved.y, totalTableWidth, totalTableHeight);
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.3f);
            GUI.DrawTexture(tableRect, BaseContent.WhiteTex);
            GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
            Widgets.DrawBox(tableRect, 1);
            GUI.color = Color.white;

            // ---- Pinned Header Area ----
            Rect pinnedHeaderRect = new Rect(tableRect.x + pad, tableRect.y + pad, tableRect.width - pad * 2f, headerHeight);

            // Header background - darker and more prominent
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            GUI.DrawTexture(pinnedHeaderRect, BaseContent.WhiteTex);
            GUI.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            Widgets.DrawBox(pinnedHeaderRect, 1);
            GUI.color = Color.white;

            // ---- Pinned Header Content ----
            // Job Type header cell
            Rect jobHeaderRect = new Rect(pinnedHeaderRect.x, pinnedHeaderRect.y, jobLabelWidth, pinnedHeaderRect.height);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.cyan;
            Widgets.Label(new Rect(jobHeaderRect.x + 4f, jobHeaderRect.y + 2f, jobHeaderRect.width - 8f, jobHeaderRect.height - 4f),
                          "JobTable_JobType".Translate());
            GUI.color = Color.white;

            float currentX = jobHeaderRect.xMax + columnSpacing;

            // Vertical separators for pinned header
            float sepTop = pinnedHeaderRect.y;
            float sepHeight = pinnedHeaderRect.height;

            float firstSepX = jobHeaderRect.xMax + (columnSpacing * 0.5f);
            GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
            GUI.DrawTexture(new Rect(firstSepX - 0.5f, sepTop, 1f, sepHeight), BaseContent.WhiteTex);
            GUI.color = Color.white;

            // Mode headers in pinned area
            if (showNormal)
            {
                Rect normalRect = new Rect(currentX, pinnedHeaderRect.y + 2f, columnWidth, pinnedHeaderRect.height - 4f);
                GUI.color = new Color(0.5f, 1f, 0.5f);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(normalRect, normalHeaderText);
                GUI.color = Color.white;

                currentX += columnWidth + columnSpacing;

                if (showHardcore || showExtraHardcore)
                {
                    float sepX = currentX - (columnSpacing * 0.5f);
                    GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
                    GUI.DrawTexture(new Rect(sepX - 0.5f, sepTop, 1f, sepHeight), BaseContent.WhiteTex);
                    GUI.color = Color.white;
                }
            }

            if (showHardcore)
            {
                Rect hardcoreRect = new Rect(currentX, pinnedHeaderRect.y + 2f, columnWidth, pinnedHeaderRect.height - 4f);
                GUI.color = new Color(1f, 1f, 0.5f);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(hardcoreRect, hardcoreHeaderText);
                GUI.color = Color.white;

                currentX += columnWidth + columnSpacing;

                if (showExtraHardcore)
                {
                    float sepX = currentX - (columnSpacing * 0.5f);
                    GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
                    GUI.DrawTexture(new Rect(sepX - 0.5f, sepTop, 1f, sepHeight), BaseContent.WhiteTex);
                    GUI.color = Color.white;
                }
            }

            if (showExtraHardcore)
            {
                Rect extraRect = new Rect(currentX, pinnedHeaderRect.y + 2f, columnWidth, pinnedHeaderRect.height - 4f);
                GUI.color = new Color(1f, 0.5f, 0.5f);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(extraRect, extraHardcoreHeaderText);
                GUI.color = Color.white;
            }

            // ---- Scrollable Content Area ----
            Rect contentAreaRect = new Rect(tableRect.x + pad, pinnedHeaderRect.yMax + 3f, tableRect.width - pad * 2f, actualContentHeight);

            if (needsScrolling)
            {
                // Use scroll view for content. Ensure view height is at least the visible content area to prevent clipping
                Rect viewRect = new Rect(0, 0, contentAreaRect.width - 20f, Mathf.Max(contentAreaRect.height, contentHeight));

                Widgets.BeginScrollView(contentAreaRect, ref enhancedScrollPosition, viewRect);

                // Draw rows in scroll view
                float rowY = 0f;
                for (int i = 0; i < jobCategories.Count; i++)
                {
                    var category = jobCategories[i];
                    Rect rowRect = new Rect(0, rowY, viewRect.width, rowHeight);

                    if (i % 2 == 1)
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.05f);
                        GUI.DrawTexture(rowRect, BaseContent.WhiteTex);
                        GUI.color = Color.white;
                    }

                    settings.DrawJobCategoryRowInTable(rowRect, category, jobLabelWidth, columnWidth, columnSpacing,
                                                       showNormal, showHardcore, showExtraHardcore, i);
                    rowY += rowHeight;
                }

                Widgets.EndScrollView();
            }
            else
            {
                // Draw rows directly without scrolling
                float rowY = contentAreaRect.y;
                for (int i = 0; i < jobCategories.Count; i++)
                {
                    var category = jobCategories[i];
                    Rect rowRect = new Rect(contentAreaRect.x, rowY, contentAreaRect.width, rowHeight);

                    if (i % 2 == 1)
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.05f);
                        GUI.DrawTexture(rowRect, BaseContent.WhiteTex);
                        GUI.color = Color.white;
                    }

                    settings.DrawJobCategoryRowInTable(rowRect, category, jobLabelWidth, columnWidth, columnSpacing,
                                                       showNormal, showHardcore, showExtraHardcore, i);
                    rowY += rowHeight;
                }
            }

            // Reset text state
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            // restore GUI state
            GUI.color = prevColor;
            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
        }


        private float CountVisibleJobs()
        {
            // Count jobs that will be displayed in the table
            // Use a simple estimate since we don't need the exact method
            return 12f; // Approximate number of job types typically shown
        }
    }
}
