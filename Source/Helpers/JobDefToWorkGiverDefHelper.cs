// RimWorld 1.6 / C# 7.3
// Source/Helpers/JobDefToWorkGiverDefHelper.cs

// TODO: Is this duplicate of other functionality in our mod? 
// evaluate and consolidate if so.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Helpers
{
    /// <summary>
    /// Helper to map a JobDef to its associated WorkGiverDef.
    /// Used in tool gating / stat checks when we need to trace
    /// from the active job back to the workgiver logic that created it.
    /// </summary>
    public static class JobDefToWorkGiverDefHelper
    {
        /// <summary>
        /// Explicit mappings (JobDef â†’ WorkGiverDef) for jobs where the link
        /// is not obvious or where defName patterns are unreliable.
        /// 
        /// ðŸ”® Future: could be externalized to XML defs or settings for easier patching
        /// by other mods (instead of hardcoding here).
        /// </summary>
        private static readonly Dictionary<string, string> ExplicitJobToWorkGiverMap =
            new Dictionary<string, string>
        {
            // Tree / plant harvesting
            { "HarvestTreeDesignated", "FellTrees" },
            { "CutPlantDesignated", "PlantsCut" },
            { "HarvestDesignated", "GrowerHarvest" },

            // Mining
            { "Mine", "Miner" },
            { "MineNonRock", "Miner" },

            // Construction (explicit)
            { "BuildRoof", "BuildRoofs" },   // vanilla 1.6
            { "RoofJob", "BuildRoofs" },     // legacy / some mods

            // Hauling
            { "HaulToCell", "HaulGeneral" },
            { "HaulToContainer", "HaulGeneral" },

            // Cleaning
            { "Clean", "CleanFilth" },

            // Butchery
            { "DoBillsButcherFlesh", "DoBillsButcherFlesh" },
            { "DoBillsButcherCorpse", "DoBillsButcherCorpse" },

            // Research
            { "Research", "Research" },
            { "FieldResearch", "Research" } // RR: extra job type
        };

        /// <summary>
        /// Finds the WorkGiverDef that produces the given JobDef, if any.
        /// 
        /// Order of resolution:
        ///  1. Check explicit hardcoded mapping table.
        ///  2. Special-case fallbacks (construction, RR jobs, plants, etc).
        ///  3. Heuristic: match job defName against WorkGiver defName or label.
        /// 
        /// Returns null if nothing reasonable can be found.
        /// </summary>
        public static WorkGiverDef GetWorkGiverDefForJob(JobDef jobDef)
        {
            if (jobDef == null) return null;

            // Step 1: explicit mapping
            if (ExplicitJobToWorkGiverMap.TryGetValue(jobDef.defName, out string wgDefName))
            {
                // Emit one cooldown-protected log when roofing mapping is used so we can verify resolution
                if ((jobDef.defName == "BuildRoof" || jobDef.defName == "RoofJob") && ShouldLogWithCooldown("JobDefToWorkGiver_BuildRoof"))
                {
                    LogCompat($"JobDefToWorkGiver: mapped {jobDef.defName} -> {wgDefName}");
                }

                return DefDatabase<WorkGiverDef>.GetNamedSilentFail(wgDefName);
            }

            // Step 2: special-case fallbacks
            var defName = jobDef.defName.ToLower();

            // ðŸ”¨ Construction (catch-all: build, construct, roof, floor, blueprints, frames)
            if (defName.Contains("construct") || defName.Contains("build") ||
                defName.Contains("roof") || defName.Contains("floor") ||
                defName.Contains("blueprint") || defName.Contains("frame"))
            {
                return DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructGeneral")
                    ?? DefDatabase<WorkGiverDef>.GetNamedSilentFail("BuildRoofs")
                    ?? DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResources");
            }

            // ðŸ“– Research Reinvented â€“ fieldwork jobs
            if (defName.Contains("fieldresearch") || defName.Contains("survey"))
                return DefDatabase<WorkGiverDef>.GetNamedSilentFail("Research");

            // ðŸŒ± Plant work (catch-all: sow, harvest, cut)
            if (defName.Contains("sow"))
                return DefDatabase<WorkGiverDef>.GetNamedSilentFail("GrowerSow");
            if (defName.Contains("harvest"))
                return DefDatabase<WorkGiverDef>.GetNamedSilentFail("GrowerHarvest");
            if (defName.Contains("cutplant"))
                return DefDatabase<WorkGiverDef>.GetNamedSilentFail("PlantsCut");

            // Step 3: heuristic fallback (best effort)
            return DefDatabase<WorkGiverDef>.AllDefsListForReading
                .FirstOrDefault(wg =>
                    wg != null &&
                    (wg.defName.ToLower().Contains(defName) ||
                     (!string.IsNullOrEmpty(wg.label) && wg.label.ToLower().Contains(defName))));
        }
    }
}

