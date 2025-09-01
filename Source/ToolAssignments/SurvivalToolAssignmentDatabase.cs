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
            Scribe_Collections.Look(ref survivalToolAssignments, "survivalToolAssignments", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (survivalToolAssignments == null) survivalToolAssignments = new List<SurvivalToolAssignment>();

                // Defensive: ensure filters/labels exist on all entries
                foreach (var assignment in survivalToolAssignments)
                {
                    if (assignment != null)
                    {
                        if (assignment.filter == null) assignment.filter = new ThingFilter();
                        if (assignment.label.NullOrEmpty()) assignment.label = "Unnamed";
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
            // Create a comprehensive "General Worker" assignment as the default
            var staGeneral = MakeNewSurvivalToolAssignment();
            staGeneral.label = "General Worker"; // This will be the default assignment
            staGeneral.filter.SetDisallowAll();

            // Add ALL tool types to the default assignment - let colonists use any appropriate tool
            foreach (ThingDef tDef in DefDatabase<ThingDef>.AllDefs)
            {
                var toolProps = tDef.GetModExtension<SurvivalToolProperties>();
                var tags = toolProps?.defaultSurvivalToolAssignmentTags;
                if (tags == null) continue;

                // Include ALL tools in the default assignment - this allows maximum flexibility
                // Colonists will automatically pick up and use any tool that helps their assigned work
                staGeneral.filter.SetAllow(tDef, true);
            }

            var staAnything = MakeNewSurvivalToolAssignment();
            staAnything.label = "OutfitAnything".Translate();

            // Create assignments for each category, but only if tools exist for that category
            var assignmentsToCreate = new List<(string tag, string labelKey)>
            {
                ("Constructor", "SurvivalToolAssignmentConstructor"),
                ("Miner", "SurvivalToolAssignmentMiner"),
                ("PlantWorker", "SurvivalToolAssignmentPlantWorker"),
                ("Researcher", "SurvivalToolAssignmentResearcher"),
                ("Cleaner", "SurvivalToolAssignmentCleaner"),
                ("Medical", "SurvivalToolAssignmentMedical"),
                ("Butcher", "SurvivalToolAssignmentButcher")
            };

            var createdAssignments = new Dictionary<string, SurvivalToolAssignment>();

            foreach (var (tag, labelKey) in assignmentsToCreate)
            {
                // Check if any tools exist with this tag
                bool hasToolsForTag = DefDatabase<ThingDef>.AllDefs.Any(tDef =>
                {
                    var toolProps = tDef.GetModExtension<SurvivalToolProperties>();
                    return toolProps?.defaultSurvivalToolAssignmentTags?.Contains(tag) == true;
                });

                if (hasToolsForTag)
                {
                    var assignment = MakeNewSurvivalToolAssignment();
                    assignment.label = labelKey.Translate();
                    assignment.filter.SetDisallowAll();
                    createdAssignments[tag] = assignment;
                }
            }

            // Now assign tools to the created assignments
            foreach (ThingDef tDef in DefDatabase<ThingDef>.AllDefs)
            {
                var toolProps = tDef.GetModExtension<SurvivalToolProperties>();
                var tags = toolProps?.defaultSurvivalToolAssignmentTags;
                if (tags == null) continue;

                foreach (var tag in tags)
                {
                    if (createdAssignments.TryGetValue(tag, out var assignment))
                    {
                        assignment.filter.SetAllow(tDef, true);
                    }
                }
            }

            var staNothing = MakeNewSurvivalToolAssignment();
            staNothing.label = "FoodRestrictionNothing".Translate();
            staNothing.filter.SetDisallowAll();
        }
    }
}
