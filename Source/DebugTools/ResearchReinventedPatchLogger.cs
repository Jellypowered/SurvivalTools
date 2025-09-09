// RimWorld 1.6 / C# 7.3
// Source/DebugTools/ResearchReinventedPatchLogger.cs
//
// Debug utility for Research Reinvented compatibility diagnostics.
// Now runs only when triggered manually via the Debug Actions menu,
// and only shows if Research Reinvented is active.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using SurvivalTools.Compat;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;
using LudeonTK;
using SurvivalTools.Compat.ResearchReinvented;

namespace SurvivalTools.DebugTools
{
    public static class ResearchReinventedPatchLogger
    {
        [DebugAction("SurvivalTools", "Dump Research Reinvented diagnostics", allowedGameStates = AllowedGameStates.Playing)]
        public static void DumpRR_DebugAction()
        {
            // Gate: only show when Research Reinvented is active
            if (!RRHelpers.IsRRActive)
            {
                Messages.Message("Research Reinvented not detected — debug action unavailable.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            try
            {
                LogInfo("[SurvivalTools] ResearchReinventedPatchLogger: starting debug dump...");

                var rrTypeName = "PeteTimesSix.ResearchReinvented.Utilities.PawnExtensions, ResearchReinvented";
                var rrType = Type.GetType(rrTypeName);

                if (rrType != null)
                {
                    LogInfo($"[SurvivalTools] ResearchReinventedPatchLogger: Found type {rrType.FullName}.");
                    TryDumpMethodPatches(rrType, "CanEverDoResearch");
                    TryDumpMethodPatches(rrType, "CanNowDoResearch");
                    DumpPatchesByHeuristics();
                }
                else
                {
                    LogInfo("[SurvivalTools] ResearchReinventedPatchLogger: PawnExtensions type not found — scanning all patches instead.");
                    DumpAllLikelyRRPatches();
                }

                LogInfo("[SurvivalTools] ResearchReinventedPatchLogger: debug dump complete.");
            }
            catch (Exception e)
            {
                LogError($"[SurvivalTools] ResearchReinventedPatchLogger: failed during debug dump: {e}");
            }
        }

        public static void DumpPatchesByHeuristics()
        {
            try
            {
                var methods = Harmony.GetAllPatchedMethods()?.ToList();
                if (methods.NullOrEmpty())
                {
                    LogInfo("[SurvivalTools] DumpRRHeur: no patched methods found.");
                    return;
                }

                var substrings = new[] { "research", "reinvent", "pete", "timesix" };
                int foundTargets = 0;

                foreach (var tgt in methods)
                {
                    var info = Harmony.GetPatchInfo(tgt);
                    if (info == null) continue;

                    var matches = new List<(HarmonyLib.Patch p, string kind)>();
                    foreach (var p in info.Prefixes)
                        if (PatchLooksLikeRR(p, substrings)) matches.Add((p, "PREFIX"));
                    foreach (var p in info.Postfixes)
                        if (PatchLooksLikeRR(p, substrings)) matches.Add((p, "POSTFIX"));
                    foreach (var p in info.Transpilers)
                        if (PatchLooksLikeRR(p, substrings)) matches.Add((p, "TRANSPILER"));

                    if (matches.Count == 0) continue;

                    foundTargets++;
                    LogInfo($"[SurvivalTools] DumpRRHeur: target -> {tgt.FullDescriptionSafe()}");

                    foreach (var (p, kind) in matches)
                    {
                        string pmDesc = p?.PatchMethod != null ? p.PatchMethod.FullDescriptionSafe() : "(unknown)";
                        LogInfo($"   {kind}: {pmDesc} (owner={p.owner}, priority={p.priority}, index={p.index})");
                    }
                }

                LogInfo($"[SurvivalTools] DumpRRHeur: found {foundTargets} target(s) with RR-like patches.");
            }
            catch (Exception e)
            {
                LogError($"[SurvivalTools] DumpRRHeur failed: {e}");
            }

            bool PatchLooksLikeRR(HarmonyLib.Patch p, string[] subs)
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

        private static void TryDumpMethodPatches(Type type, string methodName)
        {
            if (type == null || string.IsNullOrEmpty(methodName)) return;

            try
            {
                var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    LogInfo($"[SurvivalTools] ResearchReinventedPatchLogger: method {type.FullName}.{methodName} not found.");
                    return;
                }

                var patches = Harmony.GetPatchInfo(method);
                if (patches == null)
                {
                    LogInfo($"[SurvivalTools] ResearchReinventedPatchLogger: No patches found on {type.FullName}.{methodName}");
                    return;
                }

                LogInfo($"[SurvivalTools] Patches on {type.FullName}.{methodName}:");

                foreach (var patch in patches.Prefixes)
                    LogInfo($"  PREFIX: {patch.PatchMethod.FullDescriptionSafe()} (Owner={patch.owner}, Priority={patch.priority}, Index={patch.index})");

                foreach (var patch in patches.Postfixes)
                    LogInfo($"  POSTFIX: {patch.PatchMethod.FullDescriptionSafe()} (Owner={patch.owner}, Priority={patch.priority}, Index={patch.index})");

                foreach (var patch in patches.Transpilers)
                    LogInfo($"  TRANSPILER: {patch.PatchMethod.FullDescriptionSafe()} (Owner={patch.owner}, Priority={patch.priority}, Index={patch.index})");
            }
            catch (Exception e)
            {
                LogWarning($"[SurvivalTools] ResearchReinventedPatchLogger: failed to dump patches for {type.FullName}.{methodName}: {e.Message}");
            }
        }

        private static void DumpAllLikelyRRPatches()
        {
            try
            {
                var patchedMethods = Harmony.GetAllPatchedMethods()?.ToList();
                if (patchedMethods == null || patchedMethods.Count == 0)
                {
                    LogInfo("[SurvivalTools] ResearchReinventedPatchLogger: no patched methods found at all.");
                    return;
                }

                int found = 0;

                foreach (var method in patchedMethods)
                {
                    var info = Harmony.GetPatchInfo(method);
                    if (info == null) continue;

                    var rrPatches = new List<(Patch patch, string kind)>();

                    foreach (var p in info.Prefixes)
                        if (IsLikelyRRPatch(p)) rrPatches.Add((p, "PREFIX"));
                    foreach (var p in info.Postfixes)
                        if (IsLikelyRRPatch(p)) rrPatches.Add((p, "POSTFIX"));
                    foreach (var p in info.Transpilers)
                        if (IsLikelyRRPatch(p)) rrPatches.Add((p, "TRANSPILER"));

                    if (rrPatches.Count == 0) continue;

                    found++;
                    LogInfo($"[SurvivalTools] ResearchReinventedPatchLogger: RR-style patches on target -> {method.FullDescriptionSafe()}");

                    foreach (var (patch, kind) in rrPatches)
                    {
                        LogInfo($"  {kind}: {patch.PatchMethod?.FullDescriptionSafe() ?? "(unknown)"} (Owner={patch.owner}, Priority={patch.priority}, Index={patch.index})");
                    }
                }

                if (found == 0)
                    LogInfo("[SurvivalTools] ResearchReinventedPatchLogger: No RR-style patches found (owner/name filters).");
                else
                    LogInfo($"[SurvivalTools] ResearchReinventedPatchLogger: Found RR-style patches on {found} target(s).");
            }
            catch (Exception e)
            {
                LogError($"[SurvivalTools] ResearchReinventedPatchLogger: failed scanning all patches: {e}");
            }
        }

        private static bool IsLikelyRRPatch(Patch p)
        {
            if (p == null) return false;

            try
            {
                if (!string.IsNullOrEmpty(p.owner) &&
                    (p.owner.IndexOf("researchreinvented", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     p.owner.IndexOf("petetimesix", StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;

                var declType = p.PatchMethod?.DeclaringType;
                var asmName = declType?.Assembly?.GetName()?.Name ?? string.Empty;
                var typeFullName = declType?.FullName ?? string.Empty;

                if (!string.IsNullOrEmpty(asmName) &&
                    asmName.IndexOf("researchreinvented", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (!string.IsNullOrEmpty(typeFullName) &&
                    typeFullName.IndexOf("ResearchReinvented", StringComparison.OrdinalIgnoreCase) >= 0)
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
