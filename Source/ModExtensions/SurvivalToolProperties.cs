using System.Collections.Generic;
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
    }
}

