using Verse;
using RimWorld;

namespace SurvivalTools
{
    [DefOf]
    public static class ST_ConceptDefOf
    {
        public static ConceptDef UsingSurvivalTools;
        public static ConceptDef SurvivalToolDegradation;

        static ST_ConceptDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_ConceptDefOf));
        }
    }
}

