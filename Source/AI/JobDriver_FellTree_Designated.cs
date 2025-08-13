using RimWorld;
using Verse;

namespace SurvivalTools
{
    // Uses the standard CutPlant designation for designated tree felling.
    public class JobDriver_FellTree_Designated : JobDriver_FellTree
    {
        protected override DesignationDef RequiredDesignation => DesignationDefOf.CutPlant;
    }
}
