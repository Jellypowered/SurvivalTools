using System.Collections.Generic;
using Verse;

namespace SurvivalTools
{
    public class SurvivalToolForcedHandler : IExposable
    {
        private List<Thing> forcedTools = new List<Thing>();

        public void ExposeData()
        {
            Scribe_Collections.Look(ref forcedTools, "forcedTools", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (forcedTools == null) forcedTools = new List<Thing>();
                // Drop null/destroyed refs from old saves or removed items
                for (int i = forcedTools.Count - 1; i >= 0; i--)
                {
                    var t = forcedTools[i];
                    if (t == null || t.Destroyed)
                        forcedTools.RemoveAt(i);
                }
            }
        }

        public bool IsForced(Thing tool)
        {
            if (tool == null) return false;

            if (tool.Destroyed)
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                {
                    Log.Error($"SurvivalTool was forced while Destroyed: {tool}");
                }
                forcedTools?.Remove(tool);
                return false;
            }
            return forcedTools != null && forcedTools.Contains(tool);
        }

        public void SetForced(Thing tool, bool forced)
        {
            if (tool == null) return;

            if (forced)
            {
                if (!tool.Destroyed && !forcedTools.Contains(tool))
                    forcedTools.Add(tool);
            }
            else
            {
                forcedTools.Remove(tool);
            }
        }

        public bool AllowedToAutomaticallyDrop(Thing tool) => !IsForced(tool);

        public void Reset() => forcedTools.Clear();

        public List<Thing> ForcedTools => forcedTools;

        public bool SomethingForced => forcedTools != null && forcedTools.Count > 0;
    }
}
