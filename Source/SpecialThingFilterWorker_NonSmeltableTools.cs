using Verse;
using RimWorld;

namespace SurvivalTools
{
    public class SpecialThingFilterWorker_NonSmeltableTools : SpecialThingFilterWorker
    {
        public override bool Matches(Thing t)
        {
            if (t?.def == null) return false;
            return CanEverMatch(t.def) && !t.Smeltable;
        }

        public override bool CanEverMatch(ThingDef def)
        {
            if (def?.IsSurvivalTool() != true) return false;

            return IsInSurvivalToolCategory(def);
        }

        public override bool AlwaysMatches(ThingDef def)
        {
            if (!CanEverMatch(def)) return false;
            return !def.smeltable && !def.MadeFromStuff;
        }

        private static bool IsInSurvivalToolCategory(ThingDef def)
        {
            if (def.thingCategories.NullOrEmpty()) return false;

            foreach (var thingCat in def.thingCategories)
            {
                // Walk up the category hierarchy to check if it's under SurvivalTools
                for (ThingCategoryDef cat = thingCat; cat != null; cat = cat.parent)
                {
                    if (cat == ST_ThingCategoryDefOf.SurvivalTools)
                        return true;
                }
            }
            return false;
        }
    }
}
