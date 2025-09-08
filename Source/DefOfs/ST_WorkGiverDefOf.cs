// Rimworld 1.6 / C# 7.3
// Source/DefOfs/ST_WorkGiverDefOf.cs
using Verse;
using RimWorld;

namespace SurvivalTools
{
    [DefOf]
    public static class ST_WorkGiverDefOf
    {
        // -------------------------------
        // Custom SurvivalTools WorkGivers
        // -------------------------------

        public static WorkGiverDef FellTrees;
        // Custom: SurvivalTools WorkGiver for tree felling jobs 
        // (conflicts with Separate Tree Chopping if both enabled, handled by compat layer)

        static ST_WorkGiverDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_WorkGiverDefOf));
        }
    }
}


