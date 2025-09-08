// RimWorld 1.6 / C# 7.3
// Source/Helpers/JobDefToWorkGiverDefHelper.cs
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

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
        /// Explicit mappings (JobDef → WorkGiverDef) for jobs where the link
        /// is not obvious or where defName patterns are unreliable.
        /// 
        /// 🔮 Future: could be externalized to XML defs or settings for easier patching
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

            // Hauling
            { "HaulToCell", "HaulGeneral" },
            { "HaulToContainer", "HaulGeneral" },

            // Cleaning
            { "Clean", "CleanFilth" },

            // Butchery
            { "DoBillsButcherFlesh", "DoBillsButcherFlesh" },
            { "DoBillsButcherCorpse", "DoBillsButcherCorpse" },

            // Research
            { "Research", "Research" }
        };

        /// <summary>
        /// Finds the WorkGiverDef that produces the given JobDef, if any.
        /// 
        /// Order of resolution:
        ///  1. Check explicit hardcoded mapping table.
        ///  2. Fallback to heuristic: match job defName against WorkGiver defName or label.
        /// 
        /// Returns null if nothing reasonable can be found.
        /// </summary>
        public static WorkGiverDef GetWorkGiverDefForJob(JobDef jobDef)
        {
            if (jobDef == null) return null;

            // Step 1: explicit mapping
            if (ExplicitJobToWorkGiverMap.TryGetValue(jobDef.defName, out string wgDefName))
            {
                return DefDatabase<WorkGiverDef>.GetNamedSilentFail(wgDefName);
            }

            // Step 2: heuristic fallback
            string jobName = jobDef.defName.ToLower();
            return DefDatabase<WorkGiverDef>.AllDefsListForReading
                .FirstOrDefault(wg =>
                    wg != null &&
                    (wg.defName.ToLower().Contains(jobName) ||
                     (!string.IsNullOrEmpty(wg.label) && wg.label.ToLower().Contains(jobName))));
        }
    }
}
