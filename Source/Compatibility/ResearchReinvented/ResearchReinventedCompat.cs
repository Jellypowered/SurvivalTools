// RimWorld 1.6 / C# 7.3
// Source/Compat/ResearchReinvented/ResearchReinventedCompat.cs
using System;
using HarmonyLib;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Compat
{
    /// <summary>
    /// Entry point for RR compatibility. Safe no-op if RR is not present.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ResearchReinventedCompat
    {
        static ResearchReinventedCompat()
        {
            try
            {
                if (!RRReflectionAPI.IsRRActive)
                {
                    if (IsDebugLoggingEnabled) Log.Message("[SurvivalTools Compat] Research Reinvented not detected — RR compat disabled.");
                    return;
                }

                var h = new Harmony("SurvivalTools.RRCompat");
                RRReflectionAPI.ApplyHarmonyHooks(h);

                if (IsDebugLoggingEnabled) Log.Message("[SurvivalTools Compat] RR compatibility initialized (postfixes on CanEverDoResearch/CanNowDoResearch).");
            }
            catch (Exception e)
            {
                Log.Error($"[SurvivalTools Compat] RR init failed: {e}");
            }
        }
    }
}
