//Rimworld 1.6 / C# 7.3
// Source/DefOfs/ST_ConceptDefOf.cs
using Verse;
using RimWorld;

/// <summary>
/// ConceptDefs used by SurvivalTools.
/// Initialized automatically by RimWorld's DefOf system.
/// </summary>
[DefOf]
public static class ST_ConceptDefOf
{
    /// <summary>Concept tutorial shown when pawns first use survival tools.</summary>
    public static ConceptDef UsingSurvivalTools;

    /// <summary>Concept tutorial explaining tool degradation mechanics.</summary>
    public static ConceptDef SurvivalToolDegradation;

    static ST_ConceptDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(ST_ConceptDefOf));
    }
}


