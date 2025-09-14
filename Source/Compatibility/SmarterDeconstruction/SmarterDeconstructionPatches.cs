// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SmarterDeconstruction/SmarterDeconstructionPatches.cs
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

// Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs
using Verse.AI;
using static SurvivalTools.ST_Logging;
using SurvivalTools.Compat;
using SurvivalTools.Helpers;

namespace SurvivalTools.Compat.SmarterDeconstruction
{
    /// <summary>
    /// Patches to integrate with SmarterDeconstruction and related mods.
    /// Handles pacifist equip logic, roof handling, mining damage tuning, and gating.
    /// </summary>
    internal sealed class SmarterDeconstructionCompatibilityModule : ICompatibilityModule
    {
        // Indicates whether SmarterDeconstruction compat applied the WorkGiver_Scanner hooks.
        public static bool OwnerAppliedWorkGiverScannerHooks = false;

        private Harmony _harmony;
        public string ModName => "SmarterDeconstruction";
        public bool IsModActive => SmarterDeconstructionHelpers.IsSmarterDeconstructionActive();

        public void Initialize()
        {
            if (!IsModActive) return;
            try
            {
                _harmony = new Harmony("com.jellypowered.survivaltools.compat.smarterdeconstruction");

                // Pacifist equip logic is provided by core SurvivalTools (Patch_EquipmentUtility_CanEquip_PacifistTools).
                // Do not apply a duplicate CanEquip postfix here; rely on core behaviour instead.

                // Patch WorkGiver_PlantsCut.JobOnThing to gate survival tool checks
                var wgType = AccessTools.TypeByName("SmarterDeconstruction.WorkGiver_PlantsCut");
                if (wgType != null)
                {
                    var m = AccessTools.Method(wgType, "JobOnThing");
                    if (m != null)
                    {
                        try { _harmony.Patch(m, prefix: new HarmonyMethod(typeof(SmarterDeconstructionCompatibilityModule).GetMethod(nameof(Prefix_JobOnThing)))); }
                        catch (Exception e) { LogCompatWarning("SmarterDeconstruction: Failed to patch WorkGiver_PlantsCut.JobOnThing: " + e); }
                        OwnerAppliedWorkGiverScannerHooks = true;
                    }
                }

                // SmarterDeconstruction relies on the core WorkGiver_Scanner gating patch (Patch_WorkGiver_Scanner_ToolGate.cs)
                // to avoid duplicating scanner-level hooks. We keep per-WG patches (e.g., WorkGiver_PlantsCut) above.
            }
            catch (Exception e)
            {
                LogCompatError("SmarterDeconstruction.Initialize failed: " + e);
            }
        }

        public List<StatDef> GetCompatibilityStats() => new List<StatDef>();

        public Dictionary<string, string> GetDebugInfo() => new Dictionary<string, string> { ["Active"] = IsModActive.ToString() };

        // Pacifist equip logic is handled by core SurvivalTools; no postfix required here.

        public static bool Prefix_JobOnThing(object __instance)
        {
            try
            {
                if (!SmarterDeconstructionHelpers.IsSmarterDeconstructionActive()) return true;
                return true;
            }
            catch (Exception e)
            {
                LogCompatWarning("Prefix_JobOnThing exception: " + e);
                return true;
            }
        }

        /// <summary>
        /// Canonical prefix for WorkGiver_Scanner.JobOnThing.
        /// Gates jobs that SurvivalTools wants to block when the pawn lacks appropriate survival tools.
        /// Returns true to continue original when allowed, false to skip job creation.
        /// </summary>
        public static bool Prefix_WorkGiverScanner_JobOnThing(object __instance, Pawn pawn, Thing t)
        {
            try
            {
                if (!SmarterDeconstructionHelpers.IsSmarterDeconstructionActive()) return true;
                if (pawn == null || t == null) return true;

                // Determine the WorkGiverDef from the instance if possible
                WorkGiverDef wgDef = null;
                try { wgDef = (WorkGiverDef)AccessTools.Field(__instance.GetType(), "def").GetValue(__instance); } catch { }

                // If this job should be gated by SurvivalTools, enforce pawn must have a survival tool for the job's main stat
                if (wgDef != null)
                {
                    JobDef jobDef = null;
                    try { jobDef = (JobDef)AccessTools.Field(__instance.GetType(), "def").GetValue(__instance); } catch { }

                    // Use SurvivalToolUtility heuristics: ShouldGateByDefault(WorkGiverDef) and PawnHasSurvivalTool
                    try
                    {
                        if (SurvivalToolUtility.ShouldGateByDefault(wgDef))
                        {
                            // Determine required stats for this workgiver/job
                            var requiredStats = SurvivalToolUtility.RelevantStatsFor(wgDef, jobDef ?? (JobDef)null) ?? new List<StatDef>();

                            // If research stats are relevant, honor Research tools via CompatAPI
                            if ((requiredStats == null || requiredStats.Count == 0) && CompatAPI.PawnHasResearchTools(pawn))
                            {
                                // Pawn already satisfied by research tools
                            }
                            else
                            {
                                // Always call central blacklist / capability check first
                                if (!PawnToolValidator.CanUseSurvivalTools(pawn))
                                {
                                    // Pawn is blacklisted by core rules (mechanoid/quest/other) — allow original behaviour
                                    return true;
                                }

                                // Use the unified gating helper which respects hardcore mode and settings
                                if (!pawn.MeetsWorkGiverStatRequirements(requiredStats, wgDef, jobDef))
                                {
                                    if (ST_Logging.ShouldLogJobForPawn(pawn, jobDef ?? JobDefOf.Refuel))
                                    {
                                        // Avoid LINQ in this hot-path logging: build a short comma-separated list
                                        string statsText = "";
                                        for (int i = 0; i < requiredStats.Count; i++)
                                        {
                                            var s = requiredStats[i];
                                            statsText += (s != null ? s.defName : "null");
                                            if (i < requiredStats.Count - 1) statsText += ", ";
                                        }
                                        LogCompatMessage($"Blocking job from {wgDef.defName} for pawn {pawn.LabelShort} — lacks tool for required stats: {statsText}", "SmarterDeconstruction.WorkGiverGate");
                                    }
                                    return false;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogCompatWarning("Prefix_WorkGiverScanner_JobOnThing survival gate check failed: " + ex);
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                LogCompatWarning("Prefix_WorkGiverScanner_JobOnThing exception: " + e);
                return true;
            }
        }

        // Hook into Pawn_JobTracker.TryTakeOrderedJob to gate jobs requiring survival tools
        public static bool Prefix_TryTakeOrderedJob(Pawn_JobTracker __instance)
        {
            try
            {
                if (!SmarterDeconstructionHelpers.IsSmarterDeconstructionActive()) return true;
                // Let original run; could add gating logic here to check survival tool availability.
                return true;
            }
            catch (Exception e)
            {
                LogCompatWarning("Prefix_TryTakeOrderedJob exception: " + e);
                return true;
            }
        }

        // Example mining damage adjustment hook (placeholder). Ensure mechanoids are blacklisted.
        public static void Postfix_AdjustMiningDamage(ref float __result, Pawn pawn, ThingDef target)
        {
            try
            {
                if (!SmarterDeconstructionHelpers.IsSmarterDeconstructionActive()) return;
                if (pawn == null || pawn.RaceProps == null) return;
                if (pawn.RaceProps.IsMechanoid) return; // do not modify mechanoid behavior
                // Placeholder: reduce mining damage if survival tool has wear or stats
            }
            catch (Exception e)
            {
                LogCompatWarning("Postfix_AdjustMiningDamage exception: " + e);
            }
        }
    }
}
