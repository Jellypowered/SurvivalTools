// Rimworld 1.6 / C# 7.3
// Source/ToolAssignments/SurvivalToolAssignment.cs
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
            this.label = string.IsNullOrEmpty(label) ? "Unnamed" : label;
            if (filter == null) filter = new ThingFilter();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref uniqueId, "uniqueId", 0);
            Scribe_Values.Look(ref label, "label", "Unnamed");
            Scribe_Deep.Look(ref filter, "filter");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (string.IsNullOrEmpty(label)) label = "Unnamed";
                if (filter == null) filter = new ThingFilter();
            }
        }

        // ILoadReferenceable: keep a stable reference key independent of label changes
        public string GetUniqueLoadID() => $"SurvivalToolAssignment_{uniqueId}";

        public override string ToString() => $"{label} (ID: {uniqueId})";

        // QoL helpers (non-breaking)
        public string LabelCap => (label ?? "Unnamed").CapitalizeFirst();
        public bool Allows(Thing t) => filter != null && t != null && filter.Allows(t);
        public void Rename(string newLabel) => label = string.IsNullOrEmpty(newLabel) ? "Unnamed" : newLabel.Trim();
    }
}
