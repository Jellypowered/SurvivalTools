using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools
{
    /// <summary>
    /// Defines the types of survival tools available.
    /// </summary>
    public enum STToolKind
    {
        None,
        Axe,
        Hammer,
        Pick,
        Knife,
        Hoe,
        Saw,
        Sickle,
        Wrench
    }

    /// <summary>
    /// Provides utility methods for working with survival tools.
    /// This class centralizes logic for tool identification, selection, and job-to-tool mapping.
    /// </summary>
    public static class ToolUtility
    {
        #region Tool Identification

        /// <summary>
        /// Determines the <see cref="STToolKind"/> of a given thing based on its defName.
        /// </summary>
        /// <param name="t">The thing to check.</param>
        /// <returns>The corresponding <see cref="STToolKind"/>, or <see cref="STToolKind.None"/> if it's not a recognized tool.</returns>
        public static STToolKind ToolKindOf(Thing t)
        {
            if (t == null || t.def == null) return STToolKind.None;
            string dn = t.def.defName?.ToLowerInvariant() ?? string.Empty;

            // Check specific names before generic ones to avoid incorrect matches (e.g., "pickaxe" as "axe").
            if (dn.Contains("pickaxe") || dn.Contains("pick")) return STToolKind.Pick;
            if (dn.Contains("sickle")) return STToolKind.Sickle;
            if (dn.Contains("hammer") || dn.Contains("mallet")) return STToolKind.Hammer;
            if (dn.Contains("wrench") || dn.Contains("prybar") || dn.Contains("primitivelever")) return STToolKind.Wrench;
            if (dn.Contains("hatchet")) return STToolKind.Axe;
            if (dn.Contains("axe")) return STToolKind.Axe;
            if (dn.Contains("hoe")) return STToolKind.Hoe;
            if (dn.Contains("saw")) return STToolKind.Saw;
            if (dn.Contains("knife")) return STToolKind.Knife;
            if (dn.Contains("flintscraper")) return STToolKind.Knife;
            return STToolKind.None;
        }

        /// <summary>
        /// Checks if a thing is a survival tool.
        /// It first checks for the presence of a CompSurvivalTool component, then falls back to defName heuristics.
        /// </summary>
        /// <param name="t">The thing to check.</param>
        /// <returns>True if the thing is a survival tool, otherwise false.</returns>
        public static bool IsSurvivalTool(Thing t)
        {
            if (t == null || t.def == null) return false;

            // Prefer comp detection for accuracy.
            if (t is ThingWithComps twc && twc.AllComps.Any(c => c.GetType().Name.Contains("CompSurvivalTool")))
            {
                return true;
            }

            // Fallback for things without comps, using defName heuristics.
            return ToolKindOf(t) != STToolKind.None;
        }

        #endregion

        #region Job-to-Tool Mapping

        /// <summary>
        /// Determines the expected tool kind for a given job.
        /// This logic is central to deciding which tool a pawn should use for their current task.
        /// </summary>
        /// <param name="pawn">The pawn performing the job.</param>
        /// <param name="job">The job being performed.</param>
        /// <returns>The expected <see cref="STToolKind"/> for the job.</returns>
        public static STToolKind ExpectedToolFor(Pawn pawn, Job job)
        {
            if (job?.def == null) return STToolKind.None;

            string jobDefName = job.def.defName ?? string.Empty;
            string driverClassName = job.def.driverClass?.Name ?? string.Empty;
            string currentDriverName = pawn?.jobs?.curDriver?.GetType().Name ?? string.Empty;

            string searchString = (jobDefName + "|" + driverClassName + "|" + currentDriverName).ToLowerInvariant();

            // Mining & Drilling
            if (searchString.Contains("mine") || searchString.Contains("drill"))
                return STToolKind.Pick;

            // Construction Family
            if (searchString.Contains("construct") || searchString.Contains("frame") || searchString.Contains("smooth") ||
                searchString.Contains("buildroof") || searchString.Contains("removeroof") || searchString.Contains("build") ||
                searchString.Contains("deliver") || searchString.Contains("install"))
                return STToolKind.Hammer;

            // Maintenance & Repair
            if (searchString.Contains("repair") || searchString.Contains("maintain") || searchString.Contains("maintenance") ||
                searchString.Contains("fixbroken") || searchString.Contains("tendmachine") || searchString.Contains("fix"))
                return STToolKind.Wrench;

            // Deconstruction
            if (searchString.Contains("uninstall") || searchString.Contains("deconstruct") || searchString.Contains("teardown"))
                return STToolKind.Wrench;

            // Plant Work
            if (searchString.Contains("plantcut") || searchString.Contains("cutplant") || searchString.Contains("chop") || searchString.Contains("prune"))
                return STToolKind.Sickle; // Plant cutting (changed from Axe to Sickle)

            if (searchString.Contains("sow") || searchString.Contains("plantsow") || searchString.Contains("plantgrow"))
                return STToolKind.Hoe; // Sowing

            if (searchString.Contains("harvest"))
                return STToolKind.Sickle; // Harvesting

            return STToolKind.None;
        }

        #endregion

        #region Active Tool Selection

        /// <summary>
        /// Tries to find the most appropriate tool in a pawn's inventory for their current job.
        /// </summary>
        /// <param name="pawn">The pawn to check.</param>
        /// <returns>The best-suited tool, or null if no suitable tool is found.</returns>
        public static Thing TryGetActiveTool(Pawn pawn)
        {
            if (pawn?.inventory?.innerContainer == null || pawn.jobs?.curJob == null)
                return null;

            STToolKind expectedKind = ExpectedToolFor(pawn, pawn.jobs.curJob);
            if (expectedKind == STToolKind.None) return null;

            var inventory = pawn.inventory.innerContainer;

            // First pass: Exact match for the expected tool kind.
            foreach (var tool in inventory)
            {
                if (IsSurvivalTool(tool) && ToolKindOf(tool) == expectedKind)
                {
                    return tool;
                }
            }

            // Second pass: Targeted fallbacks for related tools.
            if (expectedKind == STToolKind.Pick)
            {
                foreach (var tool in inventory)
                {
                    if (ToolKindOf(tool) == STToolKind.Pick) return tool;
                }
            }

            if (expectedKind == STToolKind.Hoe)
            {
                foreach (var tool in inventory)
                {
                    // Allow sickles for sowing if no hoe is available.
                    if (IsSurvivalTool(tool) && ToolKindOf(tool) == STToolKind.Sickle) return tool;
                }
            }

            if (expectedKind == STToolKind.Sickle)
            {
                foreach (var tool in inventory)
                {
                    // In non-hardcore mode, allow hoes for plant cutting if no sickle is available.
                    // In hardcore mode, only sickles can cut plants.
                    if (!SurvivalToolUtility.IsHardcoreModeEnabled && IsSurvivalTool(tool) && ToolKindOf(tool) == STToolKind.Hoe)
                        return tool;
                }
            }

            if (expectedKind == STToolKind.Hammer)
            {
                foreach (var tool in inventory)
                {
                    if (ToolKindOf(tool) == STToolKind.Hammer) return tool;
                }
            }

            // If no suitable tool is found, return null.
            return null;
        }

        #endregion
    }
}
