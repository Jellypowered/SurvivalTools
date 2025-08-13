using System.Linq;
using Verse;
using RimWorld;
using System.Collections.Generic;

namespace SurvivalTools
{
    public class SurvivalToolAssignmentDatabase : GameComponent
    {
        private bool initialized = false;
        private List<SurvivalToolAssignment> survivalToolAssignments = new List<SurvivalToolAssignment>();

        public SurvivalToolAssignmentDatabase(Game game) { }

        public override void FinalizeInit()
        {
            if (!initialized)
            {
                GenerateStartingSurvivalToolAssignments();
                initialized = true;
            }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Collections.Look(ref survivalToolAssignments, "survivalToolAssignments", LookMode.Deep, new object[] { });

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (survivalToolAssignments == null) survivalToolAssignments = new List<SurvivalToolAssignment>();
                // Defensive: ensure filters/labels exist on all entries
                foreach (var a in survivalToolAssignments)
                {
                    if (a != null)
                    {
                        if (a.filter == null) a.filter = new ThingFilter();
                        if (a.label.NullOrEmpty()) a.label = "Unnamed";
                    }
                }
            }
        }

        public List<SurvivalToolAssignment> AllSurvivalToolAssignments => survivalToolAssignments;

        public SurvivalToolAssignment DefaultSurvivalToolAssignment() =>
            survivalToolAssignments.Count == 0 ? MakeNewSurvivalToolAssignment() : survivalToolAssignments[0];

        public AcceptanceReport TryDelete(SurvivalToolAssignment toolAssignment)
        {
            // Block deletion if ANY alive pawn (on maps, caravans, temp maps, transport pods) is using it
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                var tracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
                if (tracker?.CurrentSurvivalToolAssignment == toolAssignment)
                    return new AcceptanceReport("SurvivalToolAssignmentInUse".Translate(pawn));
            }

            // Clear references on all pawns (alive or dead) so saves don’t keep dangling refs
            foreach (Pawn pawn2 in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
            {
                var tracker = pawn2.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
                if (tracker != null && tracker.CurrentSurvivalToolAssignment == toolAssignment)
                    tracker.CurrentSurvivalToolAssignment = null;
            }

            survivalToolAssignments.Remove(toolAssignment);
            return AcceptanceReport.WasAccepted;
        }

        public SurvivalToolAssignment MakeNewSurvivalToolAssignment()
        {
            int uniqueId = survivalToolAssignments.Any() ? survivalToolAssignments.Max(a => a.uniqueId) + 1 : 1;
            var toolAssignment = new SurvivalToolAssignment(uniqueId, $"{"SurvivalToolAssignment".Translate()} {uniqueId}");
            toolAssignment.filter.SetAllow(ST_ThingCategoryDefOf.SurvivalTools, true);
            survivalToolAssignments.Add(toolAssignment);
            return toolAssignment;
        }

        private void GenerateStartingSurvivalToolAssignments()
        {
            var staAnything = MakeNewSurvivalToolAssignment();
            staAnything.label = "OutfitAnything".Translate();

            var staConstructor = MakeNewSurvivalToolAssignment();
            staConstructor.label = "SurvivalToolAssignmentConstructor".Translate();
            staConstructor.filter.SetDisallowAll();

            var staMiner = MakeNewSurvivalToolAssignment();
            staMiner.label = "SurvivalToolAssignmentMiner".Translate();
            staMiner.filter.SetDisallowAll();

            var staPlantWorker = MakeNewSurvivalToolAssignment();
            staPlantWorker.label = "SurvivalToolAssignmentPlantWorker".Translate();
            staPlantWorker.filter.SetDisallowAll();

            foreach (ThingDef tDef in DefDatabase<ThingDef>.AllDefs)
            {
                var toolProps = tDef.GetModExtension<SurvivalToolProperties>();
                var tags = toolProps?.defaultSurvivalToolAssignmentTags;
                if (tags == null) continue;

                if (tags.Contains("Constructor")) staConstructor.filter.SetAllow(tDef, true);
                if (tags.Contains("Miner")) staMiner.filter.SetAllow(tDef, true);
                if (tags.Contains("PlantWorker")) staPlantWorker.filter.SetAllow(tDef, true);
            }

            var staNothing = MakeNewSurvivalToolAssignment();
            staNothing.label = "FoodRestrictionNothing".Translate();
            staNothing.filter.SetDisallowAll();
        }
    }
}
