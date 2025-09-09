// RimWorld 1.6 / C# 7.3
// Source/Compatibility/ResearchReinvented/RRAutoToolIntegration.cs
//
// Integration between RR jobs and ST's auto-tool pickup system.
// Uses RRReflectionAPI.ResolveWorkGiverForJob to avoid jobDef.workGiverDef.

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Compat.ResearchReinvented
{
    public static class RRAutoToolIntegration
    {
        public static bool ShouldAttemptAutoToolForRRJob(Pawn pawn, Job job)
        {
            if (!RRReflectionAPI.IsRRActive || !RRSettings.IsRRCompatibilityEnabled)
                return false;

            if (pawn == null || job?.def == null) return false;

            var wgDef = RRReflectionAPI.RRReflectionAPI_Extensions.ResolveWorkGiverForJob(job.def);
            if (wgDef == null) return false;

            var requiredStats = RRReflectionAPI.RRReflectionAPI_Extensions.GetRequiredStatsForWorkGiver(wgDef);
            if (requiredStats.NullOrEmpty()) return false;

            if (IsDebugLoggingEnabled && IsCompatLogging())
                LogCompat($"RR job {job.def.defName} detected for {pawn.LabelShort}, requires stats: {string.Join(", ", requiredStats.Select(s => s.defName))}");

            return true;
        }

        public static List<StatDef> GetRequiredStatsForRRJob(Job job)
        {
            if (!RRReflectionAPI.IsRRActive || !RRSettings.IsRRCompatibilityEnabled || job?.def == null)
                return new List<StatDef>();

            var wgDef = RRReflectionAPI.RRReflectionAPI_Extensions.ResolveWorkGiverForJob(job.def);
            return wgDef != null
                ? RRReflectionAPI.RRReflectionAPI_Extensions.GetRequiredStatsForWorkGiver(wgDef)
                : new List<StatDef>();
        }

        public static bool ShouldBlockRRJobForMissingTools(Pawn pawn, Job job)
        {
            if (!RRReflectionAPI.IsRRActive || !RRSettings.IsRRCompatibilityEnabled)
                return false;

            if (pawn == null || job?.def == null) return false;

            var settings = SurvivalTools.Settings;
            if (settings?.extraHardcoreMode != true)
                return false;

            var requiredStats = GetRequiredStatsForRRJob(job);
            if (requiredStats.NullOrEmpty()) return false;

            foreach (var stat in requiredStats)
            {
                if (settings.IsStatRequiredInExtraHardcore(stat) && !pawn.HasSurvivalToolFor(stat))
                {
                    if (IsDebugLoggingEnabled && IsCompatLogging())
                        LogCompat($"Blocking RR job {job.def.defName} for {pawn.LabelShort} — missing required tool for {stat.defName}");
                    return true;
                }
            }

            return false;
        }

        public static Thing GetBestToolForRRJob(Pawn pawn, Job job)
        {
            var requiredStats = GetRequiredStatsForRRJob(job);
            if (requiredStats.NullOrEmpty()) return null;

            return SurvivalToolUtility.FindBestToolForStats(pawn, requiredStats);
        }
    }
}
