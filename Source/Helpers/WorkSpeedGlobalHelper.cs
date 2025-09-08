using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SurvivalTools.Helpers
{
    /// <summary>
    /// Helper methods for WorkSpeedGlobal job detection and management
    /// </summary>
    public static class WorkSpeedGlobalHelper
    {
        /// <summary>
        /// Get all work givers that use the WorkSpeedGlobal stat
        /// </summary>
        public static IEnumerable<WorkGiverDef> GetWorkSpeedGlobalJobs()
        {
            return DefDatabase<WorkGiverDef>.AllDefs
                .Where(workGiver => UsesWorkSpeedGlobal(workGiver))
                .OrderBy(workGiver => workGiver.workType?.label ?? "Unknown")
                .ThenBy(workGiver => workGiver.label ?? workGiver.defName);
        }

        /// <summary>
        /// Check if a work giver uses the WorkSpeedGlobal stat
        /// </summary>
        public static bool UsesWorkSpeedGlobal(WorkGiverDef workGiver)
        {
            if (workGiver == null) return false;

            // Check if it's explicitly listed in vanilla job drivers that use WorkSpeedGlobal
            var knownWorkSpeedJobs = new HashSet<string>
            {
                "DoBill", "Make", "Craft", "Cook", "Smith", "Tailor", "Art", "Sculpt",
                "Research", "Study", "Train", "Teach", "Learn", "Practice"
            };

            if (knownWorkSpeedJobs.Any(job =>
                workGiver.defName.Contains(job) ||
                (!string.IsNullOrEmpty(workGiver.label) && workGiver.label.ToLower().Contains(job.ToLower()))))
            {
                return true;
            }

            // Check work type
            if (workGiver.workType != null)
            {
                var workTypesThatUseWorkSpeed = new HashSet<string>
                {
                    "Crafting", "Cooking", "Smithing", "Tailoring", "Art", "Research", "Intellectual"
                };

                if (workTypesThatUseWorkSpeed.Any(type =>
                    workGiver.workType.defName.Contains(type) ||
                    (!string.IsNullOrEmpty(workGiver.workType.label) && workGiver.workType.label.ToLower().Contains(type.ToLower()))))
                {
                    return true;
                }
            }

            // Check if it's a bill-based work giver (these typically use WorkSpeedGlobal)
            if (workGiver.defName.Contains("DoBill") ||
                (!string.IsNullOrEmpty(workGiver.label) && workGiver.label.ToLower().Contains("bill")))
                return true;

            return false;
        }

        /// <summary>
        /// Check if a specific job should be gated by survival tools based on settings
        /// </summary>
        public static bool ShouldGateJob(WorkGiverDef workGiver, SurvivalToolsSettings settings)
        {
            if (workGiver == null || settings == null) return false;

            // If the job doesn't use WorkSpeedGlobal, don't gate it
            if (!UsesWorkSpeedGlobal(workGiver)) return false;

            // Check the settings dictionary
            return settings.workSpeedGlobalJobGating.GetValueOrDefault(workGiver.defName, true);
        }
    }
}
