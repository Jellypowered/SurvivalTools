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

        public bool ToolDegradationEnabled => toolDegradationFactor > 0.001f;

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
                    listing.CheckboxLabeled("Settings_DebugLogging".Translate(), ref debugLogging, "Settings_DebugLogging_Tooltip".Translate());
                    listing.Gap();
                }

                // Hardcore (red like Merciless)
                GUI.color = new Color(1f, 0.2f, 0.2f);
                listing.CheckboxLabeled("Settings_HardcoreMode".Translate(), ref hardcoreMode, "Settings_HardcoreMode_Tooltip".Translate());
                GUI.color = prevColor;

                listing.Gap();
                listing.CheckboxLabeled("Settings_ToolMapGen".Translate(), ref toolMapGen, "Settings_ToolMapGen_Tooltip".Translate());
                listing.Gap();
                listing.CheckboxLabeled("Settings_ToolLimit".Translate(), ref toolLimit, "Settings_ToolLimit_Tooltip".Translate());
                listing.Gap();
                listing.CheckboxLabeled("Settings_PickupFromStorageOnly".Translate(), ref pickupFromStorageOnly, "Settings_PickupFromStorageOnly_Tooltip".Translate());
                listing.Gap();
                // Degradation slider
                var degrLabel = "Settings_ToolDegradationRate".Translate();
                listing.Label(degrLabel + ": " + toolDegradationFactor.ToStringByStyle(ToStringStyle.FloatTwo, ToStringNumberSense.Factor));
                toolDegradationFactor = listing.Slider(toolDegradationFactor, 0f, 2f);
                toolDegradationFactor = Mathf.Clamp(Mathf.Round(toolDegradationFactor * 100f) / 100f, 0f, 2f);

                listing.Gap();
                listing.CheckboxLabeled("Settings_AutoTool".Translate(), ref autoTool, "Settings_AutoTool_Tooltip".Translate());
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
            Settings.DoSettingsWindowContents(inRect);
        }
    }
    #endregion
}
