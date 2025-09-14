// RimWorld 1.6 / C# 7.3
// Source/Compatibility/TDEnhancementPack/TDEnhancementPackPatches.cs
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

// Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs
using static SurvivalTools.ST_Logging;
using SurvivalTools.Compat;
using SurvivalTools.Compat.SmarterDeconstruction;
using System.Reflection;
using SurvivalTools.Helpers;

namespace SurvivalTools.Compat.TDEnhancementPack
{
    /// <summary>
    /// Harmony patches for TD Enhancement Pack compatibility.
    /// Patches are safe no-ops when the mod is absent.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class TDEnhancementPackPatches
    {
        static TDEnhancementPackPatches()
        {
            try
            {
                if (!TDEnhancementPackHelpers.IsTDEnhancementPackActive())
                    return;

                var harmony = new Harmony("com.survivaltools.compat.tdenhancementpack");

                // Placeholder for any early static patches that are safe to apply.
                // Most hooks are registered via the ICompatibilityModule below to allow dedup logic.
            }
            catch (Exception ex)
            {
                LogCompatError("Failed to initialize TD Enhancement Pack compat: " + ex);
            }
        }
    }

    /// <summary>
    /// TD Enhancement Pack compatibility. Deduplicates overlapping hooks with SmarterDeconstruction.
    /// </summary>
    internal sealed class TDEnhancementPackCompatibilityModule : ICompatibilityModule
    {
        private Harmony _harmony;
        public string ModName => "TD Enhancement Pack";
        public bool IsModActive => TDEnhancementPackHelpers.IsTDEnhancementPackActive();

        public void Initialize()
        {
            if (!IsModActive) return;
            try
            {
                _harmony = new Harmony("com.jellypowered.survivaltools.compat.tdenhancementpack");

                // If SmarterDeconstruction compat is present, avoid duplicating WorkGiver_Scanner hooks.
                var sdModule = CompatibilityRegistry.GetModule("SmarterDeconstruction");
                if (sdModule != null && sdModule.IsModActive)
                {
                    LogCompatMessage("TD Enhancement Pack detected but SmarterDeconstruction compat active; checking ownership of WorkGiver_Scanner hooks...", "TD.Enhancement.Dedup");
                    try
                    {
                        // Prefer the helper method on SmarterDeconstruction for a robust ownership check
                        if (SmarterDeconstruction.SmarterDeconstructionHelpers.ShouldOwnWorkGiverScannerHooks())
                        {
                            LogCompatMessage("SmarterDeconstruction owns WorkGiver_Scanner hooks; skipping TD overlapping hooks.", "TD.Enhancement.Dedup");
                            return;
                        }
                    }
                    catch { /* ignore and proceed to apply TD hooks if needed */ }
                }

                // Apply TD-specific hooks here. Keep them defensive and lightweight.
                // Parity: ensure TD's build-roof logic respects SurvivalTools gating in extra-hardcore.
                try
                {
                    var t = AccessTools.TypeByName("WorkGiver_BuildRoof") ?? AccessTools.TypeByName("WorkGiver_BuildRoofs");
                    if (t != null)
                    {
                        var hasCell = AccessTools.Method(t, "HasJobOnCell", new Type[] { typeof(Pawn), typeof(IntVec3), typeof(bool) });
                        if (hasCell != null)
                        {
                            try
                            {
                                var prefix = new HarmonyMethod(typeof(TDEnhancementPackCompatibilityModule).GetMethod(nameof(Prefix_BuildRoof_HasJobOnCell), BindingFlags.NonPublic | BindingFlags.Static));
                                _harmony.Patch(hasCell, prefix: prefix);
                            }
                            catch { }
                        }
                        var jobOnCell = AccessTools.Method(t, "JobOnCell", new Type[] { typeof(Pawn), typeof(IntVec3) });
                        if (jobOnCell != null)
                        {
                            try
                            {
                                var prefix2 = new HarmonyMethod(typeof(TDEnhancementPackCompatibilityModule).GetMethod(nameof(Prefix_BuildRoof_JobOnCell), BindingFlags.NonPublic | BindingFlags.Static));
                                _harmony.Patch(jobOnCell, prefix: prefix2);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            catch (Exception e)
            {
                LogCompatError("TDEnhancementPack.Initialize failed: " + e);
            }

            // Lightweight prefixes are defined in class scope below.
        }

        public List<StatDef> GetCompatibilityStats() => new List<StatDef>();

        public Dictionary<string, string> GetDebugInfo() => new Dictionary<string, string> { ["Active"] = IsModActive.ToString() };

        // Lightweight prefixes to keep TD's roofing behavior in parity with core gating
        // These mirror the core gating: deny BuildRoof assignment in extra-hardcore when pawn lacks construction tools.
        private static bool Prefix_BuildRoof_HasJobOnCell(object __instance, Pawn pawn, IntVec3 c, bool forced, ref bool __result)
        {
            try
            {
                if (pawn == null) return true;
                try
                {
                    if (StatGatingHelper.ShouldBlockBuildRoof(pawn, out string sk, c))
                    {
                        __result = false;
                        try { if (ShouldLogWithCooldown(sk)) Log.Message($"[SurvivalTools.ToolGate] TD: denying BuildRoof for {pawn.LabelShort} at {c}"); } catch { }
                        return false;
                    }
                }
                catch { }
            }
            catch { }
            return true;
        }

        private static bool Prefix_BuildRoof_JobOnCell(object __instance, Pawn pawn, IntVec3 c, ref Job __result)
        {
            try
            {
                if (pawn == null) return true;
                try
                {
                    if (StatGatingHelper.ShouldBlockBuildRoof(pawn, out string sk2, c))
                    {
                        __result = null;
                        try { if (ShouldLogWithCooldown(sk2)) Log.Message($"[SurvivalTools.ToolGate] TD: skipping BuildRoof.JobOnCell for {pawn.LabelShort} at {c}"); } catch { }
                        return false;
                    }
                }
                catch { }
            }
            catch { }
            return true;
        }
    }
}
