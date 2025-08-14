using System;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools
{
    public enum STToolKind { None, Axe, Hammer, Pick, Knife, Hoe, Saw, Sickle }

    public static class ActiveToolResolver
    {
        public static STToolKind ExpectedToolFor(Pawn pawn, Job job)
        {
            if (job == null || job.def == null) return STToolKind.None;

            string dn = job.def.defName ?? string.Empty;
            string drvDef = job.def.driverClass != null ? job.def.driverClass.Name : string.Empty;
            string drvCur = (pawn != null && pawn.jobs != null && pawn.jobs.curDriver != null)
                            ? pawn.jobs.curDriver.GetType().Name : string.Empty;

            string s = (dn + "|" + drvDef + "|" + drvCur).ToLowerInvariant();

            // Mining & drilling
            if (job.def == JobDefOf.Mine || job.def == JobDefOf.OperateDeepDrill ||
                s.IndexOf("mine") >= 0 || s.IndexOf("deepdrill") >= 0 || s.IndexOf("operatedeepdrill") >= 0 || s.IndexOf("drill") >= 0)
                return STToolKind.Pick;

            // Construction family: frames, deliver, finish, repair, smooth, roofs, (un)install, deconstruct, build
            if (s.IndexOf("finishframe") >= 0 || s.IndexOf("deliver") >= 0 || s.IndexOf("construct") >= 0 || s.IndexOf("frame") >= 0 ||
                s.IndexOf("repair") >= 0 || s.IndexOf("smooth") >= 0 ||
                s.IndexOf("buildroof") >= 0 || s.IndexOf("removeroof") >= 0 ||
                s.IndexOf("install") >= 0 || s.IndexOf("uninstall") >= 0 ||
                s.IndexOf("deconstruct") >= 0 || s.IndexOf("build") >= 0)
                return STToolKind.Hammer;

            // Plant work
            if (s.IndexOf("plantcut") >= 0 || s.IndexOf("cutplant") >= 0 || s.IndexOf("chop") >= 0 || s.IndexOf("prune") >= 0)
                return STToolKind.Axe;      // felling/clearing

            if (s.IndexOf("sow") >= 0 || s.IndexOf("plantsow") >= 0 || s.IndexOf("plantgrow") >= 0)
                return STToolKind.Hoe;      // sowing

            if (s.IndexOf("harvest") >= 0)
                return STToolKind.Sickle;   // harvesting

            return STToolKind.None;
        }


        // Try to detect the Survival Tools comp via reflection without a hard assembly reference.

        private static object TryGetSurvivalToolComp(Thing t)
        {
            var twc = t as ThingWithComps;
            if (twc == null) return null;

            var comps = twc.AllComps;
            if (comps == null) return null;

            for (int i = 0; i < comps.Count; i++)
            {
                var c = comps[i];
                if (c == null) continue;

                // match by type name without taking a hard reference
                string name = c.GetType().Name;
                if (!string.IsNullOrEmpty(name) &&
                    (name == "CompSurvivalTool" || name.EndsWith("CompSurvivalTool")))
                    return c;
            }
            return null;
        }


        public static bool IsSurvivalTool(Thing t)
        {
            if (t == null || t.def == null) return false;

            // Prefer comp detection if available
            if (TryGetSurvivalToolComp(t) != null) return true;

            // Fallback: heuristic by defName
            var dn = t.def.defName;
            if (string.IsNullOrEmpty(dn)) return false;
            dn = dn.ToLowerInvariant();

            return dn.IndexOf("axe") >= 0 || dn.IndexOf("pick") >= 0 || dn.IndexOf("hammer") >= 0 ||
                   dn.IndexOf("hoe") >= 0 || dn.IndexOf("saw") >= 0 || dn.IndexOf("sickle") >= 0 ||
                   dn.IndexOf("knife") >= 0;
        }

        // ActiveToolResolver.cs
        public static STToolKind ToolKindOf(Thing t)
        {
            if (t == null || t.def == null) return STToolKind.None;
            string dn = t.def.defName != null ? t.def.defName.ToLowerInvariant() : string.Empty;

            // specific → generic, to avoid "pickaxe" hitting "axe"
            if (dn.IndexOf("pickaxe") >= 0 || dn.IndexOf("pick") >= 0) return STToolKind.Pick;
            if (dn.IndexOf("sickle") >= 0) return STToolKind.Sickle;
            if (dn.IndexOf("hammer") >= 0 || dn.IndexOf("mallet") >= 0) return STToolKind.Hammer;
            if (dn.IndexOf("hatchet") >= 0) return STToolKind.Axe;
            if (dn.IndexOf("axe") >= 0) return STToolKind.Axe;
            if (dn.IndexOf("hoe") >= 0) return STToolKind.Hoe;
            if (dn.IndexOf("saw") >= 0) return STToolKind.Saw;
            if (dn.IndexOf("knife") >= 0) return STToolKind.Knife;
            return STToolKind.None;
        }


        public static Thing TryGetActiveTool(Pawn pawn)
        {
            if (pawn == null || pawn.inventory == null || pawn.jobs == null || pawn.jobs.curJob == null)
                return null;

            var expected = ExpectedToolFor(pawn, pawn.jobs.curJob);
            if (expected == STToolKind.None) return null;

            // Pass 1: exact match
            foreach (var t in pawn.inventory.innerContainer)
                if (IsSurvivalTool(t) && ToolKindOf(t) == expected)
                    return t;

            // Targeted fallbacks
            if (expected == STToolKind.Pick)
                foreach (var t in pawn.inventory.innerContainer)
                {
                    var dn = t.def != null && t.def.defName != null ? t.def.defName.ToLowerInvariant() : string.Empty;
                    if (dn.IndexOf("pick") >= 0) return t;
                }

            if (expected == STToolKind.Hoe)
                foreach (var t in pawn.inventory.innerContainer)
                    if (IsSurvivalTool(t) && ToolKindOf(t) == STToolKind.Sickle) // sow fallback to sickle if no hoe
                        return t;

            if (expected == STToolKind.Hammer)
                foreach (var t in pawn.inventory.innerContainer)
                {
                    var dn = t.def != null && t.def.defName != null ? t.def.defName.ToLowerInvariant() : string.Empty;
                    if (dn.IndexOf("hammer") >= 0 || dn.IndexOf("mallet") >= 0) return t;
                }

            // No generic “first tool” fallback anymore — if no right tool, show nothing
            return null;
        }

    }
}
