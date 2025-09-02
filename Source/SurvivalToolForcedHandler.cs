// RimWorld 1.6 / C# 7.3
// Source/SurvivalToolForcedHandler.cs
using System.Collections.Generic;
using Verse;

namespace SurvivalTools
{
    /// <summary>
    /// Tracks tools the player has explicitly "forced" a pawn to keep.
    /// </summary>
    public class SurvivalToolForcedHandler : IExposable
    {
        #region Fields

        // We deliberately store Thing references; RimWorld's cross-ref resolver will rebind them on load.
        private List<Thing> forcedTools = new List<Thing>();

        #endregion

        #region Scribe

        public void ExposeData()
        {
            Scribe_Collections.Look(ref forcedTools, "forcedTools", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (forcedTools == null) forcedTools = new List<Thing>();

                // Prune stale entries (null/destroyed) that can appear in old saves or when items were removed.
                for (int i = forcedTools.Count - 1; i >= 0; i--)
                {
                    var t = forcedTools[i];
                    if (t == null || t.Destroyed)
                        forcedTools.RemoveAt(i);
                }
            }
        }

        #endregion

        #region Queries

        public bool IsForced(Thing tool)
        {
            if (tool == null) return false;

            // If a destroyed thing remains, quietly remove it and treat as not forced.
            if (tool.Destroyed)
            {
                forcedTools?.Remove(tool);
                return false;
            }

            return forcedTools != null && forcedTools.Contains(tool);
        }

        public bool AllowedToAutomaticallyDrop(Thing tool) => !IsForced(tool);

        public bool SomethingForced => forcedTools != null && forcedTools.Count > 0;

        #endregion

        #region Mutators

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

        public void Reset() => forcedTools?.Clear();

        #endregion

        #region Accessors

        // Expose the backing list for existing code that expects a mutable list.
        public List<Thing> ForcedTools => forcedTools;

        #endregion
    }
}
