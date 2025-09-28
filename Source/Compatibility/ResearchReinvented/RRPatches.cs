// RimWorld 1.6 / C# 7.3
// Source/Compatibility/ResearchReinvented/RRPatches.cs
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Compat.ResearchReinvented
{
    // Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs
    [StaticConstructorOnStartup]
    internal static class RRPatches
    {
        private const string HARMONY_ID = "SurvivalTools.Compat.ResearchReinventedGating";
        private static readonly Harmony harmony;

        static RRPatches()
        {
            harmony = new Harmony(HARMONY_ID);

            try
            {
                if (!CompatAPI.IsResearchReinventedActive)
                {
                    if (IsCompatLogging()) LogCompat("RR gating: Research Reinvented not detected â€” skipping patches.");
                    return;
                }

                if (IsCompatLogging()) LogCompat("RR gating: Research Reinvented detected â€” applying patches.");

                // Patch RR pawn extension methods (postfixes are applied by RRHelpers.Initialize which calls ApplyHarmonyHooks)
                RRHelpers.Initialize();

                // Patch base WorkGiver_Scanner methods to gate jobs early when hardcore enforcement is active
                var baseType = typeof(WorkGiver_Scanner);

                var miThing = AccessTools.Method(baseType, "HasJobOnThing");
                if (miThing != null)
                {
                    harmony.Patch(miThing,
                        prefix: new HarmonyMethod(typeof(RRPatches), nameof(Prefix_WorkGiverScanner_HasJobOnThing)) { priority = Priority.First });
                }

                var miCell = AccessTools.Method(baseType, "HasJobOnCell");
                if (miCell != null)
                {
                    harmony.Patch(miCell,
                        prefix: new HarmonyMethod(typeof(RRPatches), nameof(Prefix_WorkGiverScanner_HasJobOnCell)) { priority = Priority.First });
                }

                // Patch recipe toils so research/crafting progress respects tool tiers (defensive)
                var toilsType = AccessTools.TypeByName("Toils_Recipe");
                if (toilsType != null)
                {
                    var doWork = AccessTools.Method(toilsType, "DoRecipeWork");
                    if (doWork != null)
                    {
                        try
                        {
                            harmony.Patch(doWork, prefix: new HarmonyMethod(typeof(RRPatches), nameof(Prefix_Toils_Recipe_DoRecipeWork)));
                        }
                        catch (Exception e)
                        {
                            LogCompatWarning($"RR gating: failed to patch Toils_Recipe.DoRecipeWork: {e}");
                        }
                    }

                    var makeUnfinished = AccessTools.Method(toilsType, "MakeUnfinishedThingIfNeeded");
                    if (makeUnfinished != null)
                    {
                        try
                        {
                            harmony.Patch(makeUnfinished, prefix: new HarmonyMethod(typeof(RRPatches), nameof(Prefix_Toils_Recipe_MakeUnfinishedThingIfNeeded)));
                        }
                        catch (Exception e)
                        {
                            LogCompatWarning($"RR gating: failed to patch Toils_Recipe.MakeUnfinishedThingIfNeeded: {e}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"[SurvivalTools Compat] RR gating patch init failed: {e}");
            }
        }

        private static bool Prefix_WorkGiverScanner_HasJobOnThing(WorkGiver_Scanner __instance, Pawn pawn, Thing t, ref bool __result)
        {
            return CheckWorkGiverGate(__instance?.def, pawn, ref __result, "HasJobOnThing");
        }

        private static bool Prefix_WorkGiverScanner_HasJobOnCell(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, ref bool __result)
        {
            return CheckWorkGiverGate(__instance?.def, pawn, ref __result, "HasJobOnCell");
        }

        private static bool CheckWorkGiverGate(WorkGiverDef wgDef, Pawn pawn, ref bool __result, string context)
        {
            try
            {
                if (wgDef == null || pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                    return true;

                var s = SurvivalToolsMod.Settings;
                bool enforce = SurvivalToolUtility.IsHardcoreModeEnabled || (s != null && s.extraHardcoreMode);
                if (!enforce) return true;

                if (!RRHelpers.RequiresResearchTools(wgDef)) return true;

                if (!CompatAPI.PawnHasResearchTools(pawn))
                {
                    __result = false;
                    if (IsCompatLogging()) LogCompat($"Blocking {wgDef.defName} {context} for {pawn.LabelShort} - no research tool.");
                    return false;
                }
            }
            catch (Exception e)
            {
                LogCompatWarning($"CheckWorkGiverGate threw in {context}: {e.Message}");
            }

            return true;
        }

        // Defensive prefix for Toils_Recipe.DoRecipeWork – adjust or block progress when research tools are required
        private static bool Prefix_Toils_Recipe_DoRecipeWork(object __instance, JobDriver __state)
        {
            try
            {
                if (!RRHelpers.IsRRActive) return true;

                // Attempt to locate the pawn performing the toil
                Pawn pawn = null;
                try { pawn = (Pawn)AccessTools.Field(__instance.GetType(), "actor").GetValue(__instance); } catch { }
                if (pawn == null) return true;

                // Resolve the job/workgiver context
                Job job = pawn.CurJob;
                var wgd = RRHelpers.ResolveWorkGiverForJob(job);

                // Required stats for RR-sensitive workgivers
                var required = RRHelpers.GetRequiredStatsForWorkGiverCached(wgd, job);
                if (required == null || required.Count == 0) return true;

                // If in extra-hardcore RR mode and pawn lacks research tools, block
                if (SurvivalToolsMod.Settings?.extraHardcoreMode == true && RRHelpers.Settings.IsRRCompatibilityEnabled)
                {
                    foreach (var st in required)
                    {
                        if (RRHelpers.Settings.IsRRStatRequiredInExtraHardcore(st) && !CompatAPI.PawnHasResearchTools(pawn))
                        {
                            if (IsCompatLogging()) LogCompat($"Blocking recipe/research toil for {pawn.LabelShort}: missing RR research tool for stat {st.defName}.");
                            return false; // skip original toil
                        }
                    }
                }

                // Otherwise allow original and let StatPart_SurvivalTool / WorkSpeedGlobal adjust speed
                return true;
            }
            catch (Exception e)
            {
                LogCompatWarning($"Prefix_Toils_Recipe_DoRecipeWork exception: {e}");
                return true;
            }
        }

        // Defensive prefix for Toils_Recipe.MakeUnfinishedThingIfNeeded – block creation of unfinished items if research tools are missing under extra-hardcore
        private static bool Prefix_Toils_Recipe_MakeUnfinishedThingIfNeeded(object __instance)
        {
            try
            {
                if (!RRHelpers.IsRRActive) return true;

                Pawn pawn = null;
                try { pawn = (Pawn)AccessTools.Field(__instance.GetType(), "actor").GetValue(__instance); } catch { }
                if (pawn == null) return true;

                Job job = pawn.CurJob;
                var wgd = RRHelpers.ResolveWorkGiverForJob(job);
                var required = RRHelpers.GetRequiredStatsForWorkGiverCached(wgd, job);
                if (required == null || required.Count == 0) return true;

                if (SurvivalToolsMod.Settings?.extraHardcoreMode == true && RRHelpers.Settings.IsRRCompatibilityEnabled)
                {
                    foreach (var st in required)
                    {
                        if (RRHelpers.Settings.IsRRStatRequiredInExtraHardcore(st) && !CompatAPI.PawnHasResearchTools(pawn))
                        {
                            if (IsCompatLogging()) LogCompat($"Blocking MakeUnfinished creation for {pawn.LabelShort}: missing RR research tool for stat {st.defName}.");
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                LogCompatWarning($"Prefix_Toils_Recipe_MakeUnfinishedThingIfNeeded exception: {e}");
                return true;
            }
        }
    }
}
