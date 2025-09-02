//Rimworld 1.6 / C# 7.3
//SurvivalToolProperties.cs
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SurvivalTools
{
    public class SurvivalToolProperties : DefModExtension
    {
        public static readonly SurvivalToolProperties defaultValues = new SurvivalToolProperties();

        // Base stat factors a tool contributes (e.g., TreeFellingSpeed, DiggingSpeed)
        // Initialize to avoid null checks everywhere.
        public List<StatModifier> baseWorkStatFactors = new List<StatModifier>();

        // Power multipliers applied to base stats based on material power factor
        // Used to make better materials (devilstrand, hyperweave) more effective
        public List<StatModifier> stuffPowerMultiplier = new List<StatModifier>();

        // Tags used by default survival tool assignments (untranslated keys/ids)
        [NoTranslate]
        public List<string> defaultSurvivalToolAssignmentTags = new List<string>();

        // Multiplier for wear rate (1 = normal)
        public float toolWearFactor = 1f;

        /// <summary>
        /// Convenience: get SurvivalToolProperties from a ThingDef (or defaultValues if none).
        /// </summary>
        public static SurvivalToolProperties For(ThingDef def)
        {
            return def?.GetModExtension<SurvivalToolProperties>() ?? defaultValues;
        }

        /// <summary>
        /// Check if this tool has any work stat factors defined.
        /// </summary>
        public bool HasWorkStatFactors => baseWorkStatFactors?.Count > 0;

        /// <summary>
        /// Check if this tool has any assignment tags defined.
        /// </summary>
        public bool HasAssignmentTags => defaultSurvivalToolAssignmentTags?.Count > 0;

        /// <summary>
        /// Get the modifier for a specific stat, or null if not found.
        /// </summary>
        public StatModifier GetStatModifier(StatDef stat)
        {
            return baseWorkStatFactors?.FirstOrDefault(m => m.stat == stat);
        }

        /// <summary>
        /// Get the factor value for a specific stat (1.0 if not found).
        /// </summary>
        public float GetStatFactor(StatDef stat)
        {
            return GetStatModifier(stat)?.value ?? 1f;
        }

        /// <summary>
        /// Check if this tool has a specific assignment tag.
        /// </summary>
        public bool HasAssignmentTag(string tag)
        {
            return !string.IsNullOrEmpty(tag) && defaultSurvivalToolAssignmentTags?.Contains(tag) == true;
        }
    }
}

