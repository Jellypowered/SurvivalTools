// RimWorld 1.6 / C# 7.3
// Source/ModExtensions/StuffPropsTool.cs
using System.Linq;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SurvivalTools
{
    public class StuffPropsTool : DefModExtension
    {
        public static readonly StuffPropsTool defaultValues = new StuffPropsTool();

        // Factors applied to survival tool stats when this stuff is used.
        // Initialize to avoid null refs in consumers.
        public List<StatModifier> toolStatFactors = new List<StatModifier>();

        // Multiplier for tool wear when made from this stuff (1 = normal).
        public float wearFactorMultiplier = 1f;

        /// <summary>
        /// Convenience: get the extension for a stuff def, or defaultValues if absent.
        /// </summary>
        public static StuffPropsTool For(ThingDef stuffDef)
        {
            return stuffDef?.GetModExtension<StuffPropsTool>() ?? defaultValues;
        }

        /// <summary>
        /// Check if this stuff has any tool stat factors defined.
        /// </summary>
        public bool HasToolStatFactors => toolStatFactors?.Count > 0;

        /// <summary>
        /// Get the modifier for a specific stat, or null if not found.
        /// </summary>
        public StatModifier GetStatModifier(StatDef stat)
        {
            return toolStatFactors?.FirstOrDefault(m => m.stat == stat);
        }

        /// <summary>
        /// Get the factor value for a specific stat (1.0 if not found).
        /// </summary>
        public float GetStatFactor(StatDef stat)
        {
            return GetStatModifier(stat)?.value ?? 1f;
        }
    }
}
