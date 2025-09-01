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
            try
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                {
                    Log.Message("[SurvivalTools] Starting static constructor initialization...");
                }

                // Limit ancient-ruins tools to neolithic category
                ConfigureAncientRuinsTools();

                // Apply mod compatibility patches
                if (ModCompatibilityCheck.MendAndRecycle)
                {
                    ResolveMendAndRecycleRecipes();
                }

                ResolveSmeltingRecipeUsers();
                CheckStuffForStuffPropsTool();
                AddSurvivalToolTrackersToHumanlikes();

                // Initialize settings cache for optional tools (performance optimization)
                SurvivalTools.InitializeSettings();

                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                {
                    Log.Message("[SurvivalTools] Static constructor initialization completed successfully");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools] Error during static constructor initialization: {ex}");
                throw;
            }
        }

        private static void ConfigureAncientRuinsTools()
        {
            var tsm = ST_ThingSetMakerDefOf.MapGen_AncientRuinsSurvivalTools?.root;
            if (tsm != null)
            {
                var p = tsm.fixedParams; // struct copy
                p.validator = def => def?.IsWithinCategory(ST_ThingCategoryDefOf.SurvivalToolsNeolithic) == true;
                tsm.fixedParams = p;     // assign back

                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                {
                    Log.Message("[SurvivalTools] Configured ancient ruins tools to neolithic category");
                }
            }
        }

        private static void AddSurvivalToolTrackersToHumanlikes()
        {
            int addedCount = 0;
            foreach (var tDef in DefDatabase<ThingDef>.AllDefs.Where(t => t?.race?.Humanlike == true))
            {
                if (tDef.comps == null) tDef.comps = new List<CompProperties>();

                // Avoid duplicate comp entries if patch runs twice for any reason
                if (!tDef.comps.Any(c => c?.compClass == typeof(Pawn_SurvivalToolAssignmentTracker)))
                {
                    tDef.comps.Add(new CompProperties(typeof(Pawn_SurvivalToolAssignmentTracker)));
                    addedCount++;
                }
            }

            if (SurvivalToolUtility.IsDebugLoggingEnabled)
            {
                Log.Message($"[SurvivalTools] Added SurvivalToolAssignmentTracker to {addedCount} humanlike defs");
            }
        }

        private static void ResolveMendAndRecycleRecipes()
        {
            var relevantRecipes = DefDatabase<RecipeDef>.AllDefs.Where(r =>
                r?.defName?.IndexOf("SurvivalTool", StringComparison.OrdinalIgnoreCase) >= 0
                && r.workerClass != typeof(RecipeWorker)).ToList();

            int processedCount = 0;
            int clearedCount = 0;

            foreach (var recipe in relevantRecipes)
            {
                processedCount++;
                bool hasValidIngredient = DefDatabase<ThingDef>.AllDefsListForReading
                    .Any(thing => thing?.thingClass == typeof(SurvivalTool) && recipe.IsIngredient(thing));

                if (!hasValidIngredient)
                {
                    recipe.recipeUsers?.Clear();
                    clearedCount++;
                }
            }

            if (SurvivalToolUtility.IsDebugLoggingEnabled)
            {
                Log.Message($"[SurvivalTools] Processed {processedCount} Mend&Recycle recipes, cleared {clearedCount} without valid ingredients");
            }
        }

        private static void ResolveSmeltingRecipeUsers()
        {
            int benchesProcessed = 0;
            int smeltRecipesAdded = 0;
            int destroyRecipesAdded = 0;

            foreach (var benchDef in DefDatabase<ThingDef>.AllDefs.Where(t => t?.IsWorkTable == true))
            {
                if (benchDef.recipes == null) continue;
                benchesProcessed++;

                // Add our tool recipes when the weapon equivalents are present, avoid duplicates
                if (benchDef.recipes.Contains(ST_RecipeDefOf.SmeltWeapon) &&
                    !benchDef.recipes.Contains(ST_RecipeDefOf.SmeltSurvivalTool))
                {
                    benchDef.recipes.Add(ST_RecipeDefOf.SmeltSurvivalTool);
                    smeltRecipesAdded++;
                }

                if (benchDef.recipes.Contains(ST_RecipeDefOf.DestroyWeapon) &&
                    !benchDef.recipes.Contains(ST_RecipeDefOf.DestroySurvivalTool))
                {
                    benchDef.recipes.Add(ST_RecipeDefOf.DestroySurvivalTool);
                    destroyRecipesAdded++;
                }
            }

            if (SurvivalToolUtility.IsDebugLoggingEnabled)
            {
                Log.Message($"[SurvivalTools] Processed {benchesProcessed} work benches: added {smeltRecipesAdded} smelt recipes, {destroyRecipesAdded} destroy recipes");
            }
        }

        private static void CheckStuffForStuffPropsTool()
        {
            if (!SurvivalToolUtility.IsDebugLoggingEnabled) return;

            var sb = new StringBuilder();
            sb.AppendLine("[SurvivalTools] Checking all stuff for StuffPropsTool modExtension...");
            sb.AppendLine();

            var hasProps = new StringBuilder("Has props:\n");
            var noProps = new StringBuilder("Doesn't have props:\n");

            // Collect all stuff categories used by survival tools
            var toolCats = GetSurvivalToolStuffCategories();

            // Scan all stuff defs that fall into any tool category
            var (hasPropsCount, noPropsCount) = AnalyzeStuffDefs(toolCats, hasProps, noProps);

            sb.AppendLine($"Summary: {hasPropsCount} stuff defs have StuffPropsTool, {noPropsCount} do not");
            sb.AppendLine();
            sb.Append(hasProps);
            sb.AppendLine();
            sb.Append(noProps);

            Log.Message(sb.ToString());
        }

        private static HashSet<StuffCategoryDef> GetSurvivalToolStuffCategories()
        {
            var toolCats = new HashSet<StuffCategoryDef>();
            foreach (var tool in DefDatabase<ThingDef>.AllDefsListForReading.Where(t => t?.IsSurvivalTool() == true))
            {
                if (tool.stuffCategories?.Count > 0)
                {
                    foreach (var cat in tool.stuffCategories)
                    {
                        if (cat != null) toolCats.Add(cat);
                    }
                }
            }
            return toolCats;
        }

        private static (int hasPropsCount, int noPropsCount) AnalyzeStuffDefs(
            HashSet<StuffCategoryDef> toolCats,
            StringBuilder hasProps,
            StringBuilder noProps)
        {
            int hasPropsCount = 0, noPropsCount = 0;

            foreach (var stuff in DefDatabase<ThingDef>.AllDefsListForReading.Where(t => t?.IsStuff == true))
            {
                var categories = stuff.stuffProps?.categories;
                if (categories?.Count == 0) continue;

                // Check if this stuff is relevant to any tool categories
                bool isRelevant = categories.Any(cat => cat != null && toolCats.Contains(cat));
                if (!isRelevant) continue;

                string line = $"{stuff.defName} ({stuff.modContentPack?.Name ?? "Core/Unknown"})";

                if (stuff.HasModExtension<StuffPropsTool>())
                {
                    hasProps.AppendLine(line);
                    hasPropsCount++;
                }
                else
                {
                    noProps.AppendLine(line);
                    noPropsCount++;
                }
            }

            return (hasPropsCount, noPropsCount);
        }
    }
}
