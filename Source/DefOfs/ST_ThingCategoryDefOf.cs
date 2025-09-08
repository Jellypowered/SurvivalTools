// Rimworld 1.6 / C# 7.3
// Source/DefOfs/ST_ThingCategoryDefOf.cs
using Verse;
using RimWorld;

namespace SurvivalTools
{
    [DefOf]
    public static class ST_ThingCategoryDefOf
    {
        // -------------------------------
        // Custom SurvivalTools categories
        // -------------------------------

        public static ThingCategoryDef SurvivalTools;           // root category
        public static ThingCategoryDef SurvivalToolsNeolithic;  // primitive / early tech
        public static ThingCategoryDef SurvivalToolsIndustrial; // industrial tier
        public static ThingCategoryDef SurvivalToolsSpacer;     // spacer / advanced tier

        static ST_ThingCategoryDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_ThingCategoryDefOf));
        }
    }
}

