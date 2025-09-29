// RimWorld 1.6 / C# 7.3
// Source/Compatibility/RightClickEligibilityBootstrap.cs
// Phase 10: Central registration of core (vanilla + common mod) WorkGiver worker subclasses
// for right-click tool rescue eligibility. Excludes pure delivery workers.

using System;
using HarmonyLib;

namespace SurvivalTools.Compatibility.RightClick
{
    internal static class RightClickEligibilityBootstrap
    {
        private static bool _initialized;
        internal static void Initialize()
        {
            if (_initialized) return; _initialized = true;
            try
            {
                // Helper local
                void Reg(string typeName)
                {
                    if (string.IsNullOrEmpty(typeName)) return;
                    try
                    {
                        var t = AccessTools.TypeByName(typeName) ?? AccessTools.TypeByName("RimWorld." + typeName);
                        if (t != null)
                            Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(t);
                    }
                    catch { /* ignore individual failures */ }
                }

                // Mining
                Reg("WorkGiver_Miner");

                // Construction / smoothing / repair (omit delivery workers – pure delivery exempt)
                Reg("WorkGiver_ConstructFinishFrames");
                Reg("WorkGiver_ConstructSmoothWall");
                Reg("WorkGiver_ConstructSmoothFloor");
                Reg("WorkGiver_Repair");

                // Deconstruct / uninstall
                Reg("WorkGiver_Deconstruct");
                Reg("WorkGiver_Uninstall");

                // Plants (cut / harvest / sow – tree authority variants registered separately in TreeWorkGiverMappings)
                Reg("WorkGiver_PlantsCut");
                Reg("WorkGiver_PlantsHarvest");
                Reg("WorkGiver_PlantsSow");

                // Cleaning (vanilla + CommonSense variant if present)
                Reg("WorkGiver_Clean");
                Reg("CommonSense.WorkGiver_Clean");

                // Research (vanilla + RR field research if present)
                Reg("WorkGiver_Research");
                Reg("WorkGiver_DoResearch"); // alternate naming safeguard
                Reg("WorkGiver_FieldResearch");

                // Tree worker variants handled in TreeWorkGiverMappings based on authority.
                SurvivalTools.UI.RightClickRescue.ST_RightClickRescueProvider.LogSummaryOnce();
            }
            catch (Exception e)
            {
                Verse.Log.Warning("[SurvivalTools][RightClickEligibilityBootstrap] Init error: " + e.Message);
            }
        }
    }
}
