// RimWorld 1.6 / C# 7.3
// JobDriver_FellTree_Designated.cs
using RimWorld;
using Verse;

namespace SurvivalTools
{
    /// <summary>
    /// Uses the standard CutPlant designation for designated tree felling.
    /// Subclass of JobDriver_FellTree that requires the CutPlant designation to operate.
    /// </summary>
    public class JobDriver_FellTree_Designated : JobDriver_FellTree
    {
        protected override DesignationDef RequiredDesignation
        {
            get
            {
                // Defensive: return the vanilla CutPlant designation if present.
                // This is extremely unlikely to be null, but the guard avoids a possible NRE in exotic contexts.
                return DesignationDefOf.CutPlant ?? null;
            }
        }
    }
}
