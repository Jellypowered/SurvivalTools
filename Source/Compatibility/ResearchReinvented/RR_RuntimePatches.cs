// RR_RuntimePatches.cs
// RimWorld 1.6 / C# 7.3
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LudeonTK;  // DebugAction attribute
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools.Compat
{
    /// <summary>
    /// Runtime utilities to inspect Harmony patches and specifically look for Research Reinvented style patches.
    /// - Null-safe over Harmony patch collections
    /// - Unique helper name for method formatting to avoid collisions
    /// - Minimal & stable logging format
    /// </summary>
    public static class ResearchReinventedPatchLogger
    {
        // Debug action entry in the in-game debug menu.
        [DebugAction("SurvivalTools", "Dump Research Reinvented Harmony patches")]
        public static void DumpResearchReinventedPatches_DebugAction()
        {
            DumpResearchReinventedPatches();
        }

        /// <summary>
        /// Enumerate all patched methods and log patches that appear to be from Research Reinvented.
        /// </summary>
        public static void DumpResearchReinventedPatches()
        {
            try
            {
                Log.Message("[SurvivalTools] ResearchReinventedPatchLogger: starting debug dump...");

                var patchedMethods = Harmony.GetAllPatchedMethods()?.ToList();
                if (patchedMethods == null || patchedMethods.Count == 0)
                {
                    Log.Message("[SurvivalTools] DumpRR: no patched methods found at all.");
                    Log.Message("[SurvivalTools] ResearchReinventedPatchLogger: debug dump complete.");
                    return;
                }

                int targetsFound = 0;

                foreach (var method in patchedMethods)
                {
                    var info = Harmony.GetPatchInfo(method);
                    if (info == null) continue;

                    var rrPatches = new List<(HarmonyLib.Patch patch, string kind)>();

                    var prefixes = info.Prefixes ?? Enumerable.Empty<HarmonyLib.Patch>();
                    var postfixes = info.Postfixes ?? Enumerable.Empty<HarmonyLib.Patch>();
                    var transpilers = info.Transpilers ?? Enumerable.Empty<HarmonyLib.Patch>();

                    foreach (var p in prefixes)
                        if (IsLikelyRRPatch(p)) rrPatches.Add((p, "PREFIX"));
                    foreach (var p in postfixes)
                        if (IsLikelyRRPatch(p)) rrPatches.Add((p, "POSTFIX"));
                    foreach (var p in transpilers)
                        if (IsLikelyRRPatch(p)) rrPatches.Add((p, "TRANSPILER"));

                    if (rrPatches.Count == 0) continue;

                    targetsFound++;
                    Log.Message($"[SurvivalTools] DumpRRHeur: target -> {FormatMethodFullDescription(method)}");

                    foreach (var entry in rrPatches)
                    {
                        var patch = entry.patch;
                        var kind = entry.kind;

                        string patchDesc = patch?.PatchMethod != null
                            ? FormatMethodFullDescription(patch.PatchMethod)
                            : "(unknown)";

                        Log.Message($"   {kind}: {patchDesc} (owner={patch.owner}, priority={patch.priority}, index={patch.index})");
                    }
                }

                if (targetsFound == 0)
                    Log.Message("[SurvivalTools] DumpRRHeur: No Research Reinvented patches found (owner/name filters).");
                else
                    Log.Message($"[SurvivalTools] DumpRRHeur: found {targetsFound} target(s) with RR-like patches.");

                Log.Message("[SurvivalTools] ResearchReinventedPatchLogger: debug dump complete.");
            }
            catch (Exception e)
            {
                Log.Error($"[SurvivalTools] DumpRR: failed to enumerate patches: {e}");
            }
        }

        /// <summary>
        /// Heuristic to decide whether a Harmony patch looks like it's from Research Reinvented.
        /// Checks owner string and the declaring type / assembly name for known tokens.
        /// </summary>
        private static bool IsLikelyRRPatch(HarmonyLib.Patch p)
        {
            if (p == null) return false;

            // Owner id check
            if (!string.IsNullOrEmpty(p.owner))
            {
                var owner = p.owner;
                if (owner.IndexOf("researchreinvented", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (owner.IndexOf("petetimesix", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            // Declaring type / assembly check
            var dt = p.PatchMethod?.DeclaringType;
            if (dt != null)
            {
                var asmName = dt.Assembly?.GetName()?.Name ?? string.Empty;
                if (asmName.IndexOf("researchreinvented", StringComparison.OrdinalIgnoreCase) >= 0) return true;

                var fullName = dt.FullName ?? string.Empty;
                if (fullName.IndexOf("ResearchReinvented", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (fullName.IndexOf("PeteTimesSix", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        /// <summary>
        /// Produce a safe, useful one-line description for a MethodBase.
        /// Named uniquely to avoid collisions with other projects' extension methods.
        /// </summary>
        public static string FormatMethodFullDescription(MethodBase mb)
        {
            if (mb == null) return "(null MethodBase)";
            try
            {
                var decl = mb.DeclaringType != null ? mb.DeclaringType.FullName : "(no type)";

                var mi = mb as MethodInfo;
                if (mi != null)
                {
                    var parameters = mi.GetParameters();
                    var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    var returnType = mi.ReturnType != null ? mi.ReturnType.Name : "void";
                    return $"{returnType} {decl}.{mi.Name}({paramStr})";
                }

                var ci = mb as ConstructorInfo;
                if (ci != null)
                {
                    var parameters = ci.GetParameters();
                    var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    return $"ctor {decl}({paramStr})";
                }

                // fallback
                return $"{decl}.{mb.Name}";
            }
            catch
            {
                return mb.ToString();
            }
        }
    }
}
