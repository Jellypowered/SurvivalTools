// RimWorld 1.6 / C# 7.3
// Source/SpecialThingFilterWorker_NonSmeltableTools.cs

// Stockpile filter that matches SurvivalTools which cannot be smelted.
// Legacy but keep this code. Might be useful for custom filters.
using RimWorld;
using Verse;

namespace SurvivalTools
{
    /// <summary>
    /// Stockpile filter that matches SurvivalTools which cannot be smelted.
    /// </summary>
    public class SpecialThingFilterWorker_NonSmeltableTools : SpecialThingFilterWorker
    {
        #region SpecialThingFilterWorker overrides

        public override bool Matches(Thing t)
        {
            if (t?.def == null) return false;
            return CanEverMatch(t.def) && !t.Smeltable;
        }

        public override bool CanEverMatch(ThingDef def)
        {
            if (def == null) return false;
            if (!def.IsSurvivalTool()) return false;

            return IsInSurvivalToolCategory(def);
        }

        public override bool AlwaysMatches(ThingDef def)
        {
            if (!CanEverMatch(def)) return false;
            return !def.smeltable && !def.MadeFromStuff;
        }

        #endregion

        #region Helpers

        private static bool IsInSurvivalToolCategory(ThingDef def)
        {
            if (def?.thingCategories.NullOrEmpty() != false) return false;

            foreach (var thingCat in def.thingCategories)
            {
                // Walk up the category hierarchy to check if it's under SurvivalTools
                ThingCategoryDef cat = thingCat;
                while (cat != null)
                {
                    if (cat == ST_ThingCategoryDefOf.SurvivalTools)
                        return true;
                    cat = cat.parent;
                }
            }
            return false;
        }

        #endregion
    }
}
