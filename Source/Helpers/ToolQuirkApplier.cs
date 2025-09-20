// RimWorld 1.6 / C# 7.3
// Source/Helpers/ToolQuirkApplier.cs
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SurvivalTools.Helpers
{
    /// <summary>
    /// Safe, no-alloc utility for applying tool quirks during stat resolution.
    /// Provides controlled access to modify tool stats with validation.
    /// </summary>
    public class ToolQuirkApplier
    {
        private readonly ToolStatResolver.ToolStatInfo _info;

        internal ToolQuirkApplier(ToolStatResolver.ToolStatInfo info)
        {
            _info = info ?? throw new ArgumentNullException(nameof(info));
        }

        /// <summary>
        /// The tool def being processed
        /// </summary>
        public ThingDef ToolDef => _info.ToolDef;

        /// <summary>
        /// The stuff def (material) being processed
        /// </summary>
        public ThingDef StuffDef => _info.StuffDef;

        /// <summary>
        /// The stat being resolved
        /// </summary>
        public StatDef Stat => _info.Stat;

        /// <summary>
        /// Current factor value
        /// </summary>
        public float Factor => _info.Factor;

        /// <summary>
        /// Source of the current factor ("Explicit", "StatBases", "NameHint", "Default")
        /// </summary>
        public string Source => _info.Source;

        /// <summary>
        /// Multiply the current factor by a value
        /// </summary>
        /// <param name="multiplier">Multiplier to apply</param>
        /// <param name="tag">Optional tag for tracking</param>
        public void MultiplyFactor(float multiplier, string tag = null)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
                return;

            _info.Factor *= multiplier;

            if (!string.IsNullOrEmpty(tag))
                _info.QuirkTags.Add($"*{multiplier:F2} ({tag})");
        }

        /// <summary>
        /// Add a flat value to the current factor
        /// </summary>
        /// <param name="bonus">Bonus to add</param>
        /// <param name="tag">Optional tag for tracking</param>
        public void AddBonus(float bonus, string tag = null)
        {
            if (float.IsNaN(bonus) || float.IsInfinity(bonus))
                return;

            _info.Factor += bonus;

            if (!string.IsNullOrEmpty(tag))
                _info.QuirkTags.Add($"+{bonus:F2} ({tag})");
        }

        /// <summary>
        /// Set the factor to a specific value
        /// </summary>
        /// <param name="newFactor">New factor value</param>
        /// <param name="tag">Optional tag for tracking</param>
        public void SetFactor(float newFactor, string tag = null)
        {
            if (float.IsNaN(newFactor) || float.IsInfinity(newFactor))
                return;

            _info.Factor = newFactor;

            if (!string.IsNullOrEmpty(tag))
                _info.QuirkTags.Add($"={newFactor:F2} ({tag})");
        }

        /// <summary>
        /// Apply a factor if a condition is true
        /// </summary>
        /// <param name="condition">Condition to check</param>
        /// <param name="multiplier">Multiplier to apply if condition is true</param>
        /// <param name="tag">Optional tag for tracking</param>
        public void MultiplyIf(bool condition, float multiplier, string tag = null)
        {
            if (condition)
                MultiplyFactor(multiplier, tag);
        }

        /// <summary>
        /// Apply a bonus if a condition is true
        /// </summary>
        /// <param name="condition">Condition to check</param>
        /// <param name="bonus">Bonus to add if condition is true</param>
        /// <param name="tag">Optional tag for tracking</param>
        public void AddIf(bool condition, float bonus, string tag = null)
        {
            if (condition)
                AddBonus(bonus, tag);
        }

        /// <summary>
        /// Set factor to minimum of current value and ceiling
        /// </summary>
        /// <param name="ceiling">Maximum allowed value</param>
        /// <param name="tag">Optional tag for tracking</param>
        public void ClampMax(float ceiling, string tag = null)
        {
            if (_info.Factor > ceiling)
            {
                _info.Factor = ceiling;
                if (!string.IsNullOrEmpty(tag))
                    _info.QuirkTags.Add($"max({ceiling:F2}) ({tag})");
            }
        }

        /// <summary>
        /// Set factor to maximum of current value and floor
        /// </summary>
        /// <param name="floor">Minimum allowed value</param>
        /// <param name="tag">Optional tag for tracking</param>
        public void ClampMin(float floor, string tag = null)
        {
            if (_info.Factor < floor)
            {
                _info.Factor = floor;
                if (!string.IsNullOrEmpty(tag))
                    _info.QuirkTags.Add($"min({floor:F2}) ({tag})");
            }
        }

        /// <summary>
        /// Clamp factor to a range
        /// </summary>
        /// <param name="min">Minimum value</param>
        /// <param name="max">Maximum value</param>
        /// <param name="tag">Optional tag for tracking</param>
        public void ClampRange(float min, float max, string tag = null)
        {
            float oldFactor = _info.Factor;
            _info.Factor = Math.Max(min, Math.Min(max, _info.Factor));

            if (Math.Abs(_info.Factor - oldFactor) > 0.001f && !string.IsNullOrEmpty(tag))
                _info.QuirkTags.Add($"clamp({min:F2}-{max:F2}) ({tag})");
        }

        /// <summary>
        /// Check if tool has a specific mod extension
        /// </summary>
        /// <typeparam name="T">Mod extension type</typeparam>
        /// <returns>True if tool has the extension</returns>
        public bool HasModExtension<T>() where T : DefModExtension
        {
            return ToolDef?.GetModExtension<T>() != null;
        }

        /// <summary>
        /// Check if stuff has a specific mod extension
        /// </summary>
        /// <typeparam name="T">Mod extension type</typeparam>
        /// <returns>True if stuff has the extension</returns>
        public bool StuffHasModExtension<T>() where T : DefModExtension
        {
            return StuffDef?.GetModExtension<T>() != null;
        }

        /// <summary>
        /// Check if tool label contains specific text (case-insensitive)
        /// </summary>
        /// <param name="text">Text to search for</param>
        /// <returns>True if tool label contains the text</returns>
        public bool ToolLabelContains(string text)
        {
            return !string.IsNullOrEmpty(ToolDef?.label) &&
                   ToolDef.label.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Check if stuff label contains specific text (case-insensitive)
        /// </summary>
        /// <param name="text">Text to search for</param>
        /// <returns>True if stuff label contains the text</returns>
        public bool StuffLabelContains(string text)
        {
            return !string.IsNullOrEmpty(StuffDef?.label) &&
                   StuffDef.label.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Check tech level match
        /// </summary>
        /// <param name="techLevel">Tech level to check</param>
        /// <returns>True if tool matches tech level</returns>
        public bool IsTechLevel(TechLevel techLevel)
        {
            return ToolDef?.techLevel == techLevel;
        }

        /// <summary>
        /// Check if tech level is at least the specified level
        /// </summary>
        /// <param name="minTechLevel">Minimum tech level</param>
        /// <returns>True if tool tech level is at least the minimum</returns>
        public bool IsTechLevelAtLeast(TechLevel minTechLevel)
        {
            return ToolDef?.techLevel >= minTechLevel;
        }

        /// <summary>
        /// Add a developer note without affecting stats (for debugging/tracking)
        /// </summary>
        /// <param name="note">Development note to add</param>
        public void AddDevNote(string note)
        {
            if (!string.IsNullOrEmpty(note))
                _info.QuirkTags.Add($"[{note}]");
        }

        /// <summary>
        /// Get all applied quirk tags for debugging
        /// </summary>
        /// <returns>List of quirk modification tags</returns>
        internal List<string> GetQuirkTags()
        {
            return new List<string>(_info.QuirkTags);
        }

        /// <summary>
        /// Get quirk summary for display
        /// </summary>
        /// <returns>Formatted string of all quirk modifications</returns>
        internal string GetQuirkSummary()
        {
            return _info.QuirkTags.Count > 0 ? string.Join(", ", _info.QuirkTags) : null;
        }
    }
}