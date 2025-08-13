using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;

namespace SurvivalTools
{
    [StaticConstructorOnStartup]
    public static class StaticConstructorClass
    {
        static StaticConstructorClass()
        {
            // Limit ancient-ruins tools to neolithic category
            var tsm = ST_ThingSetMakerDefOf.MapGen_AncientRuinsSurvivalTools?.root;
            if (tsm != null)
            {
                var p = tsm.fixedParams; // struct copy
                p.validator = def => def != null && def.IsWithinCategory(ST_ThingCategoryDefOf.SurvivalToolsNeolithic);
                tsm.fixedParams = p;     // assign back
            }

            if (ModCompatibilityCheck.MendAndRecycle) ResolveMendAndRecycleRecipes();
            ResolveSmeltingRecipeUsers();
            CheckStuffForStuffPropsTool();

            // Add SurvivalToolAssignmentTracker to all humanlikes
            foreach (var tDef in DefDatabase<ThingDef>.AllDefs.Where(t => t?.race?.Humanlike == true))
            {
                if (tDef.comps == null) tDef.comps = new List<CompProperties>();
                // Avoid duplicate comp entries if patch runs twice for any reason
                if (!tDef.comps.Any(c => c?.compClass == typeof(Pawn_SurvivalToolAssignmentTracker)))
                {
                    tDef.comps.Add(new CompProperties(typeof(Pawn_SurvivalToolAssignmentTracker)));
                }
            }
        }

        private static void ResolveMendAndRecycleRecipes()
        {
            bool anyMatch;
            foreach (var recipe in DefDatabase<RecipeDef>.AllDefs.Where(r =>
                         r != null && r.defName != null && r.defName.IndexOf("SurvivalTool", StringComparison.OrdinalIgnoreCase) >= 0
                         && r.workerClass != typeof(RecipeWorker)))
            {
                anyMatch = false;

                // If any SurvivalTool is a valid ingredient, keep it; otherwise strip recipeUsers
                foreach (var thing in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (thing?.thingClass == typeof(SurvivalTool) && recipe.IsIngredient(thing))
                    {
                        anyMatch = true;
                        break;
                    }
                }

                if (!anyMatch)
                {
                    recipe.recipeUsers?.Clear();
                }
            }
        }

        private static void ResolveSmeltingRecipeUsers()
        {
            foreach (var benchDef in DefDatabase<ThingDef>.AllDefs.Where(t => t?.IsWorkTable == true))
            {
                var list = benchDef.recipes;
                if (list == null) continue;

                // Add our tool recipes when the weapon equivalents are present, avoid duplicates.
                if (list.Contains(ST_RecipeDefOf.SmeltWeapon) && !list.Contains(ST_RecipeDefOf.SmeltSurvivalTool))
                    list.Add(ST_RecipeDefOf.SmeltSurvivalTool);

                if (list.Contains(ST_RecipeDefOf.DestroyWeapon) && !list.Contains(ST_RecipeDefOf.DestroySurvivalTool))
                    list.Add(ST_RecipeDefOf.DestroySurvivalTool);
            }
        }

        private static void CheckStuffForStuffPropsTool()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Checking all stuff for StuffPropsTool modExtension...");
            sb.AppendLine();

            var hasProps = new StringBuilder("Has props:\n");
            var noProps = new StringBuilder("Doesn't have props:\n");

            // Collect all stuff categories used by survival tools
            var toolCats = new HashSet<StuffCategoryDef>();
            foreach (var tool in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (tool == null || !tool.IsSurvivalTool()) continue;
                if (tool.stuffCategories == null) continue;
                foreach (var cat in tool.stuffCategories)
                {
                    if (cat != null) toolCats.Add(cat);
                }
            }

            // Scan all stuff defs that fall into any tool category
            foreach (var stuff in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (stuff == null || !stuff.IsStuff) continue;

                var sp = stuff.stuffProps;
                var cats = sp?.categories;
                if (cats == null) continue;

                bool relevant = false;
                for (int i = 0; i < cats.Count; i++)
                {
                    var c = cats[i];
                    if (c != null && toolCats.Contains(c))
                    {
                        relevant = true;
                        break;
                    }
                }
                if (!relevant) continue;

                string line = $"{stuff} ({stuff.modContentPack?.Name ?? "Core/Unknown"})";
                if (stuff.HasModExtension<StuffPropsTool>())
                    hasProps.AppendLine(line);
                else
                    noProps.AppendLine(line);
            }

            sb.Append(hasProps);
            sb.AppendLine();
            sb.Append(noProps);
            Log.Message(sb.ToString());
        }
    }
}
