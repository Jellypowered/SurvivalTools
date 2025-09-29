// RimWorld 1.6 / C# 7.3
// Registers the Normal-mode research penalty StatPart exactly once when RR is present.

using System.Linq;
using Verse;
using RimWorld;

namespace SurvivalTools.Compat.RR
{
    [StaticConstructorOnStartup]
    internal static class RRBootstrap
    {
        static RRBootstrap()
        {
            try
            {
                if (!RRHelpers.IsActive()) return; // no-op if RR not loaded

                var stat = StatDefOf.ResearchSpeed;
                if (stat == null) return;

                // Avoid duplicate registration if defs reloaded / hot-reload in devmode.
                bool already = stat.parts != null &&
                               stat.parts.Any(p => p != null && p.GetType().Name == "StatPart_RR_NoToolPenalty");

                if (!already)
                {
                    if (stat.parts == null) stat.parts = new System.Collections.Generic.List<StatPart>();
                    stat.parts.Add(new StatPart_RR_NoToolPenalty());
#if DEBUG
                    if (Prefs.DevMode)
                        Log.Message("[ST×RR] Registered StatPart_RR_NoToolPenalty on ResearchSpeed");
#endif
                }
            }
            catch { /* swallow – compat must be robust */ }
        }
    }
}
