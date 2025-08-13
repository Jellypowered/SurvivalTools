using Verse;

namespace SurvivalTools
{
    public class SurvivalToolAssignment : IExposable, ILoadReferenceable
    {
        public int uniqueId;
        public string label;
        public ThingFilter filter = new ThingFilter();

        public SurvivalToolAssignment() { }

        public SurvivalToolAssignment(int uniqueId, string label)
        {
            this.uniqueId = uniqueId;
            this.label = label;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref uniqueId, "uniqueId", 0);
            Scribe_Values.Look(ref label, "label", "Unnamed");
            Scribe_Deep.Look(ref filter, "filter", new object[] { });

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Defensive: avoid nulls from old saves or mod changes
                if (label.NullOrEmpty()) label = "Unnamed";
                if (filter == null) filter = new ThingFilter();
            }
        }

        // Keep the load ID stable even if the label changes
        public string GetUniqueLoadID()
        {
            return "SurvivalToolAssignment_" + uniqueId;
        }
    }
}
