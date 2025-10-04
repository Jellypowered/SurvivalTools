// RimWorld 1.6 / C# 7.3
// Source/StaticConstructorClass.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;
using HarmonyLib;
using SurvivalTools.Compat;
using static SurvivalTools.ToolResolver;
using static SurvivalTools.ST_Logging;

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
                if (IsDebugLoggingEnabled)
                    LogInfo("[SurvivalTools] Starting static constructor initialization...");

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
                ResolveAllTools();

                // 5) Warm settings cache (optional/perf)
                SurvivalToolsMod.InitializeSettings();

                // 6) Apply conditional feature registration
                ConditionalRegistration.ApplyTreeFellingConditionals();

                // 7) Initialize gating system with WorkGiver → StatDef mappings
                InitializeJobGatingMappings();

                // 8) Schedule delayed job validation (after game fully loads)
                LongEventHandler.QueueLongEvent(ValidateExistingJobsOnModLoad, "SurvivalTools: Validating existing jobs...", false, null);

                // 9) Schedule optional stat gating validator (ensure demoted optional stats aren't reintroduced as hard requirements by other patches)
                LongEventHandler.QueueLongEvent(ValidateDemotedOptionalStatsNotHardGated, "SurvivalTools: Validating demoted optional stats...", false, null);

                // 10) Pre-warm performance-critical caches to eliminate first-click lag
                LongEventHandler.QueueLongEvent(PreWarmCaches, "SurvivalTools: Pre-warming caches...", false, null);

                if (IsDebugLoggingEnabled)
                    LogInfo("[SurvivalTools] Static constructor initialization completed successfully");
            }
            catch (Exception ex)
            {
                LogError($"[SurvivalTools] Error during static constructor initialization: {ex}");
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

            if (IsDebugLoggingEnabled)
                LogInfo("[SurvivalTools] Configured ancient ruins tools to neolithic category");
        }

        #endregion

        #region Pawn comps

        private static void AddSurvivalToolTrackersToHumanlikes()
        {
            int legacyCount = 0;
            int forcedCount = 0;

            foreach (var tDef in DefDatabase<ThingDef>.AllDefs.Where(t => t?.race?.Humanlike == true))
            {
                if (tDef.comps == null)
                    tDef.comps = new List<CompProperties>();

                // Phase 11.11: Add legacy assignment tracker (for save migration)
#pragma warning disable CS0618 // Type is obsolete - intentionally adding for save compatibility/migration
                if (!tDef.comps.Any(c => c?.compClass == typeof(Pawn_SurvivalToolAssignmentTracker)))
                {
                    tDef.comps.Add(new CompProperties(typeof(Pawn_SurvivalToolAssignmentTracker)));
                    legacyCount++;
                }
#pragma warning restore CS0618

                // Phase 11.11: Add new forced tool tracker (replaces forcedHandler functionality)
                if (!tDef.comps.Any(c => c?.compClass == typeof(Pawn_ForcedToolTracker)))
                {
                    tDef.comps.Add(new CompProperties(typeof(Pawn_ForcedToolTracker)));
                    forcedCount++;
                }
            }

            if (IsDebugLoggingEnabled)
                LogInfo($"[SurvivalTools] Added SurvivalToolAssignmentTracker (legacy) to {legacyCount} humanlike defs, ForcedToolTracker to {forcedCount}");
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

            if (IsDebugLoggingEnabled)
                LogInfo($"[SurvivalTools] Processed {processed} Mend&Recycle recipes, cleared {cleared} without valid ingredients");
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

            if (IsDebugLoggingEnabled)
                LogInfo($"[SurvivalTools] Processed {benchesProcessed} work benches: added {smeltAdded} smelt recipes, {destroyAdded} destroy recipes");
        }

        #endregion

        #region Debug scan (StuffPropsTool)

        private static void CheckStuffForStuffPropsTool()
        {
            if (!IsDebugLoggingEnabled) return;

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

            if (IsDebugLoggingEnabled)
                LogInfo(sb.ToString());
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

        #region Job gating initialization

        private static void InitializeJobGatingMappings()
        {
            // Register core vanilla WorkGivers with their required stats
            // This populates the registry for JobGate.ShouldBlock() lookups

            // Mining/digging work
            RegisterWorkGiverForStat("DeepDrill", ST_StatDefOf.DiggingSpeed);
            RegisterWorkGiverForStat("GroundPenetratingScannerScan", ST_StatDefOf.DiggingSpeed);
            RegisterWorkGiverForStat("Mine", ST_StatDefOf.DiggingSpeed);
            RegisterWorkGiverForStat("MineQuarry", ST_StatDefOf.DiggingSpeed); // mod compat

            // Tree cutting
            RegisterWorkGiverForStat("Grower_ChopWood", ST_StatDefOf.TreeFellingSpeed);
            RegisterWorkGiverForStat("FellTrees", ST_StatDefOf.TreeFellingSpeed);

            // Plant work
            RegisterWorkGiverForStat("Grower_Harvest", ST_StatDefOf.PlantHarvestingSpeed);
            RegisterWorkGiverForStat("PlantsCut", ST_StatDefOf.PlantHarvestingSpeed);
            RegisterWorkGiverForStat("Grower_Sow", ST_StatDefOf.SowingSpeed);

            // Construction
            RegisterWorkGiverForStat("ConstructFinishFrames", StatDefOf.ConstructionSpeed);
            RegisterWorkGiverForStat("ConstructSmoothWall", StatDefOf.ConstructionSpeed);
            RegisterWorkGiverForStat("ConstructSmoothFloor", StatDefOf.ConstructionSpeed);

            // Repair/maintenance
            RegisterWorkGiverForStat("Repair", ST_StatDefOf.MaintenanceSpeed);

            // Deconstruction
            RegisterWorkGiverForStat("Deconstruct", ST_StatDefOf.DeconstructionSpeed);

            // Research (bench) – ensures vanilla research jobs are gated by appropriate research tools
            RegisterWorkGiverForStat("Research", ST_StatDefOf.ResearchSpeed);

            // NOTE: Cleaning is NOT registered here. It's an optional stat only gated in Extra Hardcore mode
            // via the requireCleaningTools setting. The XML patches handle cleaning WorkGiver extensions.

            // Dynamic discovery pass: pick up any additional (modded) sowing WorkGivers not explicitly known
            DiscoverAndRegisterAdditionalSowingWorkGivers();

            if (IsDebugLoggingEnabled)
                LogInfo("[SurvivalTools] Initialized job gating mappings for core WorkGivers");
        }

        private static void RegisterWorkGiverForStat(string workGiverDefName, StatDef stat)
        {
            var workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (workGiver != null && stat != null)
            {
                Compat.CompatAPI.RegisterWorkGiverRequirement(workGiver, stat);
            }
            else if (IsDebugLoggingEnabled)
            {
                // Dev-mode only noisy warning; in normal mode stay silent to avoid log spam if defs missing by design.
                if (Prefs.DevMode && ST_Logging.ShouldLogWithCooldown($"RegisterWG_{workGiverDefName}"))
                    LogWarning($"[SurvivalTools] (Dev) Failed to register {workGiverDefName} for stat {stat?.defName ?? "null"} - workGiver: {workGiver != null}, stat: {stat != null}");
            }
        }

        /// <summary>
        /// Auto-detect additional sowing WorkGivers added by other mods and register them for SowingSpeed.
        /// Heuristics: defName or giverClass name contains "Sow" and it's NOT the vanilla Grower_Sow (already registered),
        /// the worker derives (directly or indirectly) from RimWorld.WorkGiver_GrowerSow if that type relationship is available.
        /// Skips any WorkGiver already requiring SowingSpeed to avoid duplicates.
        /// </summary>
        private static void DiscoverAndRegisterAdditionalSowingWorkGivers()
        {
            if (ST_StatDefOf.SowingSpeed == null) return; // Stat missing – nothing to do

            int inspected = 0, added = 0;
            foreach (var wg in DefDatabase<WorkGiverDef>.AllDefsListForReading)
            {
                if (wg == null) continue;
                inspected++;

                var defName = wg.defName;
                if (string.IsNullOrEmpty(defName)) continue;
                if (defName == "Grower_Sow") continue; // vanilla already explicitly registered

                // Fast textual heuristic first
                bool nameSuggestsSow = defName.IndexOf("Sow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       (wg.giverClass?.Name?.IndexOf("Sow", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!nameSuggestsSow) continue;

                // Stronger type safety: require giverClass assignable to WorkGiver_GrowerSow if possible
                try
                {
                    if (wg.giverClass == null) continue;
                    if (!typeof(WorkGiver_GrowerSow).IsAssignableFrom(wg.giverClass))
                        continue; // Not an actual sowing worker
                }
                catch
                {
                    continue; // Defensive – if reflection blows up, skip
                }

                // Already has SowingSpeed registered?
                var existing = Compat.CompatAPI.GetRequiredStatsFor(wg);
                if (existing != null && existing.Contains(ST_StatDefOf.SowingSpeed))
                    continue;

                Compat.CompatAPI.RegisterWorkGiverRequirement(wg, ST_StatDefOf.SowingSpeed);
                added++;
            }

            if (added > 0 && IsDebugLoggingEnabled)
                LogInfo($"[SurvivalTools] Dynamic sowing WG discovery: inspected {inspected}, added SowingSpeed requirement to {added} additional WorkGivers.");
        }



        #endregion

        #region Job validation on mod load

        private static void ValidateExistingJobsOnModLoad()
        {
            // Only validate if we're in hardcore mode and the game is actually running
            if (Current.ProgramState != ProgramState.Playing) return;

            Helpers.SurvivalToolValidation.ValidateExistingJobs("mod loaded with existing save");
        }

        /// <summary>
        /// Runtime safeguard: scan all WorkGiver requirements after full def load and warn if any demoted optional stats
        /// (MiningYieldDigging, ButcheryFleshEfficiency, MedicalSurgerySuccessChance) appear as required gating stats.
        /// Intention: avoid external XML patches silently re‑hardening bonus stats and confusing players (blocked work with only efficiency bonus missing).
        /// Does not mutate data – purely diagnostic.
        /// </summary>
        private static void ValidateDemotedOptionalStatsNotHardGated()
        {
            try
            {
                if (Current.ProgramState != ProgramState.Playing) return; // Only meaningful when a map/game is running

                var optionalStats = new List<StatDef>
                {
                    ST_StatDefOf.MiningYieldDigging,
                    ST_StatDefOf.ButcheryFleshEfficiency,
                    ST_StatDefOf.MedicalSurgerySuccessChance
                }.Where(s => s != null).ToList();
                if (optionalStats.Count == 0) return;

                // Collect any WorkGivers that require these directly via our registry OR via extensions (defName pattern fallback if registry not yet populated for them)
                var flagged = new List<string>();
                foreach (var wg in DefDatabase<WorkGiverDef>.AllDefsListForReading)
                {
                    if (wg == null) continue;

                    // 1) Extension check
                    var extStats = wg.GetModExtension<WorkGiverExtension>()?.requiredStats;
                    if (extStats != null && extStats.Any(s => optionalStats.Contains(s)))
                    {
                        flagged.Add($"{wg.defName} (extension)");
                        continue;
                    }

                    // 2) Registry (CompatAPI) mapping
                    var req = Compat.CompatAPI.GetRequiredStatsFor(wg);
                    if (req != null && req.Any(s => optionalStats.Contains(s)))
                    {
                        flagged.Add($"{wg.defName} (registry)");
                        continue;
                    }
                }

                if (flagged.Count > 0)
                {
                    if (IsDebugLoggingEnabled)
                    {
                        LogWarning("[SurvivalTools] (Debug) Detected demoted optional stats being used as REQUIRED gating stats. These should usually be treated as bonuses only.\n" +
                                    " WorkGivers: " + string.Join(", ", flagged) + "\n" +
                                    " Optional stats involved: " + string.Join(", ", optionalStats.Select(s => s.defName)) + "\n" +
                                    " If this is intentional (another mod design), you can ignore this message. Otherwise consider removing those stats from required lists.");
                    }
                }
                else if (IsDebugLoggingEnabled)
                {
                    LogInfo("[SurvivalTools] (Debug) Optional stat validator: no hard-gating misuse detected.");
                }
            }
            catch (Exception ex)
            {
                LogWarning("[SurvivalTools] Optional stat validator encountered an error: " + ex);
            }
        }

        #endregion

        #region Cache pre-warming

        /// <summary>
        /// Pre-warm performance-critical caches during game load to eliminate first-click lag.
        /// Builds caches while loading screen is visible so first right-click is instant.
        /// </summary>
        private static void PreWarmCaches()
        {
            try
            {
                if (IsDebugLoggingEnabled)
                    LogInfo("[SurvivalTools] Starting cache pre-warming...");

                var sw = System.Diagnostics.Stopwatch.StartNew();

                // 1) Warm ToolStatResolver (RegisteredWorkStats HashSet)
                SurvivalTools.Helpers.ToolStatResolver.Initialize();

                // 2) Warm AssignmentSearch.GetRelevantToolDefs cache
                // This is the MAJOR bottleneck (~50-150ms on first call)
                SurvivalTools.Assign.AssignmentSearch.WarmRelevantToolDefsCache();

                sw.Stop();

                if (IsDebugLoggingEnabled)
                    LogInfo($"[SurvivalTools] Cache pre-warming completed in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                // Don't fail startup on cache warming errors
                LogWarning($"[SurvivalTools] Cache pre-warming encountered an error: {ex}");
            }
        }

        #endregion
    }
}
