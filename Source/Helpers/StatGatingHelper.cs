// RimWorld 1.6 / C# 7.3
// Source/Helpers/StatGatingHelper.cs
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SurvivalTools.Helpers
{
    /// <summary>
    /// Centralized logic for stat gating and WorkGiver → stat mapping.
    /// Replaces scattered versions in SurvivalToolUtility, Validation, and Scanner patches.
    /// </summary>
    public static class StatGatingHelper
    {
        /// <summary>
        /// Unified check if a stat should block jobs based on settings and optionally pawn.
        /// If pawn is null, only determines if the stat is considered mandatory in principle.
        /// If pawn is supplied, also checks whether the pawn has an appropriate tool.
        /// </summary>
        public static bool ShouldBlockJobForStat(StatDef stat, SurvivalToolsSettings settings, Pawn pawn = null)
        {
            if (stat == null || settings == null) return false;

            // Core work stats (construction, mining, farming, etc.)
            if (StatFilters.ShouldBlockJobForMissingStat(stat))
                return pawn == null || !pawn.HasSurvivalToolFor(stat);

            // Cleaning
            if (stat == ST_StatDefOf.CleaningSpeed)
            {
                if (settings.extraHardcoreMode && settings.requireCleaningTools)
                    return pawn == null || !pawn.HasSurvivalToolFor(stat);
                return false;
            }

            // Butchery
            if (stat == ST_StatDefOf.ButcheryFleshSpeed || stat == ST_StatDefOf.ButcheryFleshEfficiency)
            {
                if (settings.extraHardcoreMode && settings.requireButcheryTools)
                    return pawn == null || !pawn.HasSurvivalToolFor(stat);
                return false;
            }

            // Medical
            if (stat == ST_StatDefOf.MedicalOperationSpeed || stat == ST_StatDefOf.MedicalSurgerySuccessChance)
            {
                if (settings.extraHardcoreMode && settings.requireMedicalTools)
                    return pawn == null || !pawn.HasSurvivalToolFor(stat);
                return false;
            }

            // Research is optional by default
            if (stat == ST_StatDefOf.ResearchSpeed)
                return false;

            // RR or other extra-hardcore integrations
            if (settings.extraHardcoreMode && settings.IsStatRequiredInExtraHardcore(stat))
                return pawn == null || !pawn.HasSurvivalToolFor(stat);

            return false;
        }

        /// <summary>
        /// Detect required stats for a WorkGiver by extension or defName patterns.
        /// Covers vanilla WorkGivers that lack WorkGiverExtension.
        /// </summary>
        public static List<StatDef> GetStatsForWorkGiver(WorkGiverDef wgDef)
        {
            if (wgDef == null) return new List<StatDef>();

            // Explicit WorkGiverExtension
            var fromExtension = wgDef.GetModExtension<WorkGiverExtension>()?.requiredStats;
            if (fromExtension != null && fromExtension.Any())
                return fromExtension.Where(s => s != null && s.RequiresSurvivalTool()).ToList();

            var defName = wgDef.defName.ToLower();
            var stats = new List<StatDef>();

            if (defName.Contains("clean"))
                stats.Add(ST_StatDefOf.CleaningSpeed);

            if (defName.Contains("doctor") || defName.Contains("tend") || defName.Contains("surgery"))
            {
                stats.Add(ST_StatDefOf.MedicalOperationSpeed);
                stats.Add(ST_StatDefOf.MedicalSurgerySuccessChance);
            }

            if (defName.Contains("butcher") || defName.Contains("slaughter"))
            {
                stats.Add(ST_StatDefOf.ButcheryFleshSpeed);
                stats.Add(ST_StatDefOf.ButcheryFleshEfficiency);
            }

            if (defName.Contains("felltree") || defName.Contains("chopwood"))
                stats.Add(ST_StatDefOf.TreeFellingSpeed);

            if (defName.Contains("harvest") || defName.Contains("plantscut"))
                stats.Add(ST_StatDefOf.PlantHarvestingSpeed);

            if (defName.Contains("construct") || defName.Contains("build") || defName.Contains("repair") || defName.Contains("maintain"))
                stats.Add(ST_StatDefOf.MaintenanceSpeed);

            if (defName.Contains("deconstruct") || defName.Contains("demolish"))
                stats.Add(ST_StatDefOf.DeconstructionSpeed);

            if (defName.Contains("mine") || defName.Contains("drill"))
            {
                stats.Add(ST_StatDefOf.DiggingSpeed);
                stats.Add(ST_StatDefOf.MiningYieldDigging);
            }

            if (defName.Contains("research"))
                stats.Add(ST_StatDefOf.ResearchSpeed);

            if (defName.Contains("grow") || defName.Contains("sow") || defName.Contains("plant"))
                stats.Add(ST_StatDefOf.SowingSpeed);

            return stats.Distinct().ToList();
        }
    }
}
