using Verse;
using RimWorld;

namespace SurvivalTools
{
    [DefOf]
    public static class ST_ThingDefOf
    {
        public static ThingDef SurvivalTools_Multitool;

        static ST_ThingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_ThingDefOf));
        }
    }
}
