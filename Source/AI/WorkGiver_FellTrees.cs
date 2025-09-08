// RimWorld 1.6 / C# 7.3
// WorkGiver_FellTrees.cs — defensive + unified style
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools
{
    /// <summary>
    /// WorkGiver for tree felling / harvesting when designated.
    /// - Defensive null checks
    /// - Avoids duplicate targets
    /// - Chooses correct job (HarvestTreeDesignated / FellTreeDesignated)
    /// </summary>
    public class WorkGiver_FellTrees : WorkGiver_Scanner
    {
        public override Danger MaxPathDanger(Pawn pawn) => Danger.Deadly;
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            var map = pawn?.Map;
            if (map == null) yield break;

            var desigs = map.designationManager?.AllDesignations;
            if (desigs == null) yield break;

            var seen = new HashSet<Thing>();
            foreach (var des in desigs)
            {
                if (des?.target == null || !des.target.HasThing) continue;

                if (des.def != DesignationDefOf.CutPlant &&
                    des.def != DesignationDefOf.HarvestPlant)
                    continue;

                var thing = des.target.Thing;
                if (thing == null || thing.Destroyed) continue;

                var plant = thing as Plant;
                if (plant?.def?.plant?.IsTree != true) continue;

                if (seen.Add(thing))
                    yield return thing;
            }
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (pawn == null || t == null) return null;

            var plant = t as Plant;
            if (plant == null || plant.Destroyed || plant.def?.plant?.IsTree != true)
                return null;

            if (t.IsForbidden(pawn) || t.IsBurning()) return null;

            if (!pawn.CanReserveAndReach(t, PathEndMode.Touch, pawn.NormalMaxDanger()))
                return null;

            var desigs = pawn.Map?.designationManager?.AllDesignationsOn(t);
            if (desigs != null)
            {
                foreach (var des in desigs)
                {
                    if (des == null) continue;

                    if (des.def == DesignationDefOf.HarvestPlant)
                    {
                        if (!plant.HarvestableNow) return null;
                        LogDebug($"[SurvivalTools.FellTrees] {pawn.LabelShort} starting HarvestTreeDesignated on {plant.LabelShort}", $"FellTrees_Harvest_{pawn.ThingID}");
                        return new Job(ST_JobDefOf.HarvestTreeDesignated, t);
                    }

                    if (des.def == DesignationDefOf.CutPlant)
                    {
                        LogDebug($"[SurvivalTools.FellTrees] {pawn.LabelShort} starting FellTreeDesignated on {plant.LabelShort}", $"FellTrees_Fell_{pawn.ThingID}");
                        return new Job(ST_JobDefOf.FellTreeDesignated, t);
                    }
                }
            }

            return null;
        }
    }
}
