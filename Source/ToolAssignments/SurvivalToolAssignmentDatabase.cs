//Rimworld 1.6 / C# 7.3
//SurvivalToolsAssignmentDatabase.cs
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
            if (toolAssignment == null) return AcceptanceReport.WasRejected;

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
            // --- Default "General Worker" (first entry) ---
            var staGeneral = MakeNewSurvivalToolAssignment();
            staGeneral.label = "General Worker";
            staGeneral.filter.SetDisallowAll();

            // Include EVERYTHING that has SurvivalToolProperties (real tools + tool-stuffs)
            foreach (ThingDef tDef in DefDatabase<ThingDef>.AllDefs)
            {
                var props = tDef.GetModExtension<SurvivalToolProperties>();
                if (props?.baseWorkStatFactors?.Any() == true)
                    staGeneral.filter.SetAllow(tDef, true);
            }

            // --- “Anything” ---
            var staAnything = MakeNewSurvivalToolAssignment();
            staAnything.label = "OutfitAnything".Translate();
            staAnything.filter.SetDisallowAll();
            // Allow all survival-tool defs + tool-stuffs (anything with SurvivalToolProperties)
            foreach (ThingDef tDef in DefDatabase<ThingDef>.AllDefs)
            {
                var props = tDef.GetModExtension<SurvivalToolProperties>();
                if (props?.baseWorkStatFactors?.Any() == true)
                    staAnything.filter.SetAllow(tDef, true);
            }

            // --- Category assignments (only if tools exist for that tag) ---
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

            foreach (var pair in assignmentsToCreate)
            {
                var tag = pair.tag;
                var labelKey = pair.labelKey;

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

            // Assign tools to created tag sets
            foreach (ThingDef tDef in DefDatabase<ThingDef>.AllDefs)
            {
                var toolProps = tDef.GetModExtension<SurvivalToolProperties>();
                var tags = toolProps?.defaultSurvivalToolAssignmentTags;
                if (tags == null) continue;

                foreach (var tag in tags)
                    if (createdAssignments.TryGetValue(tag, out var assignment))
                        assignment.filter.SetAllow(tDef, true);
            }

            var staNothing = MakeNewSurvivalToolAssignment();
            staNothing.label = "FoodRestrictionNothing".Translate();
            staNothing.filter.SetDisallowAll();
        }
    }
}
