using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SurvivalTools
{
    public class WorkGiverExtension : DefModExtension
    {
        public static readonly WorkGiverExtension defaultValues = new WorkGiverExtension();

        // Stats a pawn must meet to perform this WorkGiver. Keep non-null for easy consumption.
        public List<StatDef> requiredStats = new List<StatDef>();

        /// <summary>
        /// Convenience: get the extension for a WorkGiver def, or defaultValues if absent.
        /// </summary>
        public static WorkGiverExtension For(WorkGiverDef def)
        {
            return def?.GetModExtension<WorkGiverExtension>() ?? defaultValues;
        }

        /// <summary>
        /// Check if this work giver has any required stats defined.
        /// </summary>
        public bool HasRequiredStats => requiredStats?.Count > 0;

        /// <summary>
        /// Check if a specific stat is required by this work giver.
        /// </summary>
        public bool RequiresStat(StatDef stat)
        {
            return stat != null && requiredStats?.Contains(stat) == true;
        }

        /// <summary>
        /// Get all required stats as a read-only enumerable.
        /// </summary>
        public IEnumerable<StatDef> GetRequiredStats()
        {
            return requiredStats?.AsEnumerable() ?? Enumerable.Empty<StatDef>();
        }
    }
}
