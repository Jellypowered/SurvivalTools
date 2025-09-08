// RimWorld 1.6 / C# 7.3
// Source/DefOfs/ST_RecipeDefOf.cs
using Verse;
using RimWorld;

namespace SurvivalTools
{
    /// <summary>
    /// RecipeDefs used by SurvivalTools.
    /// Initialized automatically by RimWorld's DefOf system.
    /// </summary>
    [DefOf]
    public static class ST_RecipeDefOf
    {
        /// <summary>Recipe: Smelt survival tool into raw materials.</summary>
        public static RecipeDef SmeltSurvivalTool;

        /// <summary>Recipe: Destroy survival tool (no material return).</summary>
        public static RecipeDef DestroySurvivalTool;

        /// <summary>Recipe: Smelt weapon into raw materials.</summary>
        public static RecipeDef SmeltWeapon;

        /// <summary>Recipe: Destroy weapon (no material return).</summary>
        public static RecipeDef DestroyWeapon;

        static ST_RecipeDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_RecipeDefOf));
        }
    }
}
