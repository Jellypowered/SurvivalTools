// file: Compatibility/SeparateTreeChopping/STC_Strip_TreeFelling.cs
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging; // use LogCompatMessage/LogCompatWarning

namespace SurvivalTools.Compat
{
    /// <summary>
    /// When Separate Tree Chopping (STC) is active, SurvivalTools must not
    /// expose or service any "fell tree" paths. This class:
    ///   1) Removes SurvivalTools' own tree WorkGivers from WorkType lists.
    ///   2) Guards vanilla PlantsCut so it never returns tree jobs (so no float-menu entry).
    /// If STC is not active, this does nothing.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Compat_STC_TreeStrip
    {
        static Compat_STC_TreeStrip()
        {
            try
            {
                if (!IsSTCActive())
                    return; // No changes unless STC is in the mod list / authority says "separate"

                // 1) Prune any SurvivalTools tree/felling WorkGivers from WorkType lists.
                try
                {
                    StripSTTreeWorkGiversFromWorkTypes();
                }
                catch (Exception ex)
                {
                    LogCompatWarning($"[ST×STC] Exception while stripping tree WorkGivers: {ex}");
                }

                // 2) Guard PlantsCut via base WorkGiver_Scanner methods (version-safe).
                try
                {
                    var h = new Harmony("SurvivalToolsReborn.Compat.STC.TreeStrip");

                    // Patch just the classes in this file to keep scope tight.
                    h.CreateClassProcessor(typeof(PlantsCut_TreeGuard.WGScanner_HasJobOnThing_Prefix)).Patch();
                    h.CreateClassProcessor(typeof(PlantsCut_TreeGuard.WGScanner_JobOnThing_Prefix)).Patch();
                }
                catch (Exception ex)
                {
                    LogCompatWarning($"[ST×STC] Harmony patch failure (tree guard): {ex}");
                }

                LogCompatMessage("[ST×STC] Tree felling stripped (STC authority active).", "STC.TreeStrip.InitMsg");
            }
            catch (Exception e)
            {
                LogCompatWarning($"[ST×STC] Tree strip init failed: {e}");
            }
        }

        /// <summary>
        /// Prefer the central arbiter if present; otherwise fall back to a packageId/name probe.
        /// </summary>
        private static bool IsSTCActive()
        {
            try
            {
                // Preferred: query the arbiter we already log from elsewhere.
                var arbiterType = AccessTools.TypeByName("SurvivalTools.Compat.TreeSystemArbiter");
                if (arbiterType != null)
                {
                    var prop = AccessTools.Property(arbiterType, "IsSeparateTreeChopping");
                    if (prop != null)
                    {
                        var boxed = prop.GetValue(null, null);
                        if (boxed is bool b) return b;
                    }
                }

                // Fallback: scan running mods (package id heuristic).
                var list = LoadedModManager.RunningModsListForReading;
                if (list != null)
                {
                    foreach (var m in list)
                    {
                        if (m == null) continue;
                        string id = (m.PackageId ?? string.Empty).ToLowerInvariant();
                        string nm = (m.Name ?? string.Empty).ToLowerInvariant();
                        if (id.Contains("separatetree") || nm.Contains("separate tree chopping"))
                            return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogCompatWarning($"[ST×STC] IsSTCActive probe failed: {ex}");
            }
            return false;
        }

        /// <summary>
        /// Remove SurvivalTools tree/felling WGs from every WorkType's list so AI & float menu never enumerate them.
        /// Runtime-only; XML remains intact for non-STC runs.
        /// </summary>
        private static void StripSTTreeWorkGiversFromWorkTypes()
        {
            var removed = new List<string>();
            List<WorkTypeDef> all;

            try
            {
                all = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            }
            catch (Exception ex)
            {
                LogCompatWarning($"[ST×STC] Unable to enumerate WorkTypeDefs: {ex}");
                return;
            }

            if (all == null || all.Count == 0) return;

            foreach (var wt in all)
            {
                if (wt == null) continue;
                var list = wt.workGiversByPriority;
                if (list == null || list.Count == 0) continue;

                try
                {
                    int before = list.Count;
                    list.RemoveAll(LooksLikeSTTreeWG);
                    if (list.Count != before)
                    {
                        removed.Add($"{wt.defName ?? "(null)"}(-{before - list.Count})");
                    }
                }
                catch (Exception ex)
                {
                    LogCompatWarning($"[ST×STC] Failed pruning work givers for {wt?.defName ?? "(null)"}: {ex}");
                }
            }

            if (removed.Count > 0)
                LogCompatMessage($"[ST×STC] Removed ST tree WorkGivers from WorkType lists: {string.Join(", ", removed)}",
                                 "STC.TreeStrip.RemovedList");
        }

        private static bool LooksLikeSTTreeWG(WorkGiverDef wg)
        {
            if (wg == null) return false;

            string pid = SafeLower(wg.modContentPack?.PackageId);
            string name = SafeLower(wg.defName);
            string gcn = SafeLower(wg.giverClass?.Name);
            string gca = SafeLower(wg.giverClass?.FullName);

            // Only strip SurvivalTools-owned WGs.
            bool fromST =
                (!string.IsNullOrEmpty(pid) && (pid.Contains("survivaltools") || pid.Contains("jelly"))) ||
                (!string.IsNullOrEmpty(gca) && gca.StartsWith("survivaltools.", StringComparison.OrdinalIgnoreCase));

            // Never strip anything explicitly "chop tree" (reserved for STC / other authority).
            if ((!string.IsNullOrEmpty(name) && name.Contains("chop") && name.Contains("tree")) ||
                (!string.IsNullOrEmpty(gcn) && gcn.Contains("chop") && gcn.Contains("tree")) ||
                (!string.IsNullOrEmpty(gca) && gca.Contains("chop") && gca.Contains("tree")))
                return false;

            bool treeish =
                (!string.IsNullOrEmpty(name) && (name.Contains("tree") || name.Contains("fell"))) ||
                (!string.IsNullOrEmpty(gcn) && (gcn.Contains("tree") || gcn.Contains("fell"))) ||
                (!string.IsNullOrEmpty(gca) && (gca.Contains("tree") || gca.Contains("fell")));

            return fromST && treeish;
        }

        private static string SafeLower(string s)
        {
            try { return s == null ? string.Empty : s.ToLowerInvariant(); }
            catch { return string.Empty; }
        }

        /// <summary>
        /// Version-safe guard that prevents vanilla PlantsCut from servicing trees while STC is active.
        /// We patch the base WorkGiver_Scanner methods and gate by "__instance is WorkGiver_PlantsCut".
        /// </summary>
        private static class PlantsCut_TreeGuard
        {
            private static bool IsTree(Thing t)
                => t is Plant p && p.def?.plant != null && p.def.plant.IsTree;

            // bool WorkGiver_Scanner.HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
            [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.HasJobOnThing))]
            internal static class WGScanner_HasJobOnThing_Prefix
            {
                static bool Prefix(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
                {
                    if (!(__instance is WorkGiver_PlantsCut)) return true; // not PlantsCut → ignore

                    if (t == null || IsTree(t))
                    {
                        __result = false; // STC owns trees → no job here
                        return false;     // skip original
                    }
                    return true;
                }
            }

            // Job WorkGiver_Scanner.JobOnThing(Pawn pawn, Thing t, bool forced = false)
            [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.JobOnThing))]
            internal static class WGScanner_JobOnThing_Prefix
            {
                static bool Prefix(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced, ref Job __result)
                {
                    if (!(__instance is WorkGiver_PlantsCut)) return true;

                    if (t == null || IsTree(t))
                    {
                        __result = null; // nothing for trees here
                        return false;
                    }
                    return true;
                }
            }
        }
    }
}
