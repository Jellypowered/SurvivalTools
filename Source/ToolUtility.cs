// RimWorld 1.6 / C# 7.3
// Source/ToolUtility.cs
using System;
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
        Wrench,
        Cleaning,
        Research,
        Medical
    }

    /// <summary>
    /// Utility methods for survival tool identification, selection, and job-to-tool mapping.
    /// </summary>
    public static class ToolUtility
    {
        #region Kind <-> Stat mapping

        // Tool type to stat mappings (lazy initialized to avoid early DefOf access)
        private static StatDef[] _stats_Pick;
        private static StatDef[] _stats_Axe;
        private static StatDef[] _stats_Hammer;
        private static StatDef[] _stats_Wrench;
        private static StatDef[] _stats_Hoe;
        private static StatDef[] _stats_Sickle;
        private static StatDef[] _stats_Cleaning;
        private static StatDef[] _stats_Research;
        private static StatDef[] _stats_Medical;
        private static StatDef[] _stats_Knife;
        private static StatDef[] _stats_Saw;

        private static StatDef[] Stats_Pick => _stats_Pick ?? (_stats_Pick = new[] { ST_StatDefOf.DiggingSpeed });
        private static StatDef[] Stats_Axe => _stats_Axe ?? (_stats_Axe = new[] { ST_StatDefOf.TreeFellingSpeed });
        private static StatDef[] Stats_Hammer => _stats_Hammer ?? (_stats_Hammer = new[] { StatDefOf.ConstructionSpeed });
        private static StatDef[] Stats_Wrench => _stats_Wrench ?? (_stats_Wrench = new[] { ST_StatDefOf.MaintenanceSpeed, ST_StatDefOf.DeconstructionSpeed });
        private static StatDef[] Stats_Hoe => _stats_Hoe ?? (_stats_Hoe = new[] { ST_StatDefOf.SowingSpeed });
        private static StatDef[] Stats_Sickle => _stats_Sickle ?? (_stats_Sickle = new[] { ST_StatDefOf.PlantHarvestingSpeed });
        private static StatDef[] Stats_Cleaning => _stats_Cleaning ?? (_stats_Cleaning = new[] { ST_StatDefOf.CleaningSpeed });
        private static StatDef[] Stats_Research => _stats_Research ?? (_stats_Research = new[] { ST_StatDefOf.ResearchSpeed });
        private static StatDef[] Stats_Medical => _stats_Medical ?? (_stats_Medical = new[] { ST_StatDefOf.MedicalOperationSpeed, ST_StatDefOf.MedicalSurgerySuccessChance });
        private static StatDef[] Stats_Knife => _stats_Knife ?? (_stats_Knife = new[] { ST_StatDefOf.ButcheryFleshSpeed, ST_StatDefOf.ButcheryFleshEfficiency });
        private static StatDef[] Stats_Saw => _stats_Saw ?? (_stats_Saw = new[] { ST_StatDefOf.DeconstructionSpeed }); // optional alias for deconstruction

        private static IEnumerable<StatDef> StatsForKind(STToolKind kind)
        {
            switch (kind)
            {
                case STToolKind.Pick: return Stats_Pick;
                case STToolKind.Axe: return Stats_Axe;
                case STToolKind.Hammer: return Stats_Hammer;
                case STToolKind.Wrench: return Stats_Wrench;
                case STToolKind.Hoe: return Stats_Hoe;
                case STToolKind.Sickle: return Stats_Sickle;
                case STToolKind.Cleaning: return Stats_Cleaning;
                case STToolKind.Research: return Stats_Research;
                case STToolKind.Medical: return Stats_Medical;
                case STToolKind.Knife: return Stats_Knife;
                case STToolKind.Saw: return Stats_Saw;
                default: return Enumerable.Empty<StatDef>();
            }
        }

        private static STToolKind KindForStats(IEnumerable<StatDef> stats)
        {
            if (stats == null) return STToolKind.None;
            var set = new HashSet<StatDef>(stats.Where(s => s != null));

            if (set.Overlaps(Stats_Pick)) return STToolKind.Pick;
            if (set.Overlaps(Stats_Axe)) return STToolKind.Axe;
            if (set.Overlaps(Stats_Sickle)) return STToolKind.Sickle;
            if (set.Overlaps(Stats_Hoe)) return STToolKind.Hoe;
            if (set.Overlaps(Stats_Hammer)) return STToolKind.Hammer;
            if (set.Overlaps(Stats_Wrench)) return STToolKind.Wrench;
            if (set.Overlaps(Stats_Cleaning)) return STToolKind.Cleaning;
            if (set.Overlaps(Stats_Medical)) return STToolKind.Medical;
            if (set.Overlaps(Stats_Research)) return STToolKind.Research;
            if (set.Overlaps(Stats_Knife)) return STToolKind.Knife;
            if (set.Overlaps(Stats_Saw)) return STToolKind.Saw;

            return STToolKind.None;
        }

        /// <summary>
        /// Public wrapper to determine the STToolKind implied by a set of StatDefs.
        /// Used by other modules that only have stat lists available.
        /// </summary>
        public static STToolKind ToolKindForStats(IEnumerable<StatDef> stats)
        {
            return KindForStats(stats);
        }

        private static bool Overlaps(this HashSet<StatDef> a, IEnumerable<StatDef> b) =>
            b != null && b.Any(a.Contains);

        #endregion

        #region Tool identification

        /// <summary>
        /// Determines whether a Thing should be treated as a virtual survival tool.
        /// Virtual tools are non-SurvivalTool Things (usually stuff/material stacks) that
        /// provide survival tool work stats via SurvivalToolProperties or statBases.
        /// Unlike real SurvivalTool instances they are transient wrappers at runtime.
        /// </summary>
        public static bool IsVirtualTool(Thing t)
        {
            if (t == null) return false;
            // Already an actual SurvivalTool instance (includes VirtualSurvivalTool subclass) => not virtual by this predicate.
            if (t is SurvivalTool) return t is VirtualTool; // Only treat the dedicated wrapper subclass as virtual.
            // Delegate to tightened textile-only factory eligibility
            // Use factory predicate without retaining wrapper (avoids transient allocation in classification paths)
            return VirtualTool.FromThing(t) != null;
        }

        /// <summary>
        /// Attempt to wrap a qualifying Thing as a VirtualTool; returns null if not virtual.
        /// Centralized to avoid scattered FromThing calls and repeated predicate logic.
        /// </summary>
        public static VirtualTool TryWrapVirtual(Thing thing)
        {
            if (thing == null) return null;
            if (thing is VirtualTool vt) return vt;
            if (!IsVirtualTool(thing)) return null;
            return VirtualTool.FromThing(thing);
        }

        /// <summary>
        /// Determine tool kind by defName/label and, for tool-stuff, by its SurvivalToolProperties stats.
        /// </summary>
        public static STToolKind ToolKindOf(Thing t)
        {
            if (t == null || t.def == null) return STToolKind.None;

            // Tool-stuff: infer kind from provided stats
            if (t.def.IsToolStuff())
            {
                var props = t.def.GetModExtension<SurvivalToolProperties>();
                if (props?.baseWorkStatFactors != null)
                {
                    var stats = props.baseWorkStatFactors.Where(m => m?.stat != null).Select(m => m.stat).ToList();
                    var kindFromStats = KindForStats(stats);
                    if (kindFromStats != STToolKind.None) return kindFromStats;
                }
            }

            string dn = t.def.defName?.ToLowerInvariant() ?? string.Empty;
            string label = t.def.label?.ToLowerInvariant() ?? string.Empty;
            string s = (dn + " " + label);

            // Specifics before generics (avoid "pickaxe" -> axe)
            if (s.Contains("pickaxe") || s.Contains("pick")) return STToolKind.Pick;
            if (s.Contains("sickle")) return STToolKind.Sickle;
            if (s.Contains("hammer") || s.Contains("mallet")) return STToolKind.Hammer;
            if (s.Contains("wrench") || s.Contains("prybar") || s.Contains("primitivelever")) return STToolKind.Wrench;
            if (s.Contains("hatchet")) return STToolKind.Axe;
            if (s.Contains("axe")) return STToolKind.Axe;
            if (s.Contains("hoe")) return STToolKind.Hoe;
            if (s.Contains("saw")) return STToolKind.Saw;
            if (s.Contains("knife")) return STToolKind.Knife;

            // Research
            if (s.Contains("microscope") || s.Contains("telescope") || s.Contains("sextant") ||
                s.Contains("calculator") || s.Contains("computer") || s.Contains("analyzer") ||
                s.Contains("scanner") || s.Contains("spectrometer") || s.Contains("datapad") ||
                s.Contains("tablet") || s.Contains("laptop")) return STToolKind.Research;

            // Medical
            if (s.Contains("scalpel") || s.Contains("forceps") || s.Contains("stethoscope") ||
                s.Contains("syringe") || s.Contains("surgical") || s.Contains("medkit")) return STToolKind.Medical;

            // Cleaning via properties on any ThingWithComps
            var props2 = t.def.GetModExtension<SurvivalToolProperties>();
            if (props2?.baseWorkStatFactors != null)
            {
                bool hasClean = props2.baseWorkStatFactors.Any(m => m?.stat == ST_StatDefOf.CleaningSpeed);
                bool hasMed = props2.baseWorkStatFactors.Any(m => m?.stat == ST_StatDefOf.MedicalOperationSpeed || m?.stat == ST_StatDefOf.MedicalSurgerySuccessChance);
                if (hasMed) return STToolKind.Medical;  // fix: medical should not fall through to cleaning
                if (hasClean) return STToolKind.Cleaning;
            }

            // Traditional cleaning names
            if (s.Contains("broom") || s.Contains("mop") || s.Contains("rags") ||
                s.Contains("sponge") || s.Contains("towel")) return STToolKind.Cleaning;

            // Known fabrics we patch as tool-stuff for cleaning/research/medical
            if (dn == "cloth" || dn == "synthread" || dn == "devilstrandcloth" || dn == "hyperweave" ||
                dn == "woolsheep" || dn == "woolalpaca" || dn == "woolmegasloth" || dn == "woolmuffalo" || dn == "woolbison")
                return STToolKind.Cleaning;

            return STToolKind.None;
        }

        /// <summary>
        /// True if this Thing is a survival tool (real tool, tool-stuff, or recognized by name).
        /// </summary>
        public static bool IsSurvivalTool(Thing t)
        {
            if (t == null || t.def == null) return false;

            // Prefer comp detection (keeps compatibility without hard deps on the actual comp type)
            if (t is ThingWithComps twc && twc.AllComps.Any(c => c.GetType().Name.Contains("CompSurvivalTool")))
                return true;

            if (t.def.IsToolStuff())
                return true;

            return ToolKindOf(t) != STToolKind.None;
        }

        #endregion

        #region Job -> ToolKind

        /// <summary>
        /// Expected tool kind for a job. Prefers stat-based mapping, falls back to name heuristics.
        /// </summary>
        public static STToolKind ExpectedToolFor(Pawn pawn, Job job)
        {
            if (job?.def == null) return STToolKind.None;

            // 1) Stat-based mapping (more reliable & mod-friendly)
            var stats = SurvivalToolUtility.StatsForJob(job.def, pawn);
            var kind = KindForStats(stats);
            if (kind != STToolKind.None) return kind;

            // 2) Heuristic fallback (names/driver)
            string jn = job.def.defName ?? string.Empty;
            string driver = job.def.driverClass?.Name ?? string.Empty;
            string curDrv = pawn?.jobs?.curDriver?.GetType().Name ?? string.Empty;
            string s = (jn + "|" + driver + "|" + curDrv).ToLowerInvariant();

            if (s.Contains("mine") || s.Contains("drill")) return STToolKind.Pick;

            if (s.Contains("construct") || s.Contains("frame") || s.Contains("smooth") ||
                s.Contains("buildroof") || s.Contains("removeroof") || s.Contains("build") ||
                s.Contains("deliver") || s.Contains("install")) return STToolKind.Hammer;

            if (s.Contains("repair") || s.Contains("maintain") || s.Contains("maintenance") ||
                s.Contains("fixbroken") || s.Contains("tendmachine") || s.Contains("fix")) return STToolKind.Wrench;

            if (s.Contains("uninstall") || s.Contains("deconstruct") || s.Contains("teardown")) return STToolKind.Wrench;

            if (s.Contains("plantcut") || s.Contains("cutplant") || s.Contains("chop") || s.Contains("prune")) return STToolKind.Sickle;
            if (s.Contains("sow") || s.Contains("plantsow") || s.Contains("plantgrow")) return STToolKind.Hoe;
            if (s.Contains("harvest")) return STToolKind.Sickle;

            if (s.Contains("clean") || s.Contains("sweep") || s.Contains("mop")) return STToolKind.Cleaning;

            if (s.Contains("medical") || s.Contains("surgery") || s.Contains("operate") ||
                s.Contains("tend") || s.Contains("doctor")) return STToolKind.Medical;

            if (s.Contains("research") || s.Contains("study") || s.Contains("analyze")) return STToolKind.Research;

            return STToolKind.None;
        }

        #endregion

        #region Active tool selection

        /// <summary>
        /// Select the most appropriate inventory item for the pawn's current job.
        /// Prefers items (tools or tool-stuff) that actually provide the expected stat(s).
        /// </summary>
        public static Thing TryGetActiveTool(Pawn pawn)
        {
            var inv = pawn?.inventory?.innerContainer;
            var job = pawn?.jobs?.curJob;
            if (inv == null || job == null) return null;

            var expectedKind = ExpectedToolFor(pawn, job);
            if (expectedKind == STToolKind.None) return null;

            var wantedStats = StatsForKind(expectedKind).ToList();

            // Pass A: tool-stuff (materials) with matching stats
            foreach (var item in inv)
            {
                if (!item.def.IsToolStuff()) continue;
                var props = item.def.GetModExtension<SurvivalToolProperties>();
                if (props?.baseWorkStatFactors == null) continue;

                if (props.baseWorkStatFactors.Any(m => m?.stat != null && wantedStats.Contains(m.stat)))
                    return item;
            }

            // Pass B: real tools that have the required stats
            foreach (var item in inv)
            {
                if (!(item is SurvivalTool st)) continue;
                if (st.WorkStatFactors.Any(m => m?.stat != null && wantedStats.Contains(m.stat)))
                    return item;
            }

            // Pass C: exact kind name heuristic
            foreach (var item in inv)
            {
                if (IsSurvivalTool(item) && ToolKindOf(item) == expectedKind)
                    return item;
            }

            // Pass D: soft fallbacks between related kinds (respect hardcore limits)
            if (expectedKind == STToolKind.Hoe)
            {
                foreach (var item in inv)
                    if (IsSurvivalTool(item) && ToolKindOf(item) == STToolKind.Sickle) return item;
            }

            if (expectedKind == STToolKind.Sickle && !SurvivalToolUtility.IsHardcoreModeEnabled)
            {
                foreach (var item in inv)
                    if (IsSurvivalTool(item) && ToolKindOf(item) == STToolKind.Hoe) return item;
            }

            if (expectedKind == STToolKind.Pick)
            {
                foreach (var item in inv)
                    if (ToolKindOf(item) == STToolKind.Pick) return item;
            }

            if (expectedKind == STToolKind.Hammer)
            {
                foreach (var item in inv)
                    if (ToolKindOf(item) == STToolKind.Hammer) return item;
            }

            if (expectedKind == STToolKind.Cleaning)
            {
                foreach (var item in inv)
                    if (IsSurvivalTool(item) && ToolKindOf(item) == STToolKind.Cleaning) return item;
            }

            if (expectedKind == STToolKind.Medical)
            {
                foreach (var item in inv)
                    if (IsSurvivalTool(item) && ToolKindOf(item) == STToolKind.Medical) return item;
            }

            if (expectedKind == STToolKind.Research)
            {
                foreach (var item in inv)
                    if (IsSurvivalTool(item) && ToolKindOf(item) == STToolKind.Research) return item;
            }

            return null;
        }

        #endregion
    }
}
