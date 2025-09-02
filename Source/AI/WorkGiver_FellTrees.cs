//Rimworld 1.6 / C# 7.3
//WorkGiver_FellTrees.cs
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
            // Guard against null map (caller should normally ensure pawn.Map is non-null, but be defensive)
            var map = pawn?.Map;
            if (map == null) yield break;

            var desigs = map.designationManager?.AllDesignations;
            if (desigs == null) yield break;

            // Prevent duplicates (multiple designations pointing to same thing)
            var seen = new HashSet<Thing>();

            foreach (var des in desigs)
            {
                if (des == null) continue;

                // Only interested in plant-harvest / cut designations on things
                if (des.target == null || !des.target.HasThing) continue;
                if (des.def != DesignationDefOf.CutPlant && des.def != DesignationDefOf.HarvestPlant) continue;

                var thing = des.target.Thing;
                if (thing == null || thing.Destroyed) continue;

                // Only plants that are trees are relevant
                var plant = thing as Plant;
                if (plant == null || plant.def?.plant?.IsTree != true) continue;

                if (seen.Add(thing))
                    yield return thing;
            }
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (pawn == null || t == null) return null;

            var plant = t as Plant;
            if (plant == null || plant.Destroyed) return null;
            if (plant.def?.plant?.IsTree != true) return null;

            // Basic checks
            if (t.IsForbidden(pawn)) return null;
            if (t.IsBurning()) return null;

            // Reserve + reach check (safer than reserve-only)
            if (!pawn.CanReserveAndReach(t, PathEndMode.Touch, pawn.NormalMaxDanger(), 1, -1, null))
                return null;

            // Prefer designations directly on the thing (avoid scanning global list again)
            var desigsOnThing = pawn.Map?.designationManager?.AllDesignationsOn(t);
            if (desigsOnThing != null)
            {
                foreach (var des in desigsOnThing)
                {
                    if (des == null) continue;

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
            }

            return null;
        }
    }
}
