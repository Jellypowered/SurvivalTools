using Verse;
using RimWorld;

namespace SurvivalTools
{
    public class SpecialThingFilterWorker_NonSmeltableTools : SpecialThingFilterWorker
    {
        public override bool Matches(Thing t)
        {
            if (t == null) return false;
            var def = t.def;
            return CanEverMatch(def) && !t.Smeltable;
        }

        public override bool CanEverMatch(ThingDef def)
        {
            if (def == null) return false;
            if (!def.IsSurvivalTool()) return false;

            // True if this def is in SurvivalTools or any of its subcategories
            if (def.thingCategories.NullOrEmpty()) return false;
            for (int i = 0; i < def.thingCategories.Count; i++)
            {
                for (ThingCategoryDef cat = def.thingCategories[i]; cat != null; cat = cat.parent)
                {
                    if (cat == ST_ThingCategoryDefOf.SurvivalTools)
                        return true;
                }
            }
            return false;
        }

        public override bool AlwaysMatches(ThingDef def)
        {
            if (!CanEverMatch(def)) return false;
            return !def.smeltable && !def.MadeFromStuff;
        }
    }
}
