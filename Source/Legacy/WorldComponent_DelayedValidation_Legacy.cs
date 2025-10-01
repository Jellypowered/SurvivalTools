// RimWorld 1.6 / C# 7.3
// Source/Legacy/WorldComponent_DelayedValidation_Legacy.cs
// Backward compatibility stub for old save games that reference WorldComponent_DelayedValidation
// This was renamed to ST_WorldComponent_DelayedValidation and moved to ST_GameComponents.cs
// This stub allows old saves to load without errors

using RimWorld.Planet;
using Verse;

namespace SurvivalTools
{
    /// <summary>
    /// Legacy stub for save game compatibility.
    /// This class was renamed to ST_WorldComponent_DelayedValidation in ST_GameComponents.cs.
    /// This stub prevents errors when loading old saves that still reference the old class name.
    /// </summary>
    public class WorldComponent_DelayedValidation : WorldComponent
    {
        public WorldComponent_DelayedValidation(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // Discard any saved data - we don't need it
            // The new ST_WorldComponent_DelayedValidation will handle validation properly
        }

        // This component does nothing - it exists only to prevent save load errors
        // The actual validation is now handled by ST_WorldComponent_DelayedValidation
    }
}
