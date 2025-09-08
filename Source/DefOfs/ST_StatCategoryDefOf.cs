// Rimworld 1.6 / C# 7.3
// Source/DefOfs/ST_StatCategoryDefOf.cs
using Verse;
using RimWorld;

namespace SurvivalTools
{
    /// <summary>
    /// Stat categories used by SurvivalTools.
    /// These group survival tool stats into logical sections in the UI.
    /// </summary>
    [DefOf]
    public static class ST_StatCategoryDefOf
    {
        /// <summary>
        /// General survival tool stats (e.g. efficiency, wear).
        /// </summary>
        public static StatCategoryDef SurvivalTool;

        /// <summary>
        /// Material-based survival tool stats (e.g. durability, modifiers).
        /// </summary>
        public static StatCategoryDef SurvivalToolMaterial;

        static ST_StatCategoryDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_StatCategoryDefOf));
        }
    }
}


