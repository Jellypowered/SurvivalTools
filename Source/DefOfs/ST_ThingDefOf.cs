// Rimworld 1.6 / C# 7.3
// Source/DefOfs/ST_ThingDefOf.cs
using Verse;
using RimWorld;

namespace SurvivalTools
{
    [DefOf]
    public static class ST_ThingDefOf
    {
        // -------------------------------
        // Custom SurvivalTools ThingDefs
        // -------------------------------

        public static ThingDef SurvivalTools_Multitool; // advanced spacer-tier survival tool

        static ST_ThingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_ThingDefOf));
        }
    }
}
