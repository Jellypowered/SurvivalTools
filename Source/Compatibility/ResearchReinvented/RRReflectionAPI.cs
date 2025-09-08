// RimWorld 1.6 / C# 7.3
// Source/Compatibility/ResearchReinvented/RRReflectionAPI.cs
//
// Reflection-based API for Research Reinvented (RR) integration.
// Provides safe wrappers that work whether RR is loaded or not.

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SurvivalTools.Compat.ResearchReinvented
{
    public static class RRReflectionAPI
    {
        private static bool? _isRRActive;

        /// <summary>
        /// Check if Research Reinvented is currently active.
        /// </summary>
        public static bool IsResearchReinventedActive()
        {
            if (_isRRActive.HasValue)
                return _isRRActive.Value;

            try
            {
                _isRRActive = ModsConfig.ActiveModsInLoadOrder
                    .Any(m =>
                        m.PackageId?.ToLowerInvariant().Contains("researchreinvented") == true ||
                        m.Name?.IndexOf("Research Reinvented", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch
            {
                _isRRActive = false;
            }

            return _isRRActive.Value;
        }

        /// <summary>
        /// Get all RR research-related stats (ResearchSpeed + FieldResearchSpeedMultiplier).
        /// </summary>
        public static List<StatDef> GetAllResearchStats()
        {
            var result = new List<StatDef>();

            var researchStat = GetResearchSpeedStat();
            if (researchStat != null) result.Add(researchStat);

            var fieldStat = GetFieldResearchSpeedStat();
            if (fieldStat != null) result.Add(fieldStat);

            return result;
        }

        /// <summary>
        /// Get the ResearchSpeed stat (from RR or vanilla).
        /// </summary>
        public static StatDef GetResearchSpeedStat()
        {
            return DefDatabase<StatDef>.GetNamedSilentFail("ResearchSpeed");
        }

        /// <summary>
        /// Get the FieldResearchSpeedMultiplier stat (RR only).
        /// </summary>
        public static StatDef GetFieldResearchSpeedStat()
        {
            return DefDatabase<StatDef>.GetNamedSilentFail("FieldResearchSpeedMultiplier");
        }

        /// <summary>
        /// Get required stats for an RR WorkGiver.
        /// Returns empty list if none or if RR inactive.
        /// </summary>
        public static List<StatDef> GetRequiredStatsForWorkGiver(WorkGiverDef wgDef)
        {
            if (!IsResearchReinventedActive() || wgDef == null)
                return new List<StatDef>();

            try
            {
                var ext = wgDef.GetModExtension<WorkGiverExtension>();
                return ext?.requiredStats ?? new List<StatDef>();
            }
            catch
            {
                return new List<StatDef>();
            }
        }

        // Inside SurvivalTools.Compat.ResearchReinvented.RRReflectionAPI
        public static bool IsRRWorkGiver(WorkGiverDef wgDef)
        {
            if (!IsResearchReinventedActive() || wgDef == null) return false;

            // Heuristic: does this WG require RR research stats?
            var stats = GetRequiredStatsForWorkGiver(wgDef);
            if (stats != null && stats.Any(s =>
                s.defName == "ResearchSpeed" ||
                s.defName == "FieldResearchSpeedMultiplier"))
            {
                return true;
            }

            return false;
        }

        public static bool IsFieldResearchWorkGiver(WorkGiverDef wgDef)
        {
            if (!IsResearchReinventedActive() || wgDef == null) return false;

            var stats = GetRequiredStatsForWorkGiver(wgDef);
            if (stats != null && stats.Any(s => s.defName == "FieldResearchSpeedMultiplier"))
                return true;

            return false;
        }

        public static WorkGiverDef ResolveWorkGiverForJob(JobDef jobDef)
        {
            if (jobDef == null) return null;

            try
            {
                // Look for a WorkGiverDef that seems associated with this job
                var wgDefs = DefDatabase<WorkGiverDef>.AllDefsListForReading;
                for (int i = 0; i < wgDefs.Count; i++)
                {
                    var wg = wgDefs[i];
                    if (wg == null) continue;

                    // If the WorkGiverDef name matches the job name, assume link
                    if (wg.defName == jobDef.defName)
                        return wg;

                    // If the job’s gerund/verb or equivalence group overlaps, we could check here too
                    // (but keep it simple for now to avoid false positives)
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Dump reflection/compatibility status for debugging.
        /// </summary>
        public static Dictionary<string, string> GetReflectionStatus()
        {
            var status = new Dictionary<string, string>
            {
                ["RR Active"] = IsResearchReinventedActive().ToString(),
                ["ResearchSpeed Stat"] = GetResearchSpeedStat()?.defName ?? "null",
                ["FieldResearchSpeed Stat"] = GetFieldResearchSpeedStat()?.defName ?? "null"
            };
            return status;
        }

        /// <summary>
        /// Initialize API (placeholder for future setup if needed).
        /// </summary>
        public static void Initialize()
        {
            // Nothing to initialize yet
        }
    }
}
