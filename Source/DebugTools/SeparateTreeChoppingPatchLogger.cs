// RimWorld 1.6 / C# 7.3
// Source/DebugTools/SeparateTreeChoppingPatchLogger.cs
//
// Logs ThingDefs with SurvivalToolProperties and dumps Harmony patches that look
// like they're from Separate Tree Chopping. Intended for debug/compat troubleshooting.
//
// Usage: Enable both SurvivalTools debug logging and compat logging to see output.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.DebugTools
{
    [StaticConstructorOnStartup]
    public static class SeparateTreeChoppingPatchLogger
    {
        static SeparateTreeChoppingPatchLogger()
        {
            // Only run when both debug and compat logging are enabled
            if (!ST_Logging.IsDebugLoggingEnabled || !ST_Logging.IsCompatLogging())
                return;

            try
            {
                LogInfo("[SurvivalTools] SeparateTreeChoppingPatchLogger: starting debug dump...");

                // 1) Try to find known STC assemblies and types
                var stcAssemblyNames = new[] { "SeparateTreeChopping", "TreeChopping" };
                Assembly stcAssembly = null;

                foreach (var asmName in stcAssemblyNames)
                {
                    stcAssembly = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name?.IndexOf(asmName, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (stcAssembly != null) break;
                }

                if (stcAssembly != null)
                {
                    LogInfo($"[SurvivalTools] SeparateTreeChoppingPatchLogger: Found assembly {stcAssembly.FullName}.");

                    // Look for common STC types/methods to analyze
                    TryDumpSeparateTreeChoppingTypes(stcAssembly);
                    DumpPatchesByHeuristics();
                }
                else
                {
                    LogInfo("[SurvivalTools] SeparateTreeChoppingPatchLogger: Separate Tree Chopping assembly not found - falling back to scanning all Harmony patches.");
                    DumpAllLikelySTCPatches();
                }

                // 2) Also check for tree-related WorkGivers and JobDefs that might conflict
                DumpTreeRelatedDefs();

                LogInfo("[SurvivalTools] SeparateTreeChoppingPatchLogger: debug dump complete.");
            }
            catch (Exception e)
            {
                LogError($"[SurvivalTools] SeparateTreeChoppingPatchLogger: failed during debug dump: {e}");
            }
        }

        public static void DumpPatchesByHeuristics()
        {
            try
            {
                var methods = HarmonyLib.Harmony.GetAllPatchedMethods()?.ToList();
                if (methods == null || methods.Count == 0)
                {
                    LogInfo("[SurvivalTools] DumpSTCHeur: no patched methods found.");
                    return;
                }

                var substrings = new[] { "tree", "chopping", "separate", "fell", "harvest" }; // case-insensitive
                int foundTargets = 0;

                foreach (var tgt in methods)
                {
                    var info = HarmonyLib.Harmony.GetPatchInfo(tgt);
                    if (info == null) continue;

                    var matches = new List<(HarmonyLib.Patch p, string kind)>();
                    foreach (var p in info.Prefixes)
                        if (PatchLooksLikeSTC(p, substrings)) matches.Add((p, "PREFIX"));
                    foreach (var p in info.Postfixes)
                        if (PatchLooksLikeSTC(p, substrings)) matches.Add((p, "POSTFIX"));
                    foreach (var p in info.Transpilers)
                        if (PatchLooksLikeSTC(p, substrings)) matches.Add((p, "TRANSPILER"));

                    if (matches.Count == 0) continue;

                    foundTargets++;
                    LogInfo($"[SurvivalTools] DumpSTCHeur: target -> {tgt.FullDescriptionSafe()}");

                    foreach (var (p, kind) in matches)
                    {
                        string pmDesc = p?.PatchMethod != null ? p.PatchMethod.FullDescriptionSafe() : "(unknown)";
                        LogInfo($"   {kind}: {pmDesc} (owner={p.owner}, priority={p.priority}, index={p.index})");
                    }
                }

                LogInfo($"[SurvivalTools] DumpSTCHeur: found {foundTargets} target(s) with STC-like patches.");
            }
            catch (Exception e)
            {
                LogError($"[SurvivalTools] DumpSTCHeur failed: {e}");
            }

            // helper
            bool PatchLooksLikeSTC(HarmonyLib.Patch p, string[] subs)
            {
                if (p == null) return false;
                var owner = p.owner ?? string.Empty;
                if (subs.Any(s => owner.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
                var decl = p.PatchMethod?.DeclaringType;
                var asm = decl?.Assembly?.GetName()?.Name ?? string.Empty;
                var typeFull = decl?.FullName ?? string.Empty;
                if (subs.Any(s => asm.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
                if (subs.Any(s => typeFull.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
                return false;
            }
        }

        // Try to find and analyze types from the STC assembly
        private static void TryDumpSeparateTreeChoppingTypes(Assembly stcAssembly)
        {
            if (stcAssembly == null) return;

            try
            {
                var types = stcAssembly.GetTypes();
                LogInfo($"[SurvivalTools] SeparateTreeChoppingPatchLogger: Found {types.Length} types in STC assembly:");

                foreach (var type in types.Take(20)) // Limit output
                {
                    LogInfo($"  Type: {type.FullName}");

                    // Check if this type has any patched methods
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                    foreach (var method in methods.Take(5)) // Limit to first few methods
                    {
                        var patches = Harmony.GetPatchInfo(method);
                        if (patches != null && (patches.Prefixes.Any() || patches.Postfixes.Any() || patches.Transpilers.Any()))
                        {
                            LogInfo($"    Patched method: {method.Name}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogWarning($"[SurvivalTools] SeparateTreeChoppingPatchLogger: failed to analyze STC types: {e.Message}");
            }
        }

        // Look for tree-related WorkGivers and JobDefs that might conflict
        private static void DumpTreeRelatedDefs()
        {
            try
            {
                // Look for tree-related WorkGivers
                var treeWorkGivers = DefDatabase<WorkGiverDef>.AllDefs
                    .Where(wg => wg.defName != null && (
                        wg.defName.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        wg.defName.IndexOf("fell", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        wg.defName.IndexOf("harvest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        wg.defName.IndexOf("plant", StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToList();

                LogInfo($"[SurvivalTools] Found {treeWorkGivers.Count} tree-related WorkGiverDefs:");

                foreach (var wg in treeWorkGivers.Take(10)) // Limit output
                {
                    var modName = wg.modContentPack?.Name ?? "Unknown";
                    var hasExtension = wg.HasModExtension<WorkGiverExtension>();
                    var extensionInfo = hasExtension ? " (has SurvivalTools extension)" : "";
                    LogInfo($"  {wg.defName} (mod: {modName}, class: {wg.giverClass?.Name}){extensionInfo}");
                }

                // Look for tree-related JobDefs
                var treeJobs = DefDatabase<JobDef>.AllDefs
                    .Where(job => job.defName != null && (
                        job.defName.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        job.defName.IndexOf("fell", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        job.defName.IndexOf("harvest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        job.defName.IndexOf("plant", StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToList();

                LogInfo($"[SurvivalTools] Found {treeJobs.Count} tree-related JobDefs:");

                foreach (var job in treeJobs.Take(10)) // Limit output
                {
                    var modName = job.modContentPack?.Name ?? "Unknown";
                    LogInfo($"  {job.defName} (mod: {modName}, class: {job.driverClass?.Name})");
                }

                // Look for tree-related ThingDefs (tools that might be used for tree work)
                var treeToolDefs = DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.GetModExtension<SurvivalToolProperties>() != null)
                    .Where(d =>
                    {
                        var ext = d.GetModExtension<SurvivalToolProperties>();
                        return ext?.baseWorkStatFactors?.Any(f =>
                            f.stat?.defName?.IndexOf("plant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            f.stat?.defName?.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0) == true;
                    })
                    .ToList();

                LogInfo($"[SurvivalTools] Found {treeToolDefs.Count} SurvivalTools with plant/tree work stats:");

                foreach (var def in treeToolDefs.Take(10)) // Limit output
                {
                    var modName = def.modContentPack?.Name ?? "Unknown";
                    var ext = def.GetModExtension<SurvivalToolProperties>();
                    var relevantStats = ext?.baseWorkStatFactors?.Where(f =>
                        f.stat?.defName?.IndexOf("plant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        f.stat?.defName?.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0)
                        ?.Select(f => f.stat.defName).ToList() ?? new List<string>();

                    LogInfo($"  {def.defName} (mod: {modName}, stats: {string.Join(", ", relevantStats)})");
                }

                // Report current tree felling system status
                LogInfo($"[SurvivalTools] Tree felling system status: {(SurvivalTools.Settings?.enableSurvivalToolTreeFelling == true ? "ENABLED" : "DISABLED")}");
            }
            catch (Exception e)
            {
                LogError($"[SurvivalTools] SeparateTreeChoppingPatchLogger: failed analyzing tree-related defs: {e}");
            }
        }

        // Fallback: enumerate all patched methods and print patches that look like they come from STC
        private static void DumpAllLikelySTCPatches()
        {
            try
            {
                var patchedMethods = Harmony.GetAllPatchedMethods()?.ToList();
                if (patchedMethods == null || patchedMethods.Count == 0)
                {
                    LogInfo("[SurvivalTools] SeparateTreeChoppingPatchLogger: no patched methods found at all.");
                    return;
                }

                int found = 0;

                foreach (var method in patchedMethods)
                {
                    var info = Harmony.GetPatchInfo(method);
                    if (info == null) continue;

                    var stcPatches = new List<(Patch patch, string kind)>();

                    foreach (var p in info.Prefixes)
                        if (IsLikelySTCPatch(p)) stcPatches.Add((p, "PREFIX"));
                    foreach (var p in info.Postfixes)
                        if (IsLikelySTCPatch(p)) stcPatches.Add((p, "POSTFIX"));
                    foreach (var p in info.Transpilers)
                        if (IsLikelySTCPatch(p)) stcPatches.Add((p, "TRANSPILER"));

                    if (stcPatches.Count == 0) continue;

                    found++;
                    LogInfo($"[SurvivalTools] SeparateTreeChoppingPatchLogger: STC-style patches on target -> {method.FullDescriptionSafe()}");

                    foreach (var (patch, kind) in stcPatches)
                    {
                        LogInfo($"  {kind}: {patch.PatchMethod?.FullDescriptionSafe() ?? "(unknown)"} (Owner={patch.owner}, Priority={patch.priority}, Index={patch.index})");
                    }
                }

                if (found == 0)
                    LogInfo("[SurvivalTools] SeparateTreeChoppingPatchLogger: No Separate Tree Chopping-style patches found (owner/name filters).");
                else
                    LogInfo($"[SurvivalTools] SeparateTreeChoppingPatchLogger: Found STC-style patches on {found} target(s).");
            }
            catch (Exception e)
            {
                LogError($"[SurvivalTools] SeparateTreeChoppingPatchLogger: failed scanning all patches: {e}");
            }
        }

        // Heuristic checks to see whether a Harmony.Patch likely originates from Separate Tree Chopping
        private static bool IsLikelySTCPatch(Patch p)
        {
            if (p == null) return false;

            try
            {
                // 1) owner string often holds harmonyId passed when the author created the Harmony instance
                if (!string.IsNullOrEmpty(p.owner) &&
                    (p.owner.IndexOf("separatetreechopping", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     p.owner.IndexOf("treechopping", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     p.owner.IndexOf("separate", StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;

                // 2) fallback: inspect declaring assembly / type name of the patch method
                var declType = p.PatchMethod?.DeclaringType;
                var asmName = declType?.Assembly?.GetName()?.Name ?? string.Empty;
                var typeFullName = declType?.FullName ?? string.Empty;

                if (!string.IsNullOrEmpty(asmName) &&
                    (asmName.IndexOf("separatetreechopping", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     asmName.IndexOf("treechopping", StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;

                if (!string.IsNullOrEmpty(typeFullName) &&
                    (typeFullName.IndexOf("SeparateTreeChopping", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     typeFullName.IndexOf("TreeChopping", StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;
            }
            catch
            {
                // ignore reflection failures and treat as non-match
            }

            return false;
        }

        // -------------------------
        // Reflection helper methods
        // -------------------------
        private static string FullDescriptionSafe(this MethodBase mb)
        {
            if (mb == null) return "(null MethodBase)";
            try
            {
                var decl = mb.DeclaringType != null ? mb.DeclaringType.FullName : "(no type)";
                if (mb is MethodInfo mi)
                {
                    var paramDesc = string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
                    return $"{decl}.{mi.ReturnType.Name} {mi.Name}({paramDesc})";
                }
                var parms = string.Join(", ", mb.GetParameters().Select(p => p.ParameterType.Name));
                return $"{decl}.{mb.Name}({parms})";
            }
            catch
            {
                return mb.ToString();
            }
        }
    }
}
