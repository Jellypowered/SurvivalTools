using Verse;
using RimWorld;

namespace SurvivalTools
{
    [DefOf]
    public static class ST_JobDefOf
    {
        public static JobDef FellTree;
        public static JobDef FellTreeDesignated;

        public static JobDef HarvestTree;
        public static JobDef HarvestTreeDesignated;

        public static JobDef DropSurvivalTool;

        static ST_JobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_JobDefOf));
        }
    }
}

