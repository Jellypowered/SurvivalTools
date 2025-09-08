// Rimworld 1.6 / C# 7.3
// Source/DefOfs/ST_JobDefOf.cs
using Verse;
using RimWorld;

/// <summary>
/// JobDefs used by SurvivalTools.
/// Initialized automatically by RimWorld's DefOf system.
/// </summary>
[DefOf]
public static class ST_JobDefOf
{
    /// <summary>Job: Fell a tree using survival tools (non-designated).</summary>
    public static JobDef FellTree;

    /// <summary>Job: Fell a tree that has been explicitly designated.</summary>
    public static JobDef FellTreeDesignated;

    /// <summary>Job: Harvest usable wood from a tree (non-designated).</summary>
    public static JobDef HarvestTree;

    /// <summary>Job: Harvest usable wood from a designated tree.</summary>
    public static JobDef HarvestTreeDesignated;

    /// <summary>Job: Drop a carried survival tool.</summary>
    public static JobDef DropSurvivalTool;

    static ST_JobDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(ST_JobDefOf));
    }
}