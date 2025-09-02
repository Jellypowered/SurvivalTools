// RimWorld 1.6 / C# 7.3
// Source/StaticConstructorClass.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;

namespace SurvivalTools
{
    [StaticConstructorOnStartup]
    public static class StaticConstructorClass
    {
        #region Static ctor

        static StaticConstructorClass()
        {
            try
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    Log.Message("[SurvivalTools] Starting static constructor initialization...");

                // 1) MapGen / content constraints
                ConfigureAncientRuinsTools();

                // 2) Mod compat shims
                if (ModCompatibilityCheck.MendAndRecycle)
                    ResolveMendAndRecycleRecipes();

                ResolveSmeltingRecipeUsers();

                // 3) Data integrity / components
                CheckStuffForStuffPropsTool();
                AddSurvivalToolTrackersToHumanlikes();

                // 4) Auto-detect and enhance tools
                ToolResolver.ResolveAllTools();

                // 5) Warm settings cache (optional/perf)
                SurvivalTools.InitializeSettings();

                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    Log.Message("[SurvivalTools] Static constructor initialization completed successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools] Error during static constructor initialization: {ex}");
                throw;
            }
        }

        #endregion

        #region MapGen constraints

        private static void ConfigureAncientRuinsTools()
        {
            var tsm = ST_ThingSetMakerDefOf.MapGen_AncientRuinsSurvivalTools?.root;
            if (tsm == null) return;

            // fixedParams is a struct; copy-modify-assign
            var p = tsm.fixedParams;
            p.validator = def => def?.IsWithinCategory(ST_ThingCategoryDefOf.SurvivalToolsNeolithic) == true;
            tsm.fixedParams = p;

            if (SurvivalToolUtility.IsDebugLoggingEnabled)
                Log.Message("[SurvivalTools] Configured ancient ruins tools to neolithic category");
        }

        #endregion

        #region Pawn comps

        private static void AddSurvivalToolTrackersToHumanlikes()
        {
            int addedCount = 0;

            foreach (var tDef in DefDatabase<ThingDef>.AllDefs.Where(t => t?.race?.Humanlike == true))
            {
                if (tDef.comps == null)
                    tDef.comps = new List<CompProperties>();

                // Avoid duplicates if another mod or reload path already injected it.
                if (!tDef.comps.Any(c => c?.compClass == typeof(Pawn_SurvivalToolAssignmentTracker)))
                {
                    tDef.comps.Add(new CompProperties(typeof(Pawn_SurvivalToolAssignmentTracker)));
                    addedCount++;
                }
            }

            if (SurvivalToolUtility.IsDebugLoggingEnabled)
                Log.Message($"[SurvivalTools] Added SurvivalToolAssignmentTracker to {addedCount} humanlike defs");
        }

        #endregion

        #region Mod compat

        private static void ResolveMendAndRecycleRecipes()
        {
            // Only recipes that look like ours and are not the vanilla worker
            var relevant = DefDatabase<RecipeDef>.AllDefs.Where(r =>
                r?.defName?.IndexOf("SurvivalTool", StringComparison.OrdinalIgnoreCase) >= 0 &&
                r.workerClass != typeof(RecipeWorker)).ToList();

            int processed = 0, cleared = 0;

            foreach (var recipe in relevant)
            {
                processed++;

                // Require at least one real SurvivalTool to be a valid ingredient
                bool hasValidIngredient = DefDatabase<ThingDef>.AllDefsListForReading
                    .Any(td => td?.thingClass == typeof(SurvivalTool) && recipe.IsIngredient(td));

                if (!hasValidIngredient)
                {
                    recipe.recipeUsers?.Clear();
                    cleared++;
                }
            }

            if (SurvivalToolUtility.IsDebugLoggingEnabled)
                Log.Message($"[SurvivalTools] Processed {processed} Mend&Recycle recipes, cleared {cleared} without valid ingredients");
        }

        private static void ResolveSmeltingRecipeUsers()
        {
            int benchesProcessed = 0, smeltAdded = 0, destroyAdded = 0;

            foreach (var benchDef in DefDatabase<ThingDef>.AllDefs.Where(t => t?.IsWorkTable == true))
            {
                if (benchDef.recipes == null) continue;
                benchesProcessed++;

                // Add our tool recipes when the weapon equivalents are present; avoid duplicates.
                if (benchDef.recipes.Contains(ST_RecipeDefOf.SmeltWeapon) &&
                    !benchDef.recipes.Contains(ST_RecipeDefOf.SmeltSurvivalTool))
                {
                    benchDef.recipes.Add(ST_RecipeDefOf.SmeltSurvivalTool);
                    smeltAdded++;
                }

                if (benchDef.recipes.Contains(ST_RecipeDefOf.DestroyWeapon) &&
                    !benchDef.recipes.Contains(ST_RecipeDefOf.DestroySurvivalTool))
                {
                    benchDef.recipes.Add(ST_RecipeDefOf.DestroySurvivalTool);
                    destroyAdded++;
                }
            }

            if (SurvivalToolUtility.IsDebugLoggingEnabled)
                Log.Message($"[SurvivalTools] Processed {benchesProcessed} work benches: added {smeltAdded} smelt recipes, {destroyAdded} destroy recipes");
        }

        #endregion

        #region Debug scan (StuffPropsTool)

        private static void CheckStuffForStuffPropsTool()
        {
            if (!SurvivalToolUtility.IsDebugLoggingEnabled) return;

            var sb = new StringBuilder();
            sb.AppendLine("[SurvivalTools] Checking all stuff for StuffPropsTool modExtension...");
            sb.AppendLine();

            var hasProps = new StringBuilder("Has props:\n");
            var noProps = new StringBuilder("Doesn't have props:\n");

            var toolCats = GetSurvivalToolStuffCategories();

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
                if (tool.stuffCategories == null || tool.stuffCategories.Count == 0) continue;
                foreach (var cat in tool.stuffCategories)
                    if (cat != null) toolCats.Add(cat);
            }
            return toolCats;
        }

        private static (int hasPropsCount, int noPropsCount) AnalyzeStuffDefs(
            HashSet<StuffCategoryDef> toolCats,
            StringBuilder hasProps,
            StringBuilder noProps)
        {
            int withProps = 0, withoutProps = 0;

            foreach (var stuff in DefDatabase<ThingDef>.AllDefsListForReading.Where(t => t?.IsStuff == true))
            {
                var categories = stuff.stuffProps?.categories;
                if (categories == null || categories.Count == 0) continue;

                // Consider only stuff used by any ST tool
                bool relevant = categories.Any(cat => cat != null && toolCats.Contains(cat));
                if (!relevant) continue;

                string line = $"{stuff.defName} ({stuff.modContentPack?.Name ?? "Core/Unknown"})";

                if (stuff.HasModExtension<StuffPropsTool>())
                {
                    hasProps.AppendLine(line);
                    withProps++;
                }
                else
                {
                    noProps.AppendLine(line);
                    withoutProps++;
                }
            }

            return (withProps, withoutProps);
        }

        #endregion
    }
}
