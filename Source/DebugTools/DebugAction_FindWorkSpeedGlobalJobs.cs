// RimWorld 1.6 / C# 7.3
// Source/DebugTools/DebugAction_FindWorkSpeedGlobalJobs.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using LudeonTK;

namespace SurvivalTools
{
    /// <summary>
    /// Debug helper to find all work givers and jobs that use WorkSpeedGlobal stat
    /// to help determine which ones should NOT be gated by survival tools.
    /// </summary>
    public static class DebugAction_FindWorkSpeedGlobalJobs
    {
#if DEBUG
        [DebugAction("Survival Tools", "Find WorkSpeedGlobal Jobs", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void FindWorkSpeedGlobalJobs()
        {
            var results = new StringBuilder();
            results.AppendLine("=== WorkSpeedGlobal Job Analysis ===\n");

            // Find all work givers that might use WorkSpeedGlobal
            var workGiversUsingWorkSpeed = new List<WorkGiverDef>();
            var workTypesUsingWorkSpeed = new HashSet<WorkTypeDef>();

            foreach (var workGiver in DefDatabase<WorkGiverDef>.AllDefs)
            {
                if (UsesWorkSpeedGlobal(workGiver))
                {
                    workGiversUsingWorkSpeed.Add(workGiver);
                    if (workGiver.workType != null)
                        workTypesUsingWorkSpeed.Add(workGiver.workType);
                }
            }

            // Categorize by work type
            results.AppendLine($"Found {workGiversUsingWorkSpeed.Count} work givers using WorkSpeedGlobal:\n");

            var categorizedJobs = new Dictionary<string, List<WorkGiverDef>>();

            foreach (var workGiver in workGiversUsingWorkSpeed.OrderBy(w => w.workType?.label ?? "Unknown"))
            {
                var category = workGiver.workType?.label ?? "Unknown";
                if (!categorizedJobs.ContainsKey(category))
                    categorizedJobs[category] = new List<WorkGiverDef>();
                categorizedJobs[category].Add(workGiver);
            }

            // Display results by category
            foreach (var category in categorizedJobs.Keys.OrderBy(k => k))
            {
                results.AppendLine($"=== {category.CapitalizeFirst()} ===");
                foreach (var workGiver in categorizedJobs[category])
                {
                    var recommendation = GetToolGatingRecommendation(workGiver);
                    results.AppendLine($"  â€¢ {workGiver.defName} - {workGiver.label}");
                    results.AppendLine($"    Recommendation: {recommendation}");
                    if (workGiver.GetModExtension<WorkGiverExtension>() != null)
                        results.AppendLine($"    Status: ALREADY HAS SURVIVAL TOOLS EXTENSION");
                    results.AppendLine();
                }
                results.AppendLine();
            }

            // Additional analysis
            results.AppendLine("=== Analysis Summary ===");
            results.AppendLine($"Total work givers using WorkSpeedGlobal: {workGiversUsingWorkSpeed.Count}");
            results.AppendLine($"Total work types affected: {workTypesUsingWorkSpeed.Count}");
            results.AppendLine();

            var shouldGate = workGiversUsingWorkSpeed.Where(w => GetToolGatingRecommendation(w).Contains("SHOULD")).Count();
            var shouldNotGate = workGiversUsingWorkSpeed.Where(w => GetToolGatingRecommendation(w).Contains("NOT")).Count();

            results.AppendLine($"Recommended for tool gating: {shouldGate}");
            results.AppendLine($"Recommended against tool gating: {shouldNotGate}");
            results.AppendLine($"Need manual review: {workGiversUsingWorkSpeed.Count - shouldGate - shouldNotGate}");

            // Create a file for easier review
            try
            {
                var desktopPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
                var filePath = System.IO.Path.Combine(desktopPath, "SurvivalTools_WorkSpeedGlobal_Analysis.txt");
                System.IO.File.WriteAllText(filePath, results.ToString());
                Messages.Message($"WorkSpeedGlobal analysis saved to: {filePath}", MessageTypeDefOf.PositiveEvent);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"Could not save WorkSpeedGlobal analysis file: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a work giver likely uses the WorkSpeedGlobal stat
        /// </summary>
        private static bool UsesWorkSpeedGlobal(WorkGiverDef workGiver)
        {
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
        /// Get a recommendation for whether this work giver should be gated by survival tools
        /// </summary>
        private static string GetToolGatingRecommendation(WorkGiverDef workGiver)
        {
            var name = workGiver.defName.ToLower();
            var label = !string.IsNullOrEmpty(workGiver.label) ? workGiver.label.ToLower() : "";

            // Jobs that should NOT be gated (basic survival activities)
            var noGateKeywords = new[]
            {
                "haul", "carry", "deliver", "refuel", "reload", "rearm", "strip", "wear", "equip",
                "rescue", "tend", "feed", "milk", "shear", "gather", "collect", "hunt", "slaughter",
                "clean", "extinguish", "repair", "maintain", "operate", "flick", "toggle"
            };

            if (noGateKeywords.Any(keyword => name.Contains(keyword) || label.Contains(keyword)))
                return "SHOULD NOT GATE - Basic survival activity";

            // Jobs that should be gated (crafting/production activities)
            var gateKeywords = new[]
            {
                "craft", "make", "cook", "smith", "tailor", "art", "sculpt", "research", "study",
                "bill", "produce", "manufacture", "create", "build", "construct"
            };

            if (gateKeywords.Any(keyword => name.Contains(keyword) || label.Contains(keyword)))
                return "SHOULD GATE - Production/crafting activity";

            // Check work type for additional context
            if (workGiver.workType != null)
            {
                var workType = workGiver.workType.defName.ToLower();

                if (workType.Contains("haul") || workType.Contains("basic") || workType.Contains("doctor"))
                    return "SHOULD NOT GATE - Essential work type";

                if (workType.Contains("craft") || workType.Contains("art") || workType.Contains("research") || workType.Contains("cook"))
                    return "SHOULD GATE - Production work type";
            }

            return "MANUAL REVIEW NEEDED - Unclear category";
        }
#endif
    }
}
