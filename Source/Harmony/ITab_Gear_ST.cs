// RimWorld 1.6 / C# 7.3
// Source/Harmony/ITab_Gear_ST.cs
//
// Phase 7: Harmony patches for ITab_Pawn_Gear integration
// - Draw our panel alongside vanilla gear list
// - Input isolation to prevent interference

using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch]
    public static class ITab_Gear_ST
    {
        private static readonly string LegacyUIHarmonyId = "pjellyowered.survivaltools.legacyui";
        private static FieldInfo _sizeField;
        private static PropertyInfo _selPawnProperty;

        static ITab_Gear_ST()
        {
            // Cache reflection info for private members
            _sizeField = AccessTools.Field(typeof(InspectTabBase), "size");
            _selPawnProperty = AccessTools.Property(typeof(ITab), "SelPawn");
        }

        /// <summary>
        /// Expand the gear tab width to accommodate our panel
        /// </summary>
        [HarmonyPatch(typeof(ITab_Pawn_Gear), MethodType.Constructor)]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void ExpandGearTabWidth(ITab_Pawn_Gear __instance)
        {
            try
            {
                // Always expand each instance (remove the global flag)
                var currentSize = GetTabSize(__instance);
                var additionalWidth = UI.GearTab_ST.DesiredWidth + 12f; // Panel width + margin
                var newSize = new Vector2(currentSize.x + additionalWidth, currentSize.y);
                SetTabSize(__instance, newSize);
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools] Failed to expand gear tab: {ex}");
            }
        }

        /// <summary>
        /// Intercept vanilla FillTab to constrain its drawing area
        /// </summary>
        [HarmonyPatch(typeof(ITab_Pawn_Gear), "FillTab")]
        [HarmonyPrefix]
        [HarmonyAfter("jellypowered.survivaltools.legacyui")]
        [HarmonyPriority(Priority.First)]
        public static void LimitVanillaDrawArea(ITab_Pawn_Gear __instance)
        {
            try
            {
                // Get pawn and check if we should constrain
                var pawn = GetSelPawn(__instance);
                if (pawn == null || HasLegacyUIActive()) return;

                // Store the original tab size and set a constrained size for vanilla drawing
                var currentTabSize = GetTabSize(__instance);
                var constrainedWidth = 425f; // Leave space for our panel

                // Only constrain if we have a wide tab
                if (currentTabSize.x > constrainedWidth + 50f) // Some buffer
                {
                    var constrainedSize = new Vector2(constrainedWidth, currentTabSize.y);
                    SetTabSize(__instance, constrainedSize);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[SurvivalTools] Error constraining vanilla draw area: {ex}");
            }
        }

        /// <summary>
        /// Draw our panel alongside the vanilla gear list
        /// </summary>
        [HarmonyPatch(typeof(ITab_Pawn_Gear), "FillTab")]
        [HarmonyPostfix]
        [HarmonyAfter("jellypowered.survivaltools.legacyui")]
        [HarmonyPriority(Priority.Last)]
        public static void DrawSurvivalToolsPanel(ITab_Pawn_Gear __instance)
        {
            try
            {
                // Get pawn using reflection
                var pawn = GetSelPawn(__instance);
                if (pawn == null) return;

                // Check if we should draw (avoid conflicts with legacy UI)
                if (HasLegacyUIActive())
                {
                    return;
                }

                // Restore the full tab size for our drawing
                var currentTabSize = GetTabSize(__instance);
                var panelWidth = UI.GearTab_ST.DesiredWidth;
                var requiredTabWidth = 430f + panelWidth + 12f; // vanilla width + panel + margin

                // Restore full size if it was constrained
                if (currentTabSize.x < requiredTabWidth)
                {
                    var expandedSize = new Vector2(requiredTabWidth, currentTabSize.y);
                    SetTabSize(__instance, expandedSize);
                    currentTabSize = expandedSize;
                }

                // Position the panel to the right of vanilla content (at x=430, vanilla area is 0-425)
                var panelRect = new Rect(
                    430f,  // Start right after vanilla content
                    10f,   // Standard top margin
                    panelWidth,
                    currentTabSize.y - 20f  // Full height minus margins
                );

                // Draw our tool efficiency panel
                UI.GearTab_ST.Draw(panelRect, pawn);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[SurvivalTools] Error drawing survival tools panel: {ex}");
            }
        }

        /// <summary>
        /// Get SelPawn using reflection
        /// </summary>
        private static Pawn GetSelPawn(ITab_Pawn_Gear instance)
        {
            try
            {
                return (Pawn)_selPawnProperty?.GetValue(instance, null);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get tab size using reflection
        /// </summary>
        private static Vector2 GetTabSize(ITab_Pawn_Gear instance)
        {
            try
            {
                return (Vector2)(_sizeField?.GetValue(instance) ?? new Vector2(432f, 480f));
            }
            catch
            {
                return new Vector2(432f, 480f); // Default size
            }
        }

        /// <summary>
        /// Set tab size using reflection
        /// </summary>
        private static void SetTabSize(ITab_Pawn_Gear instance, Vector2 newSize)
        {
            try
            {
                _sizeField?.SetValue(instance, newSize);
            }
            catch (Exception ex)
            {
                Log.Warning($"[SurvivalTools] Failed to set tab size: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if legacy UI is active to avoid conflicts
        /// </summary>
        private static bool HasLegacyUIActive()
        {
            try
            {
                // Check if legacy patches are still active
                var original = AccessTools.Method(typeof(ITab_Pawn_Gear), "FillTab");
                if (original != null)
                {
                    var patches = HarmonyLib.Harmony.GetPatchInfo(original);
                    if (patches?.Postfixes != null)
                    {
                        foreach (var patch in patches.Postfixes)
                        {
                            if (patch.owner == LegacyUIHarmonyId)
                            {
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
            catch
            {
                // If we can't determine, assume legacy is not active
                return false;
            }
        }

        /// <summary>
        /// Unpatch legacy UI if needed (called from settings or startup)
        /// </summary>
        public static void UnpatchLegacyUIIfNeeded()
        {
            try
            {
                var harmonyInstance = new HarmonyLib.Harmony("survivaltools.phase7");
                var original = AccessTools.Method(typeof(ITab_Pawn_Gear), "FillTab");
                if (original != null)
                {
                    harmonyInstance.Unpatch(original, HarmonyPatchType.Postfix, LegacyUIHarmonyId);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[SurvivalTools] Could not unpatch legacy UI: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Harmony patch guard for method signature changes
    /// </summary>
    [HarmonyPatch]
    public static class ITab_Gear_ST_Guard
    {
        public static bool Prepare()
        {
            // Verify ITab_Pawn_Gear.FillTab exists with expected signature
            var method = AccessTools.Method(typeof(ITab_Pawn_Gear), "FillTab");
            if (method == null)
            {
                Log.Warning("[SurvivalTools] ITab_Pawn_Gear.FillTab method not found - skipping gear tab integration");
                return false;
            }

            // Verify method signature (should be void FillTab())
            var parameters = method.GetParameters();
            if (parameters.Length != 0)
            {
                Log.Warning("[SurvivalTools] ITab_Pawn_Gear.FillTab has unexpected signature - skipping gear tab integration");
                return false;
            }

            return true;
        }

        [HarmonyTargetMethod]
        public static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ITab_Pawn_Gear), "FillTab");
        }
    }
}