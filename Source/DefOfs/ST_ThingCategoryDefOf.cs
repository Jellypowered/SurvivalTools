using Verse;
using RimWorld;

namespace SurvivalTools
{
    [DefOf]
    public static class ST_ThingCategoryDefOf
    {
        public static ThingCategoryDef SurvivalTools;
        public static ThingCategoryDef SurvivalToolsNeolithic;
        public static ThingCategoryDef SurvivalToolsIndustrial;
        public static ThingCategoryDef SurvivalToolsSpacer;

        static ST_ThingCategoryDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_ThingCategoryDefOf));
        }
    }
}
