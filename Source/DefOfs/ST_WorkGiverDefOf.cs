using Verse;
using RimWorld;

namespace SurvivalTools
{
    [DefOf]
    public static class ST_WorkGiverDefOf
    {
        public static WorkGiverDef FellTrees;

        static ST_WorkGiverDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_WorkGiverDefOf));
        }
    }
}

