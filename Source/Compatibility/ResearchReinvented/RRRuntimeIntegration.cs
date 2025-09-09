// RimWorld 1.6 / C# 7.3
// Source/Compatibility/ResearchReinvented/RRRuntimeIntegration.cs
//
// Runtime helpers for bridging SurvivalTools with Research Reinvented.
// Rewritten to use WorkGiver-based API from RRReflectionAPI.

using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Compat.ResearchReinvented
{
    public static class RRRuntimeIntegration
    {
        /// <summary>
        /// Quick flag so we don’t call into RR unless active.
        /// </summary>
        public static bool IsRRActive => RRReflectionAPI.IsRRActive;

        /// <summary>
        /// Returns true if this job is associated with a RR WorkGiver.
        /// </summary>
        public static bool IsRRJob(JobDef jobDef)
        {
            if (!IsRRActive || jobDef == null) return false;

            var wgDef = RRReflectionAPI.RRReflectionAPI_Extensions.ResolveWorkGiverForJob(jobDef);
            return wgDef != null &&
                   (RRReflectionAPI.RRReflectionAPI_Extensions.IsRRWorkGiver(wgDef) || RRReflectionAPI.RRReflectionAPI_Extensions.IsFieldResearchWorkGiver(wgDef));
        }

        /// <summary>
        /// Get required stats for this RR job.
        /// </summary>
        public static List<StatDef> GetRequiredStatsForRRJob(JobDef jobDef)
        {
            if (!IsRRActive || jobDef == null) return new List<StatDef>();

            var wgDef = RRReflectionAPI.RRReflectionAPI_Extensions.ResolveWorkGiverForJob(jobDef);
            if (wgDef == null) return new List<StatDef>();

            return RRReflectionAPI.RRReflectionAPI_Extensions.GetRequiredStatsForWorkGiver(wgDef);
        }

        /// <summary>
        /// Logs unknown RR jobs so we can extend support later.
        /// </summary>
        public static void LogUnknownRRJob(JobDef jobDef)
        {
            if (jobDef == null) return;
            if (!IsDebugLoggingEnabled || !IsCompatLogging()) return;

            LogCompat($"[RRRuntimeIntegration] Unrecognized RR job: {jobDef.defName}");
        }
    }
}
