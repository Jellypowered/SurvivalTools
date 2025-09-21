// RimWorld 1.6 / C# 7.3
// Source/Helpers/ToolClassification.cs

// Evaluate if this is needed or useful, otherwise remove it. Is it used by the refactor? 
// Should it be integrated? 
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Helpers
{
    /// <summary>
    /// Helper methods for classifying and identifying tool types, purposes, and capabilities.
    /// Centralized from ToolUtility, VirtualTool, and various detection logic.
    /// </summary>
    public static class ToolClassification
    {
        #region Tool Type Detection

        /// <summary>
        /// Determine the tool kind based on the thing's properties, name, and stats.
        /// </summary>
        public static STToolKind ClassifyToolKind(Thing thing)
        {
            if (thing?.def == null) return STToolKind.None;

            // Direct tool classification via mod extension
            if (thing.def.IsSurvivalTool())
            {
                var props = thing.def.GetModExtension<SurvivalToolProperties>();
                if (props?.baseWorkStatFactors?.Any() == true)
                    return ClassifyByStats(props.baseWorkStatFactors.Select(f => f.stat));
            }

            // Tool-stuff classification
            if (thing.def.IsToolStuff())
            {
                var stuffProps = thing.def.GetModExtension<SurvivalToolProperties>();
                if (stuffProps?.baseWorkStatFactors?.Any() == true)
                    return ClassifyByStats(stuffProps.baseWorkStatFactors.Select(f => f.stat));
            }

            // Fallback to name-based detection
            return ClassifyByName(thing.def);
        }

        /// <summary>
        /// Classify tool kind based on the stats it improves.
        /// </summary>
        public static STToolKind ClassifyByStats(IEnumerable<StatDef> stats)
        {
            if (stats == null) return STToolKind.None;

            var statSet = new HashSet<StatDef>(stats.Where(s => s != null));

            // Priority order matters - more specific matches first
            if (statSet.Contains(ST_StatDefOf.DiggingSpeed)) return STToolKind.Pick;
            if (statSet.Contains(ST_StatDefOf.TreeFellingSpeed)) return STToolKind.Axe;
            if (statSet.Contains(ST_StatDefOf.PlantHarvestingSpeed)) return STToolKind.Sickle;
            if (statSet.Contains(ST_StatDefOf.SowingSpeed)) return STToolKind.Hoe;
            if (statSet.Contains(StatDefOf.ConstructionSpeed)) return STToolKind.Hammer;
            if (statSet.Contains(ST_StatDefOf.MaintenanceSpeed) || statSet.Contains(ST_StatDefOf.DeconstructionSpeed)) return STToolKind.Wrench;
            if (statSet.Contains(ST_StatDefOf.CleaningSpeed)) return STToolKind.Cleaning;
            if (statSet.Contains(ST_StatDefOf.ResearchSpeed)) return STToolKind.Research;
            if (statSet.Contains(ST_StatDefOf.MedicalOperationSpeed) || statSet.Contains(ST_StatDefOf.MedicalSurgerySuccessChance)) return STToolKind.Medical;
            if (statSet.Contains(ST_StatDefOf.ButcheryFleshSpeed) || statSet.Contains(ST_StatDefOf.ButcheryFleshEfficiency)) return STToolKind.Knife;

            return STToolKind.None;
        }

        /// <summary>
        /// Classify tool kind based on defName and label patterns.
        /// </summary>
        public static STToolKind ClassifyByName(ThingDef def)
        {
            if (def?.defName == null) return STToolKind.None;

            var name = def.defName.ToLowerInvariant();
            var label = def.label?.ToLowerInvariant() ?? "";

            // Pick/Mining tools
            if (ContainsAny(name, "pick", "pickaxe", "mattock") || ContainsAny(label, "pick", "pickaxe", "mattock"))
                return STToolKind.Pick;

            // Axe/Tree felling
            if (ContainsAny(name, "axe", "hatchet", "tomahawk") || ContainsAny(label, "axe", "hatchet", "tomahawk"))
                return STToolKind.Axe;

            // Sickle/Harvesting
            if (ContainsAny(name, "sickle", "scythe", "reaper") || ContainsAny(label, "sickle", "scythe", "reaper"))
                return STToolKind.Sickle;

            // Hoe/Farming
            if (ContainsAny(name, "hoe", "tiller", "cultivator") || ContainsAny(label, "hoe", "tiller", "cultivator"))
                return STToolKind.Hoe;

            // Hammer/Construction
            if (ContainsAny(name, "hammer", "mallet", "sledge") || ContainsAny(label, "hammer", "mallet", "sledge"))
                return STToolKind.Hammer;

            // Wrench/Maintenance
            if (ContainsAny(name, "wrench", "spanner", "tool", "kit") || ContainsAny(label, "wrench", "spanner", "repair"))
                return STToolKind.Wrench;

            // Medical tools
            if (ContainsAny(name, "scalpel", "medical", "surgery") || ContainsAny(label, "scalpel", "medical", "surgery"))
                return STToolKind.Medical;

            // Cleaning tools  
            if (ContainsAny(name, "broom", "mop", "brush") || ContainsAny(label, "broom", "mop", "brush"))
                return STToolKind.Cleaning;

            // Knife/Butchery
            if (ContainsAny(name, "knife", "cleaver", "butcher") || ContainsAny(label, "knife", "cleaver", "butcher"))
                return STToolKind.Knife;

            return STToolKind.None;
        }

        #endregion

        #region Tool Capability Checks

        /// <summary>
        /// Check if a thing can function as a survival tool (real tool or tool-stuff).
        /// </summary>
        public static bool IsSurvivalTool(Thing thing)
        {
            if (thing?.def == null) return false;

            return thing.def.IsSurvivalTool() ||
                   thing.def.IsToolStuff() ||
                   ClassifyByName(thing.def) != STToolKind.None;
        }

        /// <summary>
        /// Check if a tool definition looks like tool-stuff based on name patterns.
        /// </summary>
        public static bool LooksLikeToolStuff(ThingDef def)
        {
            if (def?.defName == null) return false;

            var name = def.defName.ToLowerInvariant();
            var label = def.label?.ToLowerInvariant() ?? "";

            // Common tool-stuff patterns
            return ContainsAny(name, "steel", "iron", "metal", "wood", "stone", "plasteel") &&
                   ContainsAny(name, "tool", "implement", "head", "blade", "handle");
        }

        /// <summary>
        /// Check if a tool is considered a "pacifist tool" (non-weapon).
        /// </summary>
        public static bool IsPacifistTool(ThingDef def)
        {
            if (def?.defName == null) return false;

            var kind = ClassifyByName(def);

            // These tool types are clearly non-violent
            return kind == STToolKind.Hoe ||
                   kind == STToolKind.Sickle ||
                   kind == STToolKind.Research ||
                   kind == STToolKind.Medical ||
                   kind == STToolKind.Cleaning;
        }

        /// <summary>
        /// Get the expected stats that a tool kind should improve.
        /// </summary>
        public static IEnumerable<StatDef> GetExpectedStatsForKind(STToolKind kind)
        {
            switch (kind)
            {
                case STToolKind.Pick:
                    return new[] { ST_StatDefOf.DiggingSpeed };
                case STToolKind.Axe:
                    return new[] { ST_StatDefOf.TreeFellingSpeed };
                case STToolKind.Hammer:
                    return new[] { StatDefOf.ConstructionSpeed };
                case STToolKind.Wrench:
                    return new[] { ST_StatDefOf.MaintenanceSpeed, ST_StatDefOf.DeconstructionSpeed };
                case STToolKind.Hoe:
                    return new[] { ST_StatDefOf.SowingSpeed };
                case STToolKind.Sickle:
                    return new[] { ST_StatDefOf.PlantHarvestingSpeed };
                case STToolKind.Cleaning:
                    return new[] { ST_StatDefOf.CleaningSpeed };
                case STToolKind.Research:
                    return new[] { ST_StatDefOf.ResearchSpeed };
                case STToolKind.Medical:
                    return new[] { ST_StatDefOf.MedicalOperationSpeed, ST_StatDefOf.MedicalSurgerySuccessChance };
                case STToolKind.Knife:
                    return new[] { ST_StatDefOf.ButcheryFleshSpeed, ST_StatDefOf.ButcheryFleshEfficiency };
                case STToolKind.Saw:
                    return new[] { ST_StatDefOf.DeconstructionSpeed };
                default:
                    return Enumerable.Empty<StatDef>();
            }
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Check if a string contains any of the specified substrings.
        /// </summary>
        private static bool ContainsAny(string text, params string[] substrings)
        {
            if (string.IsNullOrEmpty(text) || substrings == null) return false;
            return substrings.Any(s => !string.IsNullOrEmpty(s) && text.Contains(s));
        }

        #endregion
    }
}
