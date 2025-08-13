using Verse;
using RimWorld;

namespace SurvivalTools
{
    [DefOf]
    public static class ST_RecipeDefOf
    {
        public static RecipeDef SmeltSurvivalTool;
        public static RecipeDef DestroySurvivalTool;

        public static RecipeDef SmeltWeapon;
        public static RecipeDef DestroyWeapon;

        static ST_RecipeDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ST_RecipeDefOf));
        }
    }
}

