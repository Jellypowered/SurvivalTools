// RimWorld 1.6 / C# 7.3
// Source/Helpers/PawnToolValidator.cs

// Legacy Code: KEEP, but evaluate merging into a better file. 
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools.Helpers
{
    /// <summary>
    /// Centralized pawn capability and policy validation for tool usage.
    /// Contains methods that were scattered throughout the codebase to validate
    /// whether pawns can use tools, acquire tools, or have tools assigned.
    /// 
    /// 🔒 Note:
    /// - Mechanoids are excluded here (comprehensive check via IsMechanoid/FleshType/inheritance).
    ///   They have built-in "tools" and should never interact with SurvivalTools.
    ///   This prevents edge cases with Biotech mechs or modded mechanoids.
    /// - If a future mod adds humanoid mechanoids *intended* to use real tools,
    ///   add a settings toggle here to re-enable them.
    /// </summary>
    public static class PawnToolValidator
    {
        /// <summary>
        /// Comprehensive mechanoid detection that catches:
        /// - Standard IsMechanoid check
        /// - FleshType mechanoid check (for races that inherit from mechanoid bases)
        /// - Race def inheritance check (for modded mechanoids)
        /// </summary>
        public static bool IsMechanoidOrInherited(Pawn pawn)
        {
            if (pawn?.RaceProps == null) return false;

            // Check 1: Standard IsMechanoid property (catches most cases)
            if (pawn.RaceProps.IsMechanoid) return true;

            // Check 2: FleshType check (catches races that inherit from mechanoid ThingDefs)
            // RimWorld's FleshTypeDefOf.Mechanoid is the canonical mechanoid flesh type
            try
            {
                if (pawn.RaceProps.FleshType?.defName == "Mechanoid") return true;
            }
            catch { /* FleshType might not be available in all contexts */ }

            // Check 3: Check if the race def inherits from a mechanoid base
            // Modded mechanoids often have defNames containing "Mech" or inherit from base mechanoid ThingDefs
            try
            {
                var raceDef = pawn.def;
                if (raceDef?.defName != null)
                {
                    var defName = raceDef.defName.ToLowerInvariant();
                    // Common mechanoid naming patterns in mods
                    if (defName.Contains("mechanoid") || defName.StartsWith("mech_") || defName.EndsWith("_mech"))
                        return true;
                }

                // Check parent race for mechanoid properties
                if (raceDef?.race != null && raceDef.race != pawn.RaceProps)
                {
                    // Recursive check on parent race properties if different
                    // (This catches inheritance that doesn't properly set IsMechanoid)
                    try
                    {
                        if (raceDef.race.IsMechanoid) return true;
                        if (raceDef.race.FleshType?.defName == "Mechanoid") return true;
                    }
                    catch { /* Defensive - some mods might have unusual race hierarchies */ }
                }
            }
            catch { /* Defensive - don't crash on modded edge cases */ }

            return false;
        }

        /// <summary>
        /// Core check if a pawn can use survival tools.
        /// This method is used extensively throughout the mod.
        /// </summary>
        public static bool CanUseSurvivalTools(Pawn pawn)
        {
            return pawn != null &&
                   pawn.RaceProps?.Humanlike == true &&
                   // Exclude animals entirely as a safeguard regardless of faction
                   pawn.RaceProps?.Animal != true &&
                   !IsMechanoidOrInherited(pawn) &&    // 🚫 Block all mechs (comprehensive check)
                   !pawn.Dead &&
                   !pawn.InMentalState &&
                   pawn.Faction?.IsPlayer == true;
        }

        /// <summary>
        /// Determines if a tool meets the policy requirements for acquisition.
        /// Used in tool acquisition logic throughout the AI system.
        /// </summary>
        public static bool ToolIsAcquirableByPolicy(Pawn pawn, Thing tool)
        {
            if (pawn?.Faction != Faction.OfPlayer || tool == null) return false;

            // Basic checks
            if (tool.IsForbidden(pawn) || !pawn.CanReach(tool, PathEndMode.ClosestTouch, Danger.Deadly))
                return false;

            // Only survival tools or tool-stuff are considered valid
            return ToolClassification.IsSurvivalTool(tool) || tool.def.IsToolStuff();
        }

        /// <summary>
        /// Check if a pawn is allowed to automatically drop a tool after completing a job.
        /// Used in auto-optimization and job completion logic.
        /// </summary>
        public static bool AllowedToAutomaticallyDrop(Pawn pawn, Thing toolThing)
        {
            if (pawn?.Faction != Faction.OfPlayer || toolThing == null) return false;

            // Never drop forced items
            if (IsToolForced(pawn, toolThing)) return false;

            // Phase 11.11: Manual assignment system removed - automatic system handles all tool selection
            // Default: allow dropping if not forced
            return true;
        }

        /// <summary>
        /// Check if a tool is forced for a specific pawn (via forced items or assignment).
        /// </summary>
        public static bool IsToolForced(Pawn pawn, Thing toolThing)
        {
            if (pawn?.Faction != Faction.OfPlayer || toolThing == null) return false;

            var tracker = pawn.TryGetComp<Pawn_ForcedToolTracker>();
            return tracker?.forcedHandler?.IsForced(toolThing) == true;
        }

        /// <summary>
        /// Basic validation that a pawn can reasonably use a survival tool.
        /// Includes mental state, capability, faction, and mechanoid checks.
        /// 
        /// 🔒 Difference from CanUseSurvivalTools:
        /// - Looser: doesn’t require player faction.
        /// - Still excludes mechs, downed, dead, and mental pawns.
        /// - Useful for utility functions where faction doesn’t matter.
        /// </summary>
        public static bool CanUseTools(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed) return false;
            if (pawn.InMentalState || pawn.IsPrisoner) return false;
            if (IsMechanoidOrInherited(pawn)) return false; // 🚫 Block all mechs (comprehensive check)
            // Exclude animals as a safeguard (consistent with CanUseSurvivalTools)
            if (pawn.RaceProps?.Animal == true) return false;
            return pawn.RaceProps?.Humanlike == true;
        }

        /// <summary>
        /// Check if a pawn would benefit from using the specified tool.
        /// Utility for relevance checks (used in auto-tool pickup logic).
        /// </summary>
        public static bool WouldBenefitFromTool(Pawn pawn, Thing tool)
        {
            if (!CanUseSurvivalTools(pawn) || tool == null) return false;

            // If it's a survival tool wrapper, always potentially beneficial
            if (ToolClassification.IsSurvivalTool(tool)) return true;

            // If it's tool-stuff, check if it has relevant properties
            if (tool.def.IsToolStuff())
            {
                var props = tool.def.GetModExtension<SurvivalToolProperties>();
                return props?.baseWorkStatFactors?.Count > 0;
            }

            return false;
        }

        /// <summary>
        /// Phase 12: Check if a tool satisfies a required stat check considering charge state.
        /// Empty powered tools don't satisfy required checks in HC/NM if the stat is power-dependent.
        /// </summary>
        public static bool ToolSatisfiesStatConsideringCharge(Thing tool, StatDef stat)
        {
            if (tool == null || stat == null)
                return false;

            // Check if this is a powered tool
            // DISABLED: Battery system turned off
            /*
            var powerComp = tool.TryGetComp<CompPowerTool>();
            if (powerComp != null)
            {
                // If tool is out of charge and this stat is power-dependent, it doesn't satisfy the requirement
                if (!powerComp.HasCharge && powerComp.IsPoweredStat(stat))
                {
                    return false;
                }
            }
            */

            // Tool has charge, isn't powered, or stat isn't power-dependent - it satisfies
            return true;
        }
    }
}

