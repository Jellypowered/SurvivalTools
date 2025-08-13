using Verse;
using RimWorld;

namespace SurvivalTools
{
    [DefOf]
    public static class ST_StatDefOf
    {
        // Pawn
        public static StatDef SurvivalToolCarryCapacity;
        public static StatDef DiggingSpeed;
        public static StatDef MiningYieldDigging;
        public static StatDef PlantHarvestingSpeed;
        public static StatDef TreeFellingSpeed;

        // Thing
        public static StatDef ToolEstimatedLifespan;
        public static StatDef ToolEffectivenessFactor;

        static ST_StatDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_StatDefOf));
        }
    }
}
