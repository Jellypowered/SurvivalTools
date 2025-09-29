// RimWorld 1.6 / C# 7.3
// Source/Compatibility/CommonSense/CommonSensePatches.cs
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;
using SurvivalTools.Compat; // for ICompatibilityModule interface

namespace SurvivalTools.Compatibility.CommonSense
{
    /// <summary>
    /// Compatibility module for CommonSense mod.
    /// Implements ICompatibilityModule so CompatAPI can discover and initialize it.
    /// All operations are defensive and safe to run when the mod is not present.
    /// </summary>
    // Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs
    internal sealed class CommonSenseCompatibilityModule : ICompatibilityModule
    {
        private Harmony _harmony;

        public string ModName => "CommonSense";

        public bool IsModActive => CommonSenseHelpers.Active;

        public void Initialize()
        {
            if (!IsModActive) return;

            try
            {
                LogCompatMessage("Initializing CommonSense compatibility...");
                _harmony = SurvivalTools.HarmonyStuff.HarmonyPatches.H ?? throw new InvalidOperationException("HarmonyPatches not initialized");

                // Example patch: intercept CommonSense.JobDriver_Ingest.PrepareToIngestToils
                var jobType = AccessTools.TypeByName("CommonSense.JobDriver_Ingest");
                if (jobType != null)
                {
                    var method = AccessTools.Method(jobType, "PrepareToIngestToils");
                    if (method != null)
                    {
                        try
                        {
                            var prefix = new HarmonyMethod(typeof(CommonSenseCompatibilityModule).GetMethod(nameof(Prefix_PrepareToIngestToils)));
                            var postfix = new HarmonyMethod(typeof(CommonSenseCompatibilityModule).GetMethod(nameof(Postfix_PrepareToIngestToils)));
                            _harmony.Patch(method, prefix, postfix);
                            LogCompatMessage("Patched CommonSense.Ingest toils (SurvivalTools hooks).", "CommonSense.IngestPatch");
                        }
                        catch (Exception e)
                        {
                            LogCompatWarning("Failed to patch CommonSense ingest toils: " + e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogCompatError("CommonSenseCompatibilityModule.Initialize failed: " + e);
            }
        }

        public List<StatDef> GetCompatibilityStats()
        {
            // CommonSense does not add stats relevant to SurvivalTools by default.
            return new List<StatDef>();
        }

        public Dictionary<string, string> GetDebugInfo()
        {
            var info = new Dictionary<string, string> { ["Active"] = IsModActive.ToString() };
            return info;
        }

        #region Patches (safe stubs)

        // Prefix - safe no-op unless CommonSense is active
        public static bool Prefix_PrepareToIngestToils(object __instance)
        {
            try
            {
                if (!CommonSenseHelpers.Active) return true;
                // Allow SurvivalTools to harmonize gear/drug policy interactions here.
                return true; // continue original
            }
            catch (Exception e)
            {
                LogCompatWarning("Prefix_PrepareToIngestToils exception: " + e);
                return true;
            }
        }

        public static void Postfix_PrepareToIngestToils(object __instance)
        {
            try
            {
                if (!CommonSenseHelpers.Active) return;
                // Reconcile SurvivalTools state after CommonSense sets up toils.
            }
            catch (Exception e)
            {
                LogCompatWarning("Postfix_PrepareToIngestToils exception: " + e);
            }
        }

        #endregion
    }
}
