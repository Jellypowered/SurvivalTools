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
        public static StatDef SowingSpeed;
        public static StatDef TreeFellingSpeed;
        public static StatDef MaintenanceSpeed;
        public static StatDef DeconstructionSpeed;
        public static StatDef ResearchSpeed;
        public static StatDef CleaningSpeed;
        public static StatDef MedicalOperationSpeed;
        public static StatDef MedicalSurgerySuccessChance;
        public static StatDef ButcheryFleshSpeed;
        public static StatDef ButcheryFleshEfficiency;

        // Thing
        public static StatDef ToolEstimatedLifespan;
        public static StatDef ToolEffectivenessFactor;

        static ST_StatDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_StatDefOf));
        }
    }
}
