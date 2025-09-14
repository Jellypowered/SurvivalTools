// RimWorld 1.6 / C# 7.3
// Source/AI/JobDriver_HarvestTree_Designated.cs
using RimWorld;
using Verse;

namespace SurvivalTools
{
    // Uses the standard HarvestPlant designation for designated tree harvesting.
    public class JobDriver_HarvestTree_Designated : JobDriver_HarvestTree
    {
        protected override DesignationDef RequiredDesignation => DesignationDefOf.HarvestPlant;
    }
}
