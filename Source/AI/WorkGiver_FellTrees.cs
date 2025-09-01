using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools
{
    public class WorkGiver_FellTrees : WorkGiver_Scanner
    {
        public override Danger MaxPathDanger(Pawn pawn) => Danger.Deadly;

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.designationManager.AllDesignations
                .Where(des => (des.def == DesignationDefOf.CutPlant || des.def == DesignationDefOf.HarvestPlant)
                             && des.target.HasThing
                             && !des.target.Thing.Destroyed)
                .Select(des => des.target.Thing);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var plant = t as Plant;
            if (plant == null || plant.Destroyed) return null;
            if (plant.def?.plant?.IsTree != true) return null;

            // Basic access checks
            if (t.IsForbidden(pawn)) return null;
            if (t.IsBurning()) return null;

            // Reserve target
            if (!pawn.CanReserve(t, 1, -1, null, forced)) return null;

            // Prefer "on-thing" designations; this avoids scanning the entire designation list again.
            var desigs = pawn.Map.designationManager.AllDesignationsOn(t);
            foreach (var des in desigs)
            {
                if (des.def == DesignationDefOf.HarvestPlant)
                {
                    if (!plant.HarvestableNow) return null;
                    return new Job(ST_JobDefOf.HarvestTreeDesignated, t);
                }

                if (des.def == DesignationDefOf.CutPlant)
                {
                    return new Job(ST_JobDefOf.FellTreeDesignated, t);
                }
            }

            return null;
        }
    }
}
