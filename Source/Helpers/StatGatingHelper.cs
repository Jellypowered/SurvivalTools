// RimWorld 1.6 / C# 7.3
// Source/Helpers/StatGatingHelper.cs
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SurvivalTools.Compat;
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
        /// Determines whether a stat should block a job for the given pawn under current settings.
        /// Integrates with Research Reinvented (RR) so research jobs behave like before.
        /// </summary>
        // Unified gating rules (vanilla + RR intent)
        public static bool ShouldBlockJobForStat(StatDef stat, SurvivalToolsSettings settings, Pawn pawn = null)
        {
            if (stat == null || settings == null) return false;

            // Never hard-block CleaningSpeed or WorkSpeedGlobal here - these are handled
            // by StatPart_SurvivalTool as a penalty-based fallback and should not abort jobs.
            if (stat == ST_StatDefOf.CleaningSpeed || stat == ST_StatDefOf.WorkSpeedGlobal)
                return false;

            // Core work stats (mining, construction, etc.) always gate in hardcore.
            if (StatFilters.ShouldBlockJobForMissingStat(stat))
            {
                bool result = pawn == null || !pawn.HasSurvivalToolFor(stat);
                // Debug logging for deconstruction decisions
                if (ST_Logging.IsDebugLoggingEnabled && stat == ST_StatDefOf.DeconstructionSpeed)
                {
                    string pawnId = pawn?.ThingID ?? "null";
                    string msg = result
                        ? $"[SurvivalTools.StatGatingHelper] Stat '{stat.defName}' -> BLOCK job for pawn {pawnId} (missing tool)."
                        : $"[SurvivalTools.StatGatingHelper] Stat '{stat.defName}' -> ALLOW job for pawn {pawnId} (has tool).";
                    ST_Logging.LogDebug(msg, $"StatGate_Deconstruct_{pawnId}");
                }
                return result;
            }

            // Optional families — only hard-gate if explicitly enabled or in extra-hardcore.
            bool xhc = settings.extraHardcoreMode;

            if (stat == ST_StatDefOf.CleaningSpeed)
                return (xhc || settings.requireCleaningTools) && (pawn == null || !pawn.HasSurvivalToolFor(stat));

            if (stat == ST_StatDefOf.ButcheryFleshSpeed || stat == ST_StatDefOf.ButcheryFleshEfficiency)
                return (xhc || settings.requireButcheryTools) && (pawn == null || !pawn.HasSurvivalToolFor(stat));

            if (stat == ST_StatDefOf.MedicalOperationSpeed || stat == ST_StatDefOf.MedicalSurgerySuccessChance)
                return (xhc || settings.requireMedicalTools) && (pawn == null || !pawn.HasSurvivalToolFor(stat));

            // Research is **not** hard-blocked in normal hardcore — job may run but progress=~0 via StatPart.
            if (stat == ST_StatDefOf.ResearchSpeed)
                return xhc && (pawn == null || !pawn.HasSurvivalToolFor(stat));

            // Extra-hardcore custom rules (RR or future packs)
            if (xhc && settings.IsStatRequiredInExtraHardcore(stat))
            {
                bool result = pawn == null || !pawn.HasSurvivalToolFor(stat);
                if (ST_Logging.IsDebugLoggingEnabled && stat == ST_StatDefOf.DeconstructionSpeed)
                {
                    string pawnId = pawn?.ThingID ?? "null";
                    string msg = result
                        ? $"[SurvivalTools.StatGatingHelper] Extra-hardcore rule: Stat '{stat.defName}' -> BLOCK job for pawn {pawnId} (missing tool)."
                        : $"[SurvivalTools.StatGatingHelper] Extra-hardcore rule: Stat '{stat.defName}' -> ALLOW job for pawn {pawnId} (has tool).";
                    ST_Logging.LogDebug(msg, $"StatGate_Deconstruct_XHC_{pawnId}");
                }
                return result;
            }

            return false;
        }



        /// <summary>
        /// Detect required stats for a WorkGiver by extension or defName patterns.
        /// Covers vanilla WorkGivers that lack WorkGiverExtension.
        /// </summary>
        public static List<StatDef> GetStatsForWorkGiver(WorkGiverDef wgDef)
        {
            if (wgDef == null) return new List<StatDef>();

            // 1) Explicit extension wins
            var fromExtension = wgDef.GetModExtension<WorkGiverExtension>()?.requiredStats;
            if (fromExtension != null && fromExtension.Any())
                return fromExtension.Where(s => s != null && s.RequiresSurvivalTool()).ToList();

            // 2) Heuristics by defName
            string name = wgDef.defName?.ToLowerInvariant() ?? string.Empty;
            var stats = new List<StatDef>();

            // Research
            if (name.Contains("research"))
                stats.Add(ST_StatDefOf.ResearchSpeed);

            // Cleaning
            if (name.Contains("clean"))
                stats.Add(ST_StatDefOf.CleaningSpeed);

            // Roofing is construction
            // (RimWorld vanilla def is "BuildRoofs"; other mods may include "roof" in name)
            if (name == "buildroofs" || name.Contains("roof"))
                stats.Add(StatDefOf.ConstructionSpeed);

            // Construction vs maintenance vs deconstruction
            // Build / construct / frames / blueprints => ConstructionSpeed
            if (name.Contains("construct") || name.Contains("build") || name.Contains("frame") || name.Contains("blueprint"))
                stats.Add(StatDefOf.ConstructionSpeed);

            // Repair / maintain => MaintenanceSpeed
            if (name.Contains("repair") || name.Contains("maintain") || name.Contains("maintenance"))
                stats.Add(ST_StatDefOf.MaintenanceSpeed);

            // Deconstruct / remove / uninstall => DeconstructionSpeed
            if (name.Contains("deconstruct") || name.Contains("remove") || name.Contains("uninstall"))
                stats.Add(ST_StatDefOf.DeconstructionSpeed);

            // Trees & plants
            if (name.Contains("felltree") || name.Contains("chopwood") || name.Contains("cutwood"))
                stats.Add(ST_StatDefOf.TreeFellingSpeed);

            if (name.Contains("harvest") || name.Contains("plantscut"))
                stats.Add(ST_StatDefOf.PlantHarvestingSpeed);

            if (name.Contains("grow") || name.Contains("sow") || name.Contains("plant"))
                stats.Add(ST_StatDefOf.SowingSpeed);

            // Mining
            if (name.Contains("mine") || name.Contains("drill"))
            {
                stats.Add(ST_StatDefOf.DiggingSpeed);
                stats.Add(ST_StatDefOf.MiningYieldDigging);
            }

            // Butchery
            if (name.Contains("butcher") || name.Contains("slaughter"))
            {
                stats.Add(ST_StatDefOf.ButcheryFleshSpeed);
                stats.Add(ST_StatDefOf.ButcheryFleshEfficiency);
            }

            // Keep only real survival-tool stats, unique
            return stats.Where(s => s != null && s.RequiresSurvivalTool()).Distinct().ToList();
        }

        /// <summary>
        /// Centralized BuildRoof gating helper.
        /// Returns true if the pawn should be blocked from BuildRoof jobs under current settings.
        /// Produces a small logKey suitable for ShouldLogWithCooldown when needed.
        /// </summary>
        public static bool ShouldBlockBuildRoof(Pawn pawn, out string logKey, IntVec3? cell = null)
        {
            logKey = null;
            if (pawn == null) return false;

            var settings = SurvivalTools.Settings;
            if (settings == null || settings.extraHardcoreMode != true) return false; // only hard-block in extra-hardcore

            // Respect general pawn/tool capability (mechs, prisoners, etc.)
            if (!PawnToolValidator.CanUseSurvivalTools(pawn)) return false;

            // ConstructionSpeed is the canonical stat for roofing
            var stat = StatDefOf.ConstructionSpeed;
            if (stat == null) return false;

            bool should = ShouldBlockJobForStat(stat, settings, pawn);
            if (should)
            {
                // Build a compact log key: include pawn id and optional cell coordinates
                if (cell.HasValue)
                    logKey = $"BuildRoof_Block_{pawn.ThingID}_{cell.Value.x}_{cell.Value.y}_{cell.Value.z}";
                else
                    logKey = $"BuildRoof_Block_{pawn.ThingID}";
            }
            return should;
        }
    }
}
/*
    📌 NOTE FOR FUTURE ME:

    Mining yield (ST_StatDefOf.MiningYieldDigging) should generally be treated as *optional*.
    Pawns without tools should still be able to mine, they’ll just be less efficient.

    If you want to enforce this cleanly, update StatFilters.cs like so:

        public static bool IsOptionalStat(StatDef stat)
        {
            if (stat == null) return true;

            // Research, cleaning, medical, and mining yield are optional
            return stat == ST_StatDefOf.ResearchSpeed ||
                   stat == ST_StatDefOf.CleaningSpeed ||
                   stat == ST_StatDefOf.MedicalOperationSpeed ||
                   stat == ST_StatDefOf.MedicalSurgerySuccessChance ||
                   stat == ST_StatDefOf.ButcheryFleshEfficiency ||
                   stat == ST_StatDefOf.MiningYieldDigging; // 👈 new optional stat
        }

    That way mining *speed* stays mandatory (hardcore tool gating), but *yield* just becomes a bonus.
*/
