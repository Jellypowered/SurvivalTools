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
        public float toolDegradationFactor = 1f; // 0..2
        public bool toolOptimization = true;

        /// <summary>Convenience for consumers: treat near-zero as off.</summary>
        public bool ToolDegradationEnabled => toolDegradationFactor > 0.001f;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref hardcoreMode, nameof(hardcoreMode), false);
            Scribe_Values.Look(ref toolMapGen, nameof(toolMapGen), true);
            Scribe_Values.Look(ref toolLimit, nameof(toolLimit), true);
            Scribe_Values.Look(ref toolDegradationFactor, nameof(toolDegradationFactor), 1f);
            Scribe_Values.Look(ref toolOptimization, nameof(toolOptimization), true);
            base.ExposeData();
        }

        public void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            var defaultColor = GUI.color;

            listing.Begin(inRect);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            listing.Gap();

            // Hardcore (red like Merciless)
            GUI.color = new Color(1f, 0.2f, 0.2f);
            listing.CheckboxLabeled(
                "Settings_HardcoreMode".Translate(),
                ref hardcoreMode,
                "Settings_HardcoreMode_Tooltip".Translate());
            GUI.color = defaultColor;

            listing.Gap();

            listing.CheckboxLabeled(
                "Settings_ToolMapGen".Translate(),
                ref toolMapGen,
                "Settings_ToolMapGen_Tooltip".Translate());

            listing.Gap();

            listing.CheckboxLabeled(
                "Settings_ToolLimit".Translate(),
                ref toolLimit,
                "Settings_ToolLimit_Tooltip".Translate());

            listing.Gap();

            // Degradation rate (0..2) with value readout
            var degrLabel = "Settings_ToolDegradationRate".Translate();
            listing.Label($"{degrLabel}: {toolDegradationFactor.ToStringByStyle(ToStringStyle.FloatTwo, ToStringNumberSense.Factor)}");
            toolDegradationFactor = listing.Slider(toolDegradationFactor, 0f, 2f);
            toolDegradationFactor = Mathf.Round(toolDegradationFactor * 100f) / 100f; // 0.01 steps
            toolDegradationFactor = Mathf.Clamp(toolDegradationFactor, 0f, 2f);

            listing.Gap();

            listing.CheckboxLabeled(
                "Settings_ToolOptimization".Translate(),
                ref toolOptimization,
                "Settings_ToolOptimization_Tooltip".Translate());

            listing.GapLine();

            // Optional: reset-to-defaults
            if (listing.ButtonText("ResetToDefaults".Translate()))
            {
                hardcoreMode = false;
                toolMapGen = true;
                toolLimit = true;
                toolDegradationFactor = 1f;
                toolOptimization = true;
            }

            listing.End();

            // No Write() here; RimWorld handles saving on close.
        }
    }

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
            // No Write() needed; saving occurs when the window closes.
        }
    }
}

