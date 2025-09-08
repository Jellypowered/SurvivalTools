// Rimworld 1.6 / C# 7.3
// Source/DefOfs/ST_ThingSetMakerDefOf.cs
using Verse;
using RimWorld;

namespace SurvivalTools
{
    [DefOf]
    public static class ST_ThingSetMakerDefOf
    {
        // -------------------------------
        // Custom SurvivalTools ThingSetMakers
        // -------------------------------

        public static ThingSetMakerDef MapGen_AncientRuinsSurvivalTools;
        // Custom: spawns survival tools as part of ancient ruins map generation

        static ST_ThingSetMakerDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_ThingSetMakerDefOf));
        }
    }
}

