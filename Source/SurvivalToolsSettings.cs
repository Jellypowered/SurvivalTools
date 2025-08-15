using UnityEngine;
using Verse;
using RimWorld;

namespace SurvivalTools
{
    // Loads GUI textures on the main thread at startup
    [StaticConstructorOnStartup]
    public static class STPreviewAssets
    {
        public static readonly Texture2D Pawn;

        public static readonly Texture2D PickaxeN;
        public static readonly Texture2D PickaxeE;
        public static readonly Texture2D PickaxeS;
        public static readonly Texture2D PickaxeW;

        static STPreviewAssets()
        {
            // Pawn silhouette for preview
            Pawn = ContentFinder<Texture2D>.Get("SurvivalTools/Preview/Pawn", false);

            // Directional, pre-rotated tool sprites
            PickaxeN = ContentFinder<Texture2D>.Get("SurvivalTools/Preview/PickaxeN", false);
            PickaxeE = ContentFinder<Texture2D>.Get("SurvivalTools/Preview/PickaxeE", false);
            PickaxeS = ContentFinder<Texture2D>.Get("SurvivalTools/Preview/PickaxeS", false);
            PickaxeW = ContentFinder<Texture2D>.Get("SurvivalTools/Preview/PickaxeW", false);
        }

        public static Texture2D PickaxeFor(Rot4 f)
        {
            if (f == Rot4.North) return PickaxeN;
            if (f == Rot4.East) return PickaxeE;
            if (f == Rot4.South) return PickaxeS;
            return PickaxeW;
        }
    }

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

        // Offsets used by in-world draw (stored per facing)
        public static Vector3 offsetNorth = new Vector3(0.10f, 0f, 0.30f);
        public static Vector3 offsetSouth = new Vector3(0.10f, 0f, 0.10f);
        public static Vector3 offsetEast = new Vector3(0.22f, 0f, 0.20f);
        public static Vector3 offsetWest = new Vector3(-0.22f, 0f, 0.20f);

        public static bool drawToolsDuringWork = true;

        // UI state
        private static Rot4 previewFacing = Rot4.North;

        // Text buffers for each facing
        private static string nXBuf = offsetNorth.x.ToString("0.###"), nZBuf = offsetNorth.z.ToString("0.###");
        private static string sXBuf = offsetSouth.x.ToString("0.###"), sZBuf = offsetSouth.z.ToString("0.###");
        private static string eXBuf = offsetEast.x.ToString("0.###"), eZBuf = offsetEast.z.ToString("0.###");
        private static string wXBuf = offsetWest.x.ToString("0.###"), wZBuf = offsetWest.z.ToString("0.###");

        // Tool hotspot (grip) inside each texture (0..1). Adjust if your art changes.
        private const float ToolAnchorU = 0.58f;
        private const float ToolAnchorV = 0.50f;

        private const float OffsetMin = -1.0f, OffsetMax = 1.0f;
        private const float NudgeSmall = 0.01f, NudgeBig = 0.05f;

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

            Scribe_Values.Look(ref drawToolsDuringWork, "st_drawToolsDuringWork", true);
            Scribe_Values.Look(ref offsetNorth, "st_offsetNorth", new Vector3(0.10f, 0f, 0.30f));
            Scribe_Values.Look(ref offsetSouth, "st_offsetSouth", new Vector3(0.10f, 0f, 0.10f));
            Scribe_Values.Look(ref offsetEast, "st_offsetEast", new Vector3(0.23f, 0f, 0.22f));
            Scribe_Values.Look(ref offsetWest, "st_offsetWest", new Vector3(-0.23f, 0f, 0.22f));
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
                listing.CheckboxLabeled("Draw tools during work", ref drawToolsDuringWork, "Show survival tools in pawns' hands while they work.");
                listing.Gap();
                listing.CheckboxLabeled("Settings_AutoTool".Translate(), ref autoTool, "Settings_AutoTool_Tooltip".Translate());
                listing.GapLine();

                // Header + global facing toolbar
                var headerRect = listing.GetRect(30f);
                Widgets.Label(headerRect.LeftHalf().ContractedBy(2f), "Tool draw offsets (X / Z)".CapitalizeFirst());
                DrawFacingToolbar(headerRect.RightHalf().ContractedBy(2f));

                listing.Gap(6f);

                // Single row: active facing editor (left) + single preview (right)
                var row = listing.GetRect(160f);
                var left = new Rect(row.x, row.y, row.width * 0.52f, row.height).ContractedBy(6f);
                var right = new Rect(left.xMax + 6f, row.y, row.width - left.width - 6f, row.height).ContractedBy(6f);

                DrawActiveOffsetEditor(left);
                DrawOffsetPreview(right);

                listing.GapLine();

                // Reset buttons (always visible)
                var resetRow = listing.GetRect(32f);
                var leftHalf = resetRow.LeftHalf().ContractedBy(2f);
                var rightHalf = resetRow.RightHalf().ContractedBy(2f);

                if (Widgets.ButtonText(leftHalf, "Reset Offsets")) ResetOffsetsToDefaults();
                if (Widgets.ButtonText(rightHalf, "Reset All")) ResetAllToDefaults();

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
        #region UI pieces 

        private static void DrawFacingToolbar(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(4f);
            float btnW = inner.width / 4f - 3f, btnH = 24f;

            var nRect = new Rect(inner.x, inner.y, btnW, btnH);
            var eRect = new Rect(nRect.xMax + 4f, inner.y, btnW, btnH);
            var sRect = new Rect(eRect.xMax + 4f, inner.y, btnW, btnH);
            var wRect = new Rect(sRect.xMax + 4f, inner.y, btnW, btnH);

            if (DrawToggle(nRect, "N", previewFacing == Rot4.North)) previewFacing = Rot4.North;
            if (DrawToggle(eRect, "E", previewFacing == Rot4.East)) previewFacing = Rot4.East;
            if (DrawToggle(sRect, "S", previewFacing == Rot4.South)) previewFacing = Rot4.South;
            if (DrawToggle(wRect, "W", previewFacing == Rot4.West)) previewFacing = Rot4.West;
        }

        private static bool DrawToggle(Rect r, string label, bool selected)
        {
            if (selected) Widgets.DrawHighlightSelected(r);
            var a = Text.Anchor; var c = GUI.color;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = selected ? Color.white : new Color(1f, 1f, 1f, 0.85f);
            bool clicked = Widgets.ButtonText(r, label, drawBackground: !selected);
            GUI.color = c; Text.Anchor = a;
            return clicked;
        }

        private static ref Vector3 RefOffsetForFacing(Rot4 f)
        {
            if (f == Rot4.North) return ref offsetNorth;
            if (f == Rot4.East) return ref offsetEast;
            if (f == Rot4.South) return ref offsetSouth;
            return ref offsetWest;
        }

        private static void GetBuffersForFacing(Rot4 f, out string xBuf, out string zBuf)
        {
            if (f == Rot4.North) { xBuf = nXBuf; zBuf = nZBuf; return; }
            if (f == Rot4.East) { xBuf = eXBuf; zBuf = eZBuf; return; }
            if (f == Rot4.South) { xBuf = sXBuf; zBuf = sZBuf; return; }
            xBuf = wXBuf; zBuf = wZBuf;
        }
        private static void SetBuffersForFacing(Rot4 f, string xBuf, string zBuf)
        {
            if (f == Rot4.North) { nXBuf = xBuf; nZBuf = zBuf; return; }
            if (f == Rot4.East) { eXBuf = xBuf; eZBuf = zBuf; return; }
            if (f == Rot4.South) { sXBuf = xBuf; sZBuf = zBuf; return; }
            wXBuf = xBuf; wZBuf = zBuf;
        }

        private static void DrawActiveOffsetEditor(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 22f), "Editing: " + previewFacing + " (X / Z)");

            ref var vec = ref RefOffsetForFacing(previewFacing);
            GetBuffersForFacing(previewFacing, out var xBuf, out var zBuf);

            float half = (rect.width - 12f) / 2f;
            var xRect = new Rect(rect.x, rect.y + 26f, half, 28f);
            var zRect = new Rect(xRect.xMax + 12f, xRect.y, half, 28f);

            float x = vec.x, z = vec.z;
            Widgets.TextFieldNumeric(xRect, ref x, ref xBuf, OffsetMin, OffsetMax);
            Widgets.TextFieldNumeric(zRect, ref z, ref zBuf, OffsetMin, OffsetMax);

            var xNudge = new Rect(xRect.x, xRect.yMax + 6f, half, 24f);
            var zNudge = new Rect(zRect.x, zRect.yMax + 6f, half, 24f);
            DrawNudgers(xNudge, ref x, ref xBuf);
            DrawNudgers(zNudge, ref z, ref zBuf);

            vec.x = Mathf.Clamp(Round2(x), OffsetMin, OffsetMax);
            vec.z = Mathf.Clamp(Round2(z), OffsetMin, OffsetMax);
            xBuf = vec.x.ToString("0.###");
            zBuf = vec.z.ToString("0.###");
            SetBuffersForFacing(previewFacing, xBuf, zBuf);
        }

        private static float Round2(float v) { return Mathf.Round(v * 100f) / 100f; }

        private static void DrawNudgers(Rect rect, ref float value, ref string buf)
        {
            float w = rect.width / 4f - 3f, h = rect.height;
            var m1 = new Rect(rect.x, rect.y, w, h);
            var m5 = new Rect(m1.xMax + 4f, rect.y, w, h);
            var p1 = new Rect(m5.xMax + 4f, rect.y, w, h);
            var p5 = new Rect(p1.xMax + 4f, rect.y, w, h);

            if (Widgets.ButtonText(m1, "-.01")) value -= NudgeSmall;
            if (Widgets.ButtonText(m5, "-.05")) value -= NudgeBig;
            if (Widgets.ButtonText(p1, "+.01")) value += NudgeSmall;
            if (Widgets.ButtonText(p5, "+.05")) value += NudgeBig;

            value = Mathf.Clamp(Round2(value), OffsetMin, OffsetMax);
            buf = value.ToString("0.###");
        }

        private static void DrawOffsetPreview(Rect rect)
        {
            // Frame and inner square
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(8f);

            float size = Mathf.Clamp(Mathf.Min(inner.width, inner.height), 110f, 280f);
            var square = new Rect(inner.x + (inner.width - size) / 2f,
                                  inner.y + (inner.height - size) / 2f,
                                  size, size);

            GUI.BeginGroup(square);
            try
            {
                // Background
                var full = new Rect(0f, 0f, size, size);
                Widgets.DrawBoxSolid(full, new Color(0.08f, 0.08f, 0.08f, 0.20f));

                // Pawn
                float pawnSize = size * 0.70f;
                var pawnRect = new Rect((size - pawnSize) / 2f, (size - pawnSize) / 2f, pawnSize, pawnSize);
                if (STPreviewAssets.Pawn != null) GUI.DrawTexture(pawnRect, STPreviewAssets.Pawn, ScaleMode.StretchToFill, true);
                else Widgets.DrawBox(pawnRect, 1);

                // Crosshair
                var center = pawnRect.center;
                var cross = new Color(1f, 1f, 1f, 0.15f);
                LineH(center.x - 12f, center.y, 24f, cross, 1.5f);
                LineV(center.x, center.y - 12f, 24f, cross, 1.5f);

                // Convert world offset -> preview pixels
                var off = RefOffsetForFacing(previewFacing);
                float pxPerUnit = pawnRect.width * (60f / 64f);
                Vector3 rotated = off.RotatedBy(previewFacing);
                var toolPos = new Vector2(center.x + rotated.x * pxPerUnit, center.y - rotated.z * pxPerUnit);

                // Choose the pre-rotated texture for the current facing
                Texture2D toolTex = STPreviewAssets.PickaxeFor(previewFacing);

                // Reasonable, consistent size across facings
                float toolSize = Mathf.Clamp(pawnRect.width * 0.30f, 16f, 30f);

                // Build an anchored rect (no rotation)
                var tRect = BuildAnchoredToolRect(toolPos, toolSize, toolTex);

                if (toolTex != null)
                    GUI.DrawTexture(tRect, toolTex, ScaleMode.StretchToFill, true);
                else
                    GUI.DrawTexture(tRect, BaseContent.WhiteTex, ScaleMode.StretchToFill, true);

                // Red pivot dot (where your offset lands)
                Widgets.DrawBoxSolid(new Rect(toolPos.x - 1.5f, toolPos.y - 1.5f, 3f, 3f), new Color(0.95f, 0.35f, 0.35f, 1f));
            }
            finally { GUI.EndGroup(); }
        }

        private static Rect BuildAnchoredToolRect(Vector2 pivotScreen, float baseSizePx, Texture2D texOrNull)
        {
            float aspect = 1f;
            if (texOrNull != null && texOrNull.width > 0)
                aspect = (float)texOrNull.height / texOrNull.width;

            float toolW = baseSizePx;
            float toolH = baseSizePx * aspect;

            // Anchor inside the (already correctly oriented) texture
            float dx = (ToolAnchorU - 0.5f) * toolW;
            float dy = (ToolAnchorV - 0.5f) * toolH;

            var center = new Vector2(pivotScreen.x - dx, pivotScreen.y - dy);
            return new Rect(center.x - toolW / 2f, center.y - toolH / 2f, toolW, toolH);
        }
        private static void LineH(float x, float y, float length, Color color, float thickness = 1f)
        {
            var old = GUI.color; GUI.color = color;
            Widgets.DrawBoxSolid(new Rect(x, y - thickness * 0.5f, length, thickness), color);
            GUI.color = old;
        }

        private static void LineV(float x, float y, float length, Color color, float thickness = 1f)
        {
            var old = GUI.color; GUI.color = color;
            Widgets.DrawBoxSolid(new Rect(x - thickness * 0.5f, y, thickness, length), color);
            GUI.color = old;
        }
        public static Vector3 OffsetFor(Rot4 facing)
        {
            if (facing == Rot4.North) return offsetNorth;
            if (facing == Rot4.East) return offsetEast;
            if (facing == Rot4.South) return offsetSouth;
            return offsetWest;
        }
        #endregion
        #region Resets

        private static void ResetOffsetsToDefaults()
        {
            offsetNorth = new Vector3(0.10f, 0f, 0.30f);
            offsetSouth = new Vector3(0.10f, 0f, 0.10f);
            offsetEast = new Vector3(0.22f, 0f, 0.20f);
            offsetWest = new Vector3(-0.22f, 0f, 0.20f);

            nXBuf = offsetNorth.x.ToString("0.###"); nZBuf = offsetNorth.z.ToString("0.###");
            sXBuf = offsetSouth.x.ToString("0.###"); sZBuf = offsetSouth.z.ToString("0.###");
            eXBuf = offsetEast.x.ToString("0.###"); eZBuf = offsetEast.z.ToString("0.###");
            wXBuf = offsetWest.x.ToString("0.###"); wZBuf = offsetWest.z.ToString("0.###");
        }

        private void ResetAllToDefaults()
        {
            hardcoreMode = false;
            toolMapGen = true;
            toolLimit = true;
            toolDegradationFactor = 1f;
            toolOptimization = true;
            drawToolsDuringWork = true;
            autoTool = true;
            debugLogging = false;

            previewFacing = Rot4.North;

            ResetOffsetsToDefaults();
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
