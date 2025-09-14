// RimWorld 1.6 / C# 7.3
// Source/ToolResolver.cs
using System;
using System.Linq;
using System.Collections.Generic;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools
{
    /// <summary>
    /// A system for automatically adding relevant stats to tools that don't already have them.
    /// This ensures compatibility with modded tools by intelligently detecting tool types
    /// and applying appropriate stats based on their names and tech levels.
    /// </summary>
    public static class ToolResolver
    {
        #region Tool Detection Rules

        /// <summary>
        /// Defines tool type detection rules with name patterns and corresponding stats
        /// </summary>
        private static readonly Dictionary<STToolKind, ToolDetectionRule> DetectionRules = new Dictionary<STToolKind, ToolDetectionRule>
        {
            {
                STToolKind.Axe,
                new ToolDetectionRule
                {
                    NamePatterns = new[] { "axe", "hatchet" },
                    ExcludePatterns = new[] { "pickaxe", "pick axe", "mining", "quarry" },
                    RequiredStats = new[] { ST_StatDefOf.TreeFellingSpeed },
                    Description = "Tree felling tools"
                }
            },
            {
                STToolKind.Pick,
                new ToolDetectionRule
                {
                    NamePatterns = new[] { "pickaxe", "pick", "mattock" },
                    ExcludePatterns = new[] { "toothpick", "picket" },
                    RequiredStats = new[] { ST_StatDefOf.DiggingSpeed, ST_StatDefOf.MiningYieldDigging },
                    Description = "Mining and digging tools"
                }
            },
            {
                STToolKind.Knife,
                new ToolDetectionRule
                {
                    NamePatterns = new[] { "knife", "blade", "dagger" },
                    ExcludePatterns = new[] { "butterknife" },
                    RequiredStats = new[] { ST_StatDefOf.ButcheryFleshSpeed, ST_StatDefOf.ButcheryFleshEfficiency },
                    Description = "Cutting and butchering tools"
                }
            },
            {
                STToolKind.Sickle,
                new ToolDetectionRule
                {
                    NamePatterns = new[] { "sickle", "scythe" },
                    ExcludePatterns = new string[0],
                    RequiredStats = new[] { ST_StatDefOf.PlantHarvestingSpeed },
                    // Sickles also get reduced butchering capability (0.5x) since they're blades
                    SecondaryStats = new Dictionary<StatDef, float>
                    {
                        { ST_StatDefOf.ButcheryFleshSpeed, 0.5f },
                        { ST_StatDefOf.ButcheryFleshEfficiency, 0.5f }
                    },
                    Description = "Plant harvesting tools with limited butchering capability"
                }
            },
            {
                STToolKind.Hammer,
                new ToolDetectionRule
                {
                    NamePatterns = new[] { "hammer", "mallet", "maul" },
                    ExcludePatterns = new[] { "sledgehammer", "persona" }, // Sledgehammers might be mining tools
                    RequiredStats = new[] { ST_StatDefOf.MaintenanceSpeed },
                    Description = "Construction and maintenance tools"
                }
            },
            {
                STToolKind.Wrench,
                new ToolDetectionRule
                {
                    NamePatterns = new[] { "wrench", "spanner", "screwdriver", "prybar", "crowbar", "lever" },
                    ExcludePatterns = new string[0],
                    RequiredStats = new[] { ST_StatDefOf.DeconstructionSpeed, ST_StatDefOf.MaintenanceSpeed },
                    Description = "Mechanical repair and deconstruction tools"
                }
            },
            {
                STToolKind.Hoe,
                new ToolDetectionRule
                {
                    NamePatterns = new[] { "hoe", "cultivator", "tiller" },
                    ExcludePatterns = new[] { "armor" },
                    RequiredStats = new[] { ST_StatDefOf.SowingSpeed },
                    Description = "Soil preparation and planting tools"
                }
            },
            {
                STToolKind.Saw,
                new ToolDetectionRule
                {
                    NamePatterns = new[] { "saw", "chainsaw" },
                    ExcludePatterns = new[] { "seesaw" },
                    RequiredStats = new[] { ST_StatDefOf.TreeFellingSpeed },
                    Description = "Wood cutting tools"
                }
            },
            {
                STToolKind.Cleaning,
                new ToolDetectionRule
                {
                    NamePatterns = new[] { "broom", "mop", "rags", "cloth", "towel", "sponge" },
                    ExcludePatterns = new[] { "broomcorn", "broomstick", "tablecloth", "bedcloth" },
                    RequiredStats = new[] { ST_StatDefOf.CleaningSpeed },
                    Description = "Cleaning and maintenance tools"
                }
            },
            {
                STToolKind.Research,
                new ToolDetectionRule
                {
                    NamePatterns = new[] { "microscope", "telescope", "sextant", "calculator", "computer", "instrument", "analyzer", "scanner", "spectrometer", "chromatograph", "oscilloscope", "multimeter", "datapad", "tablet", "laptop" },
                    ExcludePatterns = new[] { "musical", "music", "guitar", "piano", "drum", "violin", "flute", "horn" },
                    RequiredStats = new[] { ST_StatDefOf.ResearchSpeed },
                    Description = "Research and analytical instruments"
                }
            },
            {
                STToolKind.Medical,
                new ToolDetectionRule
                {
                    NamePatterns = new[] { "scalpel", "forceps", "stethoscope", "syringe", "needle", "suture", "bandage", "gauze", "medkit", "surgical", "medical" },
                    ExcludePatterns = new[] { "herbal", "tribal", "gun", "skilltrainer", "needle gun", "needle launcher", "launcher" },
                    RequiredStats = new[] { ST_StatDefOf.MedicalOperationSpeed, ST_StatDefOf.MedicalSurgerySuccessChance },
                    Description = "Medical and surgical instruments"
                }
            }
        };

        #endregion

        #region Main Resolution Method

        /// <summary>
        /// Automatically resolves and enhances all tools in the game with appropriate stats.
        /// This method should be called during static constructor initialization.
        /// </summary>
        public static void ResolveAllTools()
        {
            if (IsDebugLoggingEnabled)
            {
                LogDebug("[SurvivalTools] Starting automatic tool resolution...", "ToolResolutionStart");
                LogModStatsVerification();
            }

            int totalEnhanced = 0;
            var enhancementLog = new List<string>();

            // Process all ThingDefs for tool enhancement (weapons and items)
            foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefs.Where(t => t != null &&
                (t.IsWeapon || (t.category == ThingCategory.Item && !t.IsCorpse))))
            {
                if (TryEnhanceTool(thingDef, out string logEntry))
                {
                    totalEnhanced++;
                    enhancementLog.Add(logEntry);
                }
            }

            // Summary logging
            if (IsDebugLoggingEnabled)
            {
                if (totalEnhanced > 0)
                {
                    LogDebug($"[SurvivalTools] Tool resolution complete: Enhanced {totalEnhanced} tools", "ToolResolutionComplete");
                    foreach (string entry in enhancementLog)
                    {
                        LogDebug($"  - {entry}", $"ToolResolutionEntry_{entry.GetHashCode()}");
                    }
                }
                else
                {
                    // Only log if author attention is needed
                    if (totalEnhanced == 0)
                    {
                        Log.Warning("[SurvivalTools] Tool resolution complete: No new tools found to enhance");
                    }
                }
            }
        }

        #endregion

        #region Tool Enhancement Logic

        /// <summary>
        /// Attempts to enhance a weapon ThingDef with appropriate tool stats
        /// </summary>
        private static bool TryEnhanceTool(ThingDef thingDef, out string logEntry)
        {
            logEntry = null;
            if (thingDef?.label == null) return false;

            string label = thingDef.label.ToLower();
            string modSource = GetModSource(thingDef);

            // Check each detection rule
            foreach (var kvp in DetectionRules)
            {
                var rule = kvp.Value;

                // Check if label matches any pattern
                bool matches = rule.NamePatterns.Any(pattern => label.Contains(pattern));
                if (!matches) continue;

                // Check if it should be excluded
                bool excluded = rule.ExcludePatterns.Any(pattern => label.Contains(pattern));
                if (excluded)
                {
                    if (IsDebugLoggingEnabled)
                        logEntry = $"Excluded {rule.Description.ToLower()}: {thingDef.defName} ({thingDef.label}) from {modSource}";
                    return false;
                }

                // Check if it already has the required stats (in statBases or mod extensions)
                bool needsEnhancement = rule.RequiredStats.Any(stat => !HasExistingStat(thingDef, stat));

                if (!needsEnhancement)
                {
                    if (IsDebugLoggingEnabled)
                        logEntry = $"Already enhanced {rule.Description.ToLower()}: {thingDef.defName} ({thingDef.label}) from {modSource}";
                    return false;
                }

                // Skip if this is already a SurvivalTools item (don't modify our own tools)
                if (IsSurvivalToolsItem(thingDef))
                {
                    if (IsDebugLoggingEnabled)
                        logEntry = $"Skipped SurvivalTools item: {thingDef.defName} ({thingDef.label}) from {modSource}";
                    return false;
                }

                // Enhance the tool
                EnhanceTool(thingDef, rule, kvp.Key);
                logEntry = $"Enhanced {rule.Description.ToLower()}: {thingDef.defName} ({thingDef.label}) from {modSource}";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Enhances a ThingDef with tool stats based on the detection rule
        /// </summary>
        private static void EnhanceTool(ThingDef thingDef, ToolDetectionRule rule, STToolKind toolKind)
        {
            float multiplier = GetStatMultiplierForTechLevel(thingDef.techLevel);

            // Initialize statBases if needed
            if (thingDef.statBases == null)
                thingDef.statBases = new List<StatModifier>();

            // Add primary stats
            foreach (StatDef requiredStat in rule.RequiredStats)
            {
                // Only add if not already present
                if (!thingDef.statBases.Any(sb => sb.stat == requiredStat))
                {
                    thingDef.statBases.Add(new StatModifier
                    {
                        stat = requiredStat,
                        value = multiplier
                    });
                }
            }

            // Add secondary stats (like reduced butchering for sickles)
            if (rule.SecondaryStats != null)
            {
                foreach (var kvp in rule.SecondaryStats)
                {
                    if (!thingDef.statBases.Any(sb => sb.stat == kvp.Key))
                    {
                        thingDef.statBases.Add(new StatModifier
                        {
                            stat = kvp.Key,
                            value = kvp.Value * multiplier // Apply tech level scaling to secondary stats too
                        });
                    }
                }
            }

            // Add SurvivalToolProperties mod extension if not present
            EnsureSurvivalToolExtension(thingDef, toolKind);
        }

        /// <summary>
        /// Ensures a ThingDef has the SurvivalToolProperties mod extension
        /// </summary>
        private static void EnsureSurvivalToolExtension(ThingDef thingDef, STToolKind toolKind)
        {
            // Check if it already has the extension
            if (thingDef.HasModExtension<SurvivalToolProperties>()) return;

            // Initialize modExtensions if needed
            if (thingDef.modExtensions == null)
                thingDef.modExtensions = new List<DefModExtension>();

            // Add basic SurvivalToolProperties extension
            var toolProps = new SurvivalToolProperties();

            // Set tool assignment tags based on tool kind
            if (toolKind != STToolKind.None)
            {
                toolProps.defaultSurvivalToolAssignmentTags = new List<string> { toolKind.ToString().ToLower() };
            }

            thingDef.modExtensions.Add(toolProps);
        }

        #endregion

        #region Tech Level Multipliers

        /// <summary>
        /// Gets the stat multiplier based on tech level
        /// </summary>
        private static float GetStatMultiplierForTechLevel(TechLevel techLevel)
        {
            switch (techLevel)
            {
                case TechLevel.Neolithic:
                    return 0.75f;
                case TechLevel.Medieval:
                    return 0.85f;
                case TechLevel.Industrial:
                    return 1.0f;
                case TechLevel.Spacer:
                    return 1.15f;
                case TechLevel.Ultra:
                    return 1.3f;
                default:
                    return 0.85f; // Default for unrecognized tech levels
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Checks if a ThingDef already has a specific stat (either in statBases or mod extensions)
        /// </summary>
        private static bool HasExistingStat(ThingDef thingDef, StatDef stat)
        {
            // Check statBases
            if (thingDef.statBases?.Any(sb => sb.stat == stat) == true)
                return true;

            // Check SurvivalToolProperties mod extension
            var toolProps = thingDef.GetModExtension<SurvivalToolProperties>();
            if (toolProps?.baseWorkStatFactors?.Any(modifier => modifier.stat == stat) == true)
                return true;

            return false;
        }

        /// <summary>
        /// Checks if a ThingDef is from SurvivalTools (our own items)
        /// </summary>
        private static bool IsSurvivalToolsItem(ThingDef thingDef)
        {
            // Check if defName starts with SurvivalTools_
            if (thingDef.defName?.StartsWith("SurvivalTools_") == true)
                return true;

            // Check if it has SurvivalToolProperties extension (strong indicator it's ours)
            if (thingDef.HasModExtension<SurvivalToolProperties>())
                return true;

            return false;
        }

        /// <summary>
        /// Logs verification of all mod stats to ensure they're accounted for in detection rules
        /// </summary>
        private static void LogModStatsVerification()
        {
            var allModStats = new[]
            {
                //ST_StatDefOf.SurvivalToolCarryCapacity,
                ST_StatDefOf.DiggingSpeed,
                ST_StatDefOf.MiningYieldDigging,
                ST_StatDefOf.PlantHarvestingSpeed,
                ST_StatDefOf.SowingSpeed,
                ST_StatDefOf.TreeFellingSpeed,
                ST_StatDefOf.MaintenanceSpeed,
                ST_StatDefOf.DeconstructionSpeed,
                ST_StatDefOf.ResearchSpeed,
                ST_StatDefOf.CleaningSpeed,
                ST_StatDefOf.MedicalOperationSpeed,
                ST_StatDefOf.MedicalSurgerySuccessChance,
                ST_StatDefOf.ButcheryFleshSpeed,
                ST_StatDefOf.ButcheryFleshEfficiency
                //ST_StatDefOf.ToolEstimatedLifespan,
                //ST_StatDefOf.ToolEffectivenessFactor
            };

            // Get all stats used in detection rules
            var usedStats = new HashSet<StatDef>();
            foreach (var rule in DetectionRules.Values)
            {
                foreach (var stat in rule.RequiredStats)
                    usedStats.Add(stat);
                if (rule.SecondaryStats != null)
                {
                    foreach (var kvp in rule.SecondaryStats)
                        usedStats.Add(kvp.Key);
                }
            }

            // Check for stats that aren't used in any detection rule
            var unusedStats = allModStats.Where(stat => stat != null && !usedStats.Contains(stat)).ToList();

            if (IsDebugLoggingEnabled)
            {
                LogDebug($"[SurvivalTools] Stat coverage verification: {usedStats.Count}/{allModStats.Where(s => s != null).Count()} stats covered in detection rules", "StatCoverageVerification");
                if (unusedStats.Any())
                {
                    LogDebug($"[SurvivalTools] The following stats are not covered by any detection rule:", "StatsNotCoveredWarning");
                    foreach (var stat in unusedStats)
                    {
                        LogDebug($"  - {stat.defName} ({stat.label})", $"StatNotCovered_{stat.defName}");
                    }
                }
            }
        }

        /// <summary>
        /// Gets the mod source name for a ThingDef
        /// </summary>
        private static string GetModSource(ThingDef thingDef)
        {
            try
            {
                // Try to get the mod content pack from the def
                ModContentPack modContentPack = thingDef.modContentPack;
                if (modContentPack != null)
                {
                    // Return a clean mod name
                    string modName = modContentPack.Name;
                    if (string.IsNullOrEmpty(modName))
                        modName = modContentPack.PackageId;

                    return string.IsNullOrEmpty(modName) ? "Unknown Mod" : modName;
                }

                // Fallback - try to determine from package ID if available
                string packageId = thingDef.modContentPack?.PackageId;
                if (!string.IsNullOrEmpty(packageId))
                {
                    if (packageId.ToLower().Contains("core") || packageId.Equals("ludeon.rimworld"))
                        return "Core";
                    return packageId;
                }

                return "Unknown Mod";
            }
            catch
            {
                return "Unknown Mod";
            }
        }

        #endregion

        #region Helper Classes

        /// <summary>
        /// Defines detection rules for a specific tool type
        /// </summary>
        private class ToolDetectionRule
        {
            public string[] NamePatterns { get; set; }
            public string[] ExcludePatterns { get; set; }
            public StatDef[] RequiredStats { get; set; }
            public Dictionary<StatDef, float> SecondaryStats { get; set; }
            public string Description { get; set; }
        }

        #endregion
    }
}
