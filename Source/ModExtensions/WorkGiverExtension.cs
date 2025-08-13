using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SurvivalTools
{
    public class WorkGiverExtension : DefModExtension
    {
        // Stats a pawn must meet to perform this WorkGiver. Keep non-null for easy consumption.
        public List<StatDef> requiredStats = new List<StatDef>();
    }
}
