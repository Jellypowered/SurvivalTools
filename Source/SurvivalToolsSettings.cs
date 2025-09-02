using UnityEngine;
using Verse;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using SurvivalTools.Compat;

namespace SurvivalTools
{
    public class SurvivalToolsSettings : ModSettings
    {
        // UI Layout constants
        private const float JOB_LABEL_WIDTH = 140f; // Increased from 100f to fix "Mining Yield (digging)" truncation

        public bool hardcoreMode;
        public bool toolMapGen = true;
        public bool toolLimit = true;
        public float toolDegradationFactor = 1f;
        public bool toolOptimization = true;

        public bool autoTool = true;

        public bool debugLogging = false;
        public bool compatLogging = false; // Additional debugging for mod compatibility
        public bool pickupFromStorageOnly = false;

        // Allow pacifist pawns to equip survival tools that are classified as weapons
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
            Scribe_Values.Look(ref compatLogging, nameof(compatLogging), false);
            Scribe_Values.Look(ref pickupFromStorageOnly, nameof(pickupFromStorageOnly), false);
            Scribe_Values.Look(ref allowPacifistEquip, nameof(allowPacifistEquip), true);
            Scribe_Values.Look(ref autoTool, nameof(autoTool), true);

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
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;

                // Use standard RimWorld settings approach - no scrolling needed
                var listing = new Listing_Standard();
                listing.Begin(inRect);

                DrawSettingsContent(listing);

                listing.End();
            }
            finally
            {
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
                GUI.color = prevColor;
            }
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
            listing.CheckboxLabeled("Settings_HardcoreMode".Translate(), ref hardcoreMode, "Settings_HardcoreMode_Tooltip".Translate());

            // Auto-disable Extra Hardcore Mode when Hardcore Mode is disabled
            if (prevHardcoreMode && !hardcoreMode && extraHardcoreMode)
            {
                extraHardcoreMode = false;
            }

            GUI.color = prevColor;

            // General settings
            listing.CheckboxLabeled("Settings_ToolMapGen".Translate(), ref toolMapGen, "Settings_ToolMapGen_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_ToolLimit".Translate(), ref toolLimit, "Settings_ToolLimit_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_AutoTool".Translate(), ref autoTool, "Settings_AutoTool_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_ToolOptimization".Translate(), ref toolOptimization, "Settings_ToolOptimization_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_PickupFromStorageOnly".Translate(), ref pickupFromStorageOnly, "Settings_PickupFromStorageOnly_Tooltip".Translate());
            listing.CheckboxLabeled("Settings_AllowPacifistEquip".Translate(), ref allowPacifistEquip, "Settings_AllowPacifistEquip_Tooltip".Translate());
            listing.Gap();

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
                    SurvivalToolUtility.InvalidateDebugLoggingCache();
                }

                // Show compatibility logging option only when debug logging is enabled
                if (debugLogging)
                {
                    listing.CheckboxLabeled("Settings_CompatLogging".Translate(), ref compatLogging, "Settings_CompatLogging_Tooltip".Translate());
                }
            }

            listing.GapLine();
            listing.Gap(6f);

            // Enhanced Settings Button - Red and Centered
            Text.Font = prevFont;
            Text.Anchor = prevAnchor;

            // Use listing to get properly positioned button
            var buttonWidth = 280f;
            var buttonHeight = 45f;
            var fullButtonRect = listing.GetRect(buttonHeight);

            // Center the button within the available width
            var buttonRect = new Rect(
                fullButtonRect.x + (fullButtonRect.width - buttonWidth) / 2f,
                fullButtonRect.y,
                buttonWidth,
                buttonHeight
            );

            // Draw red enhanced settings button
            var originalColor = GUI.color;
            GUI.color = new Color(1f, 0.3f, 0.3f); // Red color

            if (Widgets.ButtonText(buttonRect, "Settings_OpenEnhancedButton".Translate()))
            {
                // Ensure cache is initialized before showing settings
                InitializeOptionalToolCache();

                // Close any existing resizable window first
                var existingWindow = Find.WindowStack.Windows.OfType<SurvivalToolsResizableSettingsWindow>().FirstOrDefault();
                if (existingWindow != null)
                {
                    Find.WindowStack.TryRemove(existingWindow);
                }

                var window = new SurvivalToolsResizableSettingsWindow(this);
                Find.WindowStack.Add(window);
            }

            GUI.color = originalColor;

            // Restore original styling
            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
            GUI.color = prevColor;
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
                listing.Label("No active mode selected.");
                return;
            }

            const float columnSpacing = 8f;
            const float minColumnWidth = 120f; // Minimum width to fit "Extra Hardcore" and "Required" text

            // Calculate dynamic job label width based on actual content
            var measureFont = Text.Font;
            Text.Font = GameFont.Medium; // Use same font as rendering

            var jobLabelText = "JobTable_JobType".Translate();
            var dynamicJobLabelWidth = Mathf.Max(Text.CalcSize(jobLabelText).x + 30f, 140f);

            // Check all job categories for max width needed
            foreach (var category in jobCategories)
            {
                dynamicJobLabelWidth = Mathf.Max(dynamicJobLabelWidth, Text.CalcSize(category.Name).x + 30f);
            }

            Text.Font = measureFont; // Restore font

            var availableWidth = listing.ColumnWidth - dynamicJobLabelWidth;
            var calculatedColumnWidth = activeColumns == 1 ? availableWidth :
                              (availableWidth - (columnSpacing * (activeColumns - 1))) / activeColumns;

            // Ensure column width is sufficient for the text content
            var columnWidth = Mathf.Max(calculatedColumnWidth, minColumnWidth);

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

            // Draw columns based on which modes are active
            var currentX = rowRect.x + jobLabelWidth; // Start after job label

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
            }

            if (showHardcore)
            {
                var hardcoreRect = new Rect(currentX, rowRect.y, columnWidth, rowRect.height);
                GUI.color = GetModeColor(category, GameMode.Hardcore);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(hardcoreRect, GetModeStatusText(category, GameMode.Hardcore));
                Text.Anchor = TextAnchor.MiddleLeft;
                currentX += columnWidth + columnSpacing;
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

            // Research Reinvented compatibility
            if (stat?.defName == "ResearchSpeed") return "Compat_ResearchReinvented".Translate();
            if (stat?.defName == "FieldResearchSpeedMultiplier") return "Compat_FieldResearch".Translate();

            return stat.LabelCap.ToString() ?? stat.defName;
        }

        private bool IsOptionalJobCategory(string categoryName, List<StatDef> stats)
        {
            // Cleaning and butchery are always optional (never required even in hardcore)
            if (categoryName == "Cleaning" || categoryName == "Butchery") return true;

            // Medical is optional in extra hardcore mode based on settings
            if (categoryName == "Medical") return true;

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
                    if (category.IsOptional && (category.Name == "Cleaning" || category.Name == "Butchery"))
                        return "JobTable_Enhanced".Translate(); // Still optional in hardcore
                    return "JobTable_Required".Translate(); // Tools are required to do the job

                case GameMode.ExtraHardcore:
                    if (category.Name == "Cleaning" && !requireCleaningTools) return "JobTable_Enhanced".Translate();
                    if (category.Name == "Butchery" && !requireButcheryTools) return "JobTable_Enhanced".Translate();
                    if (category.Name == "Medical" && !requireMedicalTools) return "JobTable_Enhanced".Translate();
                    return "JobTable_Required".Translate(); // Tools are required based on settings

                default:
                    return "Unknown";
            }
        }

        private Color GetModeColor(JobCategory category, GameMode mode)
        {
            var status = GetModeStatusText(category, mode);
            switch (status)
            {
                case "Enhanced": return Color.green;
                case "Required": return Color.yellow;
                case "Blocked": return Color.red;
                default: return Color.white;
            }
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

                // Create scrollable area
                var viewRect = new Rect(0, 0, contentRect.width - 20f, contentHeight);

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
                }

                GUI.color = prevColor;
                listing.GapLine();
                listing.Gap(12f);
            }

            // Job Requirements Table - centered and properly sized for the window
            DrawCenteredJobTable(listing, availableWidth);
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
            float tableHeight = headerHeight + (rowHeight * jobCategories.Count) + (pad * 2f);

            // Reserve vertical space from the listing and center horizontally
            Rect reserved = listing.GetRect(tableHeight);
            float tableStartX = Mathf.Max(0f, (availableWidth - totalTableWidth) / 2f);

            // Outer frame
            Rect tableRect = new Rect(reserved.x + tableStartX, reserved.y, totalTableWidth, tableHeight);
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.3f);
            GUI.DrawTexture(tableRect, BaseContent.WhiteTex);
            GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
            Widgets.DrawBox(tableRect, 1);
            GUI.color = Color.white;

            // Inner content area (use this for headers, rows, and ALL separators)
            Rect innerRect = new Rect(tableRect.x + pad, tableRect.y + pad, tableRect.width - pad * 2f, tableRect.height - pad * 2f);
            Rect headerRect = new Rect(innerRect.x, innerRect.y, innerRect.width, headerHeight);

            // Header background
            GUI.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
            GUI.DrawTexture(headerRect, BaseContent.WhiteTex);
            GUI.color = Color.white;

            // ---- Header cells ----
            // Job Type header cell (text area slightly inset)
            Rect jobHeaderRect = new Rect(headerRect.x, headerRect.y, jobLabelWidth, headerRect.height);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(jobHeaderRect.x + 4f, jobHeaderRect.y + 2f, jobHeaderRect.width - 8f, jobHeaderRect.height - 4f),
                          "JobTable_JobType".Translate());

            float currentX = jobHeaderRect.xMax + columnSpacing;

            // Vertical separators (now perfectly bounded)
            float sepTop = innerRect.y;
            float sepHeight = innerRect.height;

            float firstSepX = jobHeaderRect.xMax + (columnSpacing * 0.5f);
            GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
            GUI.DrawTexture(new Rect(firstSepX - 0.5f, sepTop, 1f, sepHeight), BaseContent.WhiteTex);
            GUI.color = Color.white;

            // Mode headers
            if (showNormal)
            {
                Rect normalRect = new Rect(currentX, headerRect.y + 2f, columnWidth, headerRect.height - 4f);
                GUI.color = new Color(0.5f, 1f, 0.5f);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(normalRect, normalHeaderText);
                GUI.color = Color.white;

                currentX += columnWidth + columnSpacing;

                if (showHardcore || showExtraHardcore)
                {
                    float sepX = currentX - (columnSpacing * 0.5f);
                    GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                    GUI.DrawTexture(new Rect(sepX - 0.5f, sepTop, 1f, sepHeight), BaseContent.WhiteTex);
                    GUI.color = Color.white;
                }
            }

            if (showHardcore)
            {
                Rect hardcoreRect = new Rect(currentX, headerRect.y + 2f, columnWidth, headerRect.height - 4f);
                GUI.color = new Color(1f, 1f, 0.5f);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(hardcoreRect, hardcoreHeaderText);
                GUI.color = Color.white;

                currentX += columnWidth + columnSpacing;

                if (showExtraHardcore)
                {
                    float sepX = currentX - (columnSpacing * 0.5f);
                    GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                    GUI.DrawTexture(new Rect(sepX - 0.5f, sepTop, 1f, sepHeight), BaseContent.WhiteTex);
                    GUI.color = Color.white;
                }
            }

            if (showExtraHardcore)
            {
                Rect extraRect = new Rect(currentX, headerRect.y + 2f, columnWidth, headerRect.height - 4f);
                GUI.color = new Color(1f, 0.5f, 0.5f);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(extraRect, extraHardcoreHeaderText);
                GUI.color = Color.white;
            }

            // Reset text state
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            // ---- Rows ----
            float rowY = innerRect.y + headerHeight;
            for (int i = 0; i < jobCategories.Count; i++)
            {
                var category = jobCategories[i];
                Rect rowRect = new Rect(innerRect.x, rowY, innerRect.width, rowHeight);

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
