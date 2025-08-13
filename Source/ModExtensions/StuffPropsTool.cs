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
    }
}
