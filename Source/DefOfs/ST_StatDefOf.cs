// Rimworld 1.6 / C# 7.3
// Source/DefOfs/ST_StatDefOf.cs
using Verse;
using RimWorld;

namespace SurvivalTools
{
    [DefOf]
    public static class ST_StatDefOf
    {
        // -------------------------------
        // Pawn-related stats
        // -------------------------------

        // Custom (SurvivalTools)
        public static StatDef SurvivalToolCarryCapacity;
        public static StatDef DiggingSpeed;
        public static StatDef MiningYieldDigging;
        public static StatDef PlantHarvestingSpeed;
        public static StatDef SowingSpeed;
        public static StatDef TreeFellingSpeed;
        public static StatDef MaintenanceSpeed;
        public static StatDef DeconstructionSpeed;

        // Vanilla (fallback for compat)
        public static StatDef ResearchSpeed;
        public static StatDef CleaningSpeed;
        public static StatDef MedicalOperationSpeed;           // vanilla
        public static StatDef MedicalSurgerySuccessChance;     // vanilla
        public static StatDef ButcheryFleshSpeed;              // vanilla
        public static StatDef ButcheryFleshEfficiency;         // vanilla
        public static StatDef WorkSpeedGlobal;                 // vanilla

        // -------------------------------
        // Thing-related stats
        // -------------------------------

        // Custom (SurvivalTools)
        public static StatDef ToolEstimatedLifespan;
        public static StatDef ToolEffectivenessFactor;

        static ST_StatDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_StatDefOf));
        }
    }
}
