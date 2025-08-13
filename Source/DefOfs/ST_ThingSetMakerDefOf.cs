using Verse;
using RimWorld;

namespace SurvivalTools
{
    [DefOf]
    public static class ST_ThingSetMakerDefOf
    {
        public static ThingSetMakerDef MapGen_AncientRuinsSurvivalTools;

        static ST_ThingSetMakerDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_ThingSetMakerDefOf));
        }
    }
}
