using Verse;
using RimWorld;

namespace SurvivalTools
{
    [DefOf]
    public static class ST_StatCategoryDefOf
    {
        public static StatCategoryDef SurvivalTool;
        public static StatCategoryDef SurvivalToolMaterial;

        static ST_StatCategoryDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_StatCategoryDefOf));
        }
    }
}

