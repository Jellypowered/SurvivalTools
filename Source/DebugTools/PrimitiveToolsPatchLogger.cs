// RimWorld 1.6 / C# 7.3
// Source/DebugTools/PrimitiveToolsPatchLogger.cs
//
// Debug utility for Primitive Tools compatibility diagnostics.
// Now runs only when triggered manually via the Debug Actions menu,
// and only shows if Primitive Tools is active.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using SurvivalTools.Compat.PrimitiveTools;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;
using LudeonTK;

namespace SurvivalTools.DebugTools
{
    public static class PrimitiveToolsPatchLogger
    {
        [DebugAction("SurvivalTools", "Dump Primitive Tools diagnostics", allowedGameStates = AllowedGameStates.Playing)]
        public static void DumpPrimitiveTools_DebugAction()
        {
            // Gate: only show when Primitive Tools is active
            if (!PrimitiveToolsCompat.IsPrimitiveToolsActive())
            {
                Messages.Message("Primitive Tools not detected — debug action unavailable.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            try
            {
                LogInfo("[SurvivalTools] PrimitiveToolsPatchLogger: starting debug dump...");

                // 1) Try to find known PT assemblies and types
                var ptAssemblyName = "PrimitiveTools";
                var ptAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name?.IndexOf(ptAssemblyName, StringComparison.OrdinalIgnoreCase) >= 0);

                if (ptAssembly != null)
                {
                    LogInfo($"[SurvivalTools] PrimitiveToolsPatchLogger: Found assembly {ptAssembly.FullName}.");
                    TryDumpPrimitiveToolsTypes(ptAssembly);
                    DumpPatchesByHeuristics();
                }
                else
                {
                    LogInfo("[SurvivalTools] PrimitiveToolsPatchLogger: Primitive Tools assembly not found - falling back to scanning all Harmony patches.");
                    DumpAllLikelyPTPatches();
                }

                // 2) Also check for PT ThingDefs that might conflict with our tools
                DumpPrimitiveToolsThingDefs();

                LogInfo("[SurvivalTools] PrimitiveToolsPatchLogger: debug dump complete.");
            }
            catch (Exception e)
            {
                LogError($"[SurvivalTools] PrimitiveToolsPatchLogger: failed during debug dump: {e}");
            }
        }

        public static void DumpPatchesByHeuristics()
        {
            try
            {
                var methods = Harmony.GetAllPatchedMethods()?.ToList();
                if (methods.NullOrEmpty())
                {
                    LogInfo("[SurvivalTools] DumpPTHeur: no patched methods found.");
                    return;
                }

                var substrings = new[] { "primitive", "tools", "vby" }; // case-insensitive
                int foundTargets = 0;

                foreach (var tgt in methods)
                {
                    var info = Harmony.GetPatchInfo(tgt);
                    if (info == null) continue;

                    var matches = new List<(HarmonyLib.Patch p, string kind)>();
                    foreach (var p in info.Prefixes)
                        if (PatchLooksLikePT(p, substrings)) matches.Add((p, "PREFIX"));
                    foreach (var p in info.Postfixes)
                        if (PatchLooksLikePT(p, substrings)) matches.Add((p, "POSTFIX"));
                    foreach (var p in info.Transpilers)
                        if (PatchLooksLikePT(p, substrings)) matches.Add((p, "TRANSPILER"));

                    if (matches.Count == 0) continue;

                    foundTargets++;
                    LogInfo($"[SurvivalTools] DumpPTHeur: target -> {tgt.FullDescriptionSafe()}");

                    foreach (var (p, kind) in matches)
                    {
                        string pmDesc = p?.PatchMethod != null ? p.PatchMethod.FullDescriptionSafe() : "(unknown)";
                        LogInfo($"   {kind}: {pmDesc} (owner={p.owner}, priority={p.priority}, index={p.index})");
                    }
                }

                LogInfo($"[SurvivalTools] DumpPTHeur: found {foundTargets} target(s) with PT-like patches.");
            }
            catch (Exception e)
            {
                LogError($"[SurvivalTools] DumpPTHeur failed: {e}");
            }

            // helper
            bool PatchLooksLikePT(HarmonyLib.Patch p, string[] subs)
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

        private static void TryDumpPrimitiveToolsTypes(Assembly ptAssembly)
        {
            if (ptAssembly == null) return;

            try
            {
                var types = ptAssembly.GetTypes();
                LogInfo($"[SurvivalTools] PrimitiveToolsPatchLogger: Found {types.Length} types in PT assembly:");

                foreach (var type in types.Take(20)) // Limit output
                {
                    LogInfo($"  Type: {type.FullName}");

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
                LogWarning($"[SurvivalTools] PrimitiveToolsPatchLogger: failed to analyze PT types: {e.Message}");
            }
        }

        private static void DumpPrimitiveToolsThingDefs()
        {
            try
            {
                var primitiveToolDefs = DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.defName != null && (
                        d.defName.IndexOf("primitive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        d.defName.IndexOf("stone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        d.defName.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        d.modContentPack?.Name?.IndexOf("primitive", StringComparison.OrdinalIgnoreCase) >= 0))
                    .Where(d => d.IsWeapon || d.GetModExtension<SurvivalToolProperties>() != null || d.thingCategories?.Any(tc =>
                        tc.defName.IndexOf("tool", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        tc.defName.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0) == true)
                    .ToList();

                LogInfo($"[SurvivalTools] Found {primitiveToolDefs.Count} potential primitive tool ThingDefs:");

                foreach (var def in primitiveToolDefs.Take(10))
                {
                    var modName = def.modContentPack?.Name ?? "Unknown";
                    var categories = def.thingCategories?.Select(tc => tc.defName).ToList() ?? new List<string>();
                    LogInfo($"  {def.defName} (mod: {modName}, categories: {string.Join(", ", categories)})");

                    var ext = def.GetModExtension<SurvivalToolProperties>();
                    if (ext != null)
                    {
                        LogInfo($"    Has SurvivalToolProperties extension");
                    }
                }
            }
            catch (Exception e)
            {
                LogError($"[SurvivalTools] PrimitiveToolsPatchLogger: failed analyzing ThingDefs: {e}");
            }
        }

        private static void DumpAllLikelyPTPatches()
        {
            try
            {
                var patchedMethods = Harmony.GetAllPatchedMethods()?.ToList();
                if (patchedMethods == null || patchedMethods.Count == 0)
                {
                    LogInfo("[SurvivalTools] PrimitiveToolsPatchLogger: no patched methods found at all.");
                    return;
                }

                int found = 0;

                foreach (var method in patchedMethods)
                {
                    var info = Harmony.GetPatchInfo(method);
                    if (info == null) continue;

                    var ptPatches = new List<(Patch patch, string kind)>();

                    foreach (var p in info.Prefixes)
                        if (IsLikelyPTPatch(p)) ptPatches.Add((p, "PREFIX"));
                    foreach (var p in info.Postfixes)
                        if (IsLikelyPTPatch(p)) ptPatches.Add((p, "POSTFIX"));
                    foreach (var p in info.Transpilers)
                        if (IsLikelyPTPatch(p)) ptPatches.Add((p, "TRANSPILER"));

                    if (ptPatches.Count == 0) continue;

                    found++;
                    LogInfo($"[SurvivalTools] PrimitiveToolsPatchLogger: PT-style patches on target -> {method.FullDescriptionSafe()}");

                    foreach (var (patch, kind) in ptPatches)
                    {
                        LogInfo($"  {kind}: {patch.PatchMethod?.FullDescriptionSafe() ?? "(unknown)"} (Owner={patch.owner}, Priority={patch.priority}, Index={patch.index})");
                    }
                }

                if (found == 0)
                    LogInfo("[SurvivalTools] PrimitiveToolsPatchLogger: No Primitive Tools-style patches found (owner/name filters).");
                else
                    LogInfo($"[SurvivalTools] PrimitiveToolsPatchLogger: Found PT-style patches on {found} target(s).");
            }
            catch (Exception e)
            {
                LogError($"[SurvivalTools] PrimitiveToolsPatchLogger: failed scanning all patches: {e}");
            }
        }

        private static bool IsLikelyPTPatch(Patch p)
        {
            if (p == null) return false;

            try
            {
                if (!string.IsNullOrEmpty(p.owner) &&
                    (p.owner.IndexOf("primitivetools", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     p.owner.IndexOf("primitive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     p.owner.IndexOf("vby", StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;

                var declType = p.PatchMethod?.DeclaringType;
                var asmName = declType?.Assembly?.GetName()?.Name ?? string.Empty;
                var typeFullName = declType?.FullName ?? string.Empty;

                if (!string.IsNullOrEmpty(asmName) &&
                    asmName.IndexOf("primitivetools", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (!string.IsNullOrEmpty(typeFullName) &&
                    (typeFullName.IndexOf("PrimitiveTools", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     typeFullName.IndexOf("Primitive", StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;
            }
            catch
            {
                // ignore reflection failures
            }

            return false;
        }

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
