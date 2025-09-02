// RimWorld 1.6 / C# 7.3
// Source/Compat/ResearchReinventedCompat.cs
//
// Research Reinvented Compatibility Layer (updated)
// - runtime toggles
// - skip-if-RR-already-patched behavior
// - debug/dump of applied patches
// - #region blocks for easy navigation

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SurvivalTools.Compat
{
    [StaticConstructorOnStartup]
    internal static class ResearchReinventedCompat
    {
        #region Constants

        private const string RR_PACKAGE_ID = "PeteTimesSix.ResearchReinvented";
        private const string RR_OWNER_HINT = "researchreinvented";
        private const string RR_AUTHOR_HINT = "petetimesix";

        #endregion

        #region Runtime configuration toggles

        // if false, don't apply blocking behavior even if RR is present (also checked in prefixes)
        public static bool EnableRRIntegration = true;

        // when true, prints extra compat logs even if global compat logging is off
        public static bool DebugPatchLogging = false;

        #endregion

        #region Known RR WorkGivers / tags / tools

        private static readonly string[] RRWorkGiverDefNames = {
            "RR_Analyse", "RR_AnalyseInPlace", "RR_AnalyseTerrain", "RR_ResearchRR", "RR_LearnRemotely"
        };

        private static readonly string[] FieldResearchWorkGiverDefNames = {
            "RR_AnalyseTerrain"
        };

        private static readonly string[] ResearchToolDefNames = {
            "SurvivalTools_Abacus", "SurvivalTools_Microscope"
        };

        private static readonly string[] ResearchTags = { "research", "study" };

        #endregion

        #region Harmony & state

        private static readonly Harmony harmony = new Harmony("SurvivalTools.Compat.RR");

        // keep track of methods we actually patched (for debug dump)
        private static readonly List<MethodBase> _patchedupTargets = new List<MethodBase>();

        // ensure RR detection only logs once per session
        private static bool _hasLoggedRRDetection = false;

        #endregion

        #region Initialization

        static ResearchReinventedCompat()
        {
            LogCompat("=== Research Reinvented Compatibility Setup START ===");

            try
            {
                LogCompat("RR Compat: Checking if Research Reinvented is active...");
                if (!IsRRActive())
                {
                    LogCompat("RR Compat: Research Reinvented not detected, skipping compatibility setup");
                    return;
                }

                LogCompat("RR Compat: Research Reinvented detected! Proceeding with compatibility setup...");

                LogCompat("RR Compat: Starting WorkGiver wiring...");
                int wiredCount = WireWorkGiversToResearchTools();
                LogCompat($"RR Compat: WorkGiver wiring complete - {wiredCount} tools wired (attempted)");

                if (!EnableRRIntegration)
                {
                    LogCompat("RR Compat: EnableRRIntegration=false, skipping Harmony patching.");
                    LogCompat("=== Research Reinvented Compatibility Setup COMPLETE (patching disabled) ===");
                    return;
                }

                LogCompat("RR Compat: Starting Harmony patching...");
                int patchedCount = TryPatchRRChecks();
                LogCompat($"RR Compat: Harmony patching complete - {patchedCount} patches applied/skipped");

                LogCompat($"RR Compat: === SUMMARY === wired Abacus/Microscope to {wiredCount} tools, applied/skipped {patchedCount} patches.");
                LogCompat("=== Research Reinvented Compatibility Setup COMPLETE ===");
            }
            catch (Exception e)
            {
                LogCompatError("RR Compat: init failed: " + e);
                LogCompatError("RR Compat: Stack trace: " + e.StackTrace);
            }
        }

        #endregion

        #region Detection

        public static bool IsRRActive()
        {
            try
            {
                // Prefer ModLister method if available
                var mod = ModLister.GetActiveModWithIdentifier(RR_PACKAGE_ID);
                if (mod != null)
                {
                    if (!_hasLoggedRRDetection)
                    {
                        LogCompat($"RR Compat: Found RR mod via ModLister: {mod.Name}");
                        _hasLoggedRRDetection = true;
                    }
                    return true;
                }

                // fallback to active mods list
                var activeMods = ModsConfig.ActiveModsInLoadOrder.ToList();
                foreach (var m in activeMods)
                {
                    if (m?.PackageId != null && m.PackageId.Equals(RR_PACKAGE_ID, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }
            catch (Exception e)
            {
                LogCompatWarning($"RR Compat: IsRRActive() failed: {e.Message}");
                return false;
            }
        }

        #endregion

        #region WG wiring

        private static int WireWorkGiversToResearchTools()
        {
            LogCompat("RR Compat: WireWorkGiversToResearchTools() starting...");

            // try known RR WorkGiver defNames
            var rrWGs = RRWorkGiverDefNames
                .Select(id => DefDatabase<WorkGiverDef>.GetNamedSilentFail(id))
                .Where(wg => wg != null)
                .ToList();

            // fallback to dynamic discovery if none found
            if (rrWGs.Count == 0)
            {
                var allWorkGivers = DefDatabase<WorkGiverDef>.AllDefsListForReading;
                rrWGs = allWorkGivers
                    .Where(wg => wg.giverClass?.Namespace?.Contains("ResearchReinvented") == true
                              || wg.giverClass?.Name?.IndexOf("Analy", StringComparison.OrdinalIgnoreCase) >= 0
                              || wg.giverClass?.Name?.IndexOf("Research", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            if (rrWGs.Count == 0)
            {
                LogCompatWarning("RR Compat: no RR WorkGiverDefs found; skipping WG wiring.");
                return 0;
            }

            int wiredCount = 0;
            foreach (var toolDefName in ResearchToolDefNames)
            {
                var toolDef = DefDatabase<ThingDef>.GetNamedSilentFail(toolDefName);
                if (toolDef == null)
                {
                    LogCompatWarning($"RR Compat: research tool {toolDefName} not found, skipping.");
                    continue;
                }

                var props = toolDef.GetModExtension<SurvivalToolProperties>();
                if (props != null)
                {
                    if (props.defaultSurvivalToolAssignmentTags == null)
                        props.defaultSurvivalToolAssignmentTags = new List<string>();

                    foreach (var tag in ResearchTags)
                    {
                        if (!props.defaultSurvivalToolAssignmentTags.Contains(tag))
                            props.defaultSurvivalToolAssignmentTags.Add(tag);
                    }
                }
                else
                {
                    LogCompatWarning($"RR Compat: Tool {toolDef.defName} missing SurvivalToolProperties!");
                }

                // wire to workgivers by adding WorkGiverExtension and stats
                if (WireToolToWorkGivers(toolDef, rrWGs))
                    wiredCount++;
            }

            EnsureResearchToolsInAssignments();

            return wiredCount;
        }

        private static bool WireToolToWorkGivers(ThingDef toolDef, List<WorkGiverDef> workGivers)
        {
            var props = toolDef.GetModExtension<SurvivalToolProperties>();
            int successCount = 0;
            int alreadyConfiguredCount = 0;

            foreach (var wgDef in workGivers)
            {
                var wgExt = wgDef.GetModExtension<WorkGiverExtension>();
                if (wgExt == null)
                {
                    if (wgDef.modExtensions == null)
                        wgDef.modExtensions = new List<DefModExtension>();

                    wgExt = new WorkGiverExtension();
                    wgDef.modExtensions.Add(wgExt);
                }

                if (wgExt.requiredStats == null)
                    wgExt.requiredStats = new List<StatDef>();

                var researchSpeedStat = DefDatabase<StatDef>.GetNamedSilentFail("ResearchSpeed");
                if (researchSpeedStat != null)
                {
                    if (!wgExt.requiredStats.Contains(researchSpeedStat))
                    {
                        wgExt.requiredStats.Add(researchSpeedStat);
                        successCount++;
                    }
                    else alreadyConfiguredCount++;
                }

                if (FieldResearchWorkGiverDefNames.Contains(wgDef.defName))
                {
                    var fieldResearch = DefDatabase<StatDef>.GetNamedSilentFail("FieldResearchSpeedMultiplier");
                    if (fieldResearch != null && !wgExt.requiredStats.Contains(fieldResearch))
                    {
                        wgExt.requiredStats.Add(fieldResearch);
                        successCount++;
                    }
                    else if (fieldResearch != null)
                    {
                        alreadyConfiguredCount++;
                    }
                }

                // Add any other "research*" stats that require survival tool usage
                var researchStats = DefDatabase<StatDef>.AllDefsListForReading
                    .Where(s => s.defName.IndexOf("research", StringComparison.OrdinalIgnoreCase) >= 0 && s.RequiresSurvivalTool())
                    .ToList();

                foreach (var st in researchStats)
                {
                    if (!wgExt.requiredStats.Contains(st))
                    {
                        wgExt.requiredStats.Add(st);
                        successCount++;
                    }
                }
            }

            bool success = successCount > 0 || alreadyConfiguredCount > 0;
            LogCompat($"RR Compat: WireToolToWorkGivers() - success={success}, new={successCount}, already={alreadyConfiguredCount}");
            return success;
        }

        private static void EnsureResearchToolsInAssignments()
        {
            var database = Current.Game?.GetComponent<SurvivalToolAssignmentDatabase>();
            if (database == null) return;

            var assignments = database.AllSurvivalToolAssignments;
            if (assignments?.Any() != true) return;

            var anythingAssignment = assignments.FirstOrDefault(a =>
                a.label.ToLowerInvariant().Contains("anything") ||
                a.label.ToLowerInvariant().Contains("general"));

            if (anythingAssignment == null) return;

            int addedCount = 0;
            foreach (var toolDefName in ResearchToolDefNames)
            {
                var toolDef = DefDatabase<ThingDef>.GetNamedSilentFail(toolDefName);
                if (toolDef != null && !anythingAssignment.filter.Allows(toolDef))
                {
                    anythingAssignment.filter.SetAllow(toolDef, true);
                    addedCount++;
                }
            }

            LogCompat($"RR Compat: Added {addedCount} research tools to assignments");
        }

        #endregion

        #region Patch application

        private static int TryPatchRRChecks()
        {
            int patchedCount = 0;

            try
            {
                // Patch PawnExtensions methods to OR result with SurvivalTools (so RR recognizes our tools)
                patchedCount += TryPatchPawnExtensionMethod(
                    "PeteTimesSix.ResearchReinvented.Utilities.PawnExtensions",
                    "CanEverDoResearch",
                    typeof(Action));

                patchedCount += TryPatchPawnExtensionMethod(
                    "PeteTimesSix.ResearchReinvented.Utilities.PawnExtensions",
                    "CanNowDoResearch",
                    typeof(Action));

                // Patch WorkGiver_Scanner base HasJobOnThing/HasJobOnCell (declared on base class)
                var wgScannerType = typeof(WorkGiver_Scanner);
                var hasJobOnThing = wgScannerType.GetMethod("HasJobOnThing", BindingFlags.Public | BindingFlags.Instance);
                var hasJobOnCell = wgScannerType.GetMethod("HasJobOnCell", BindingFlags.Public | BindingFlags.Instance);

                if (hasJobOnThing != null)
                {
                    if (ShouldApplyPatchToMethod(hasJobOnThing))
                    {
                        var prefix = new HarmonyMethod(typeof(ResearchReinventedCompat)
                            .GetMethod(nameof(Prefix_WorkGiverScanner_HasJobOnThing), BindingFlags.NonPublic | BindingFlags.Static));
                        harmony.Patch(hasJobOnThing, prefix: prefix);
                        _patchedupTargets.Add(hasJobOnThing);
                        patchedCount++;
                        LogCompat("Compat RR: patched WorkGiver_Scanner.HasJobOnThing (base)");
                    }
                    else LogCompat("Compat RR: skipped patching WorkGiver_Scanner.HasJobOnThing because RR-like patch present");
                }

                if (hasJobOnCell != null)
                {
                    if (ShouldApplyPatchToMethod(hasJobOnCell))
                    {
                        var prefix = new HarmonyMethod(typeof(ResearchReinventedCompat)
                            .GetMethod(nameof(Prefix_WorkGiverScanner_HasJobOnCell), BindingFlags.NonPublic | BindingFlags.Static));
                        harmony.Patch(hasJobOnCell, prefix: prefix);
                        _patchedupTargets.Add(hasJobOnCell);
                        patchedCount++;
                        LogCompat("Compat RR: patched WorkGiver_Scanner.HasJobOnCell (base)");
                    }
                    else LogCompat("Compat RR: skipped patching WorkGiver_Scanner.HasJobOnCell because RR-like patch present");
                }
            }
            catch (Exception e)
            {
                LogCompatWarning($"Compat RR: Harmony patching failed: {e.Message}");
            }

            return patchedCount;
        }

        private static int TryPatchPawnExtensionMethod(string typeName, string methodName, Type _)
        {
            try
            {
                var targetType = Type.GetType($"{typeName}, ResearchReinvented");
                if (targetType == null)
                {
                    LogCompat($"Compat RR: type {typeName} not found");
                    return 0;
                }

                var m = targetType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (m == null)
                {
                    LogCompat($"Compat RR: method {methodName} not found in {typeName}");
                    return 0;
                }

                if (!ShouldApplyPatchToMethod(m))
                {
                    LogCompat($"Compat RR: skipped patching {typeName}.{methodName} because RR-like patch present");
                    return 0;
                }

                var postfix = new HarmonyMethod(typeof(ResearchReinventedCompat)
                    .GetMethod(nameof(Postfix_PawnHasTool_OR), BindingFlags.Static | BindingFlags.NonPublic));
                harmony.Patch(m, postfix: postfix);
                _patchedupTargets.Add(m);
                LogCompat($"Compat RR: successfully patched {typeName}.{methodName}");
                return 1;
            }
            catch (Exception e)
            {
                LogCompat($"Compat RR: failed to patch {typeName}.{methodName}: {e.Message}");
                return 0;
            }
        }

        private static bool ShouldApplyPatchToMethod(MethodBase target)
        {
            try
            {
                var info = Harmony.GetPatchInfo(target);
                if (info == null) return true;

                IEnumerable<HarmonyLib.Patch> all =
                    (info.Prefixes ?? Enumerable.Empty<HarmonyLib.Patch>())
                    .Concat(info.Postfixes ?? Enumerable.Empty<HarmonyLib.Patch>())
                    .Concat(info.Transpilers ?? Enumerable.Empty<HarmonyLib.Patch>());

                foreach (var p in all)
                {
                    if (p == null) continue;

                    if (!string.IsNullOrEmpty(p.owner) &&
                       (p.owner.IndexOf(RR_OWNER_HINT, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        p.owner.IndexOf(RR_AUTHOR_HINT, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        if (DebugPatchLogging) LogCompat($"Compat RR: skipping patch target {target.FullDescription()} because existing patch owner looks like RR ({p.owner})");
                        return false;
                    }

                    var declType = p.PatchMethod?.DeclaringType;
                    var asmName = declType?.Assembly?.GetName()?.Name ?? "";
                    var fullName = declType?.FullName ?? "";

                    if (asmName.IndexOf("researchreinvented", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (DebugPatchLogging) LogCompat($"Compat RR: skipping patch target {target.FullDescription()} because patch assembly looks like RR ({asmName})");
                        return false;
                    }

                    if (fullName.IndexOf("ResearchReinvented", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (DebugPatchLogging) LogCompat($"Compat RR: skipping patch target {target.FullDescription()} because patch type looks like RR ({fullName})");
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                LogCompatWarning($"ShouldApplyPatchToMethod check failed: {e.Message}");
            }

            return true;
        }

        #endregion

        #region Patched prefix/postfix implementations

        // Postfix for RR PawnExtensions.CanEverDoResearch / CanNowDoResearch
        private static void Postfix_PawnHasTool_OR(ref bool __result, Pawn pawn)
        {
            if (__result) return;           // already allowed
            if (pawn == null) return;       // let original semantics stand

            if (PawnHasResearchTool(pawn))
            {
                __result = true;
                LogCompat($"Compat RR: OR override true for {pawn.LabelShort} via SurvivalTools research tool");
            }
        }

        // Prefix for WorkGiver_Scanner.HasJobOnThing (declared on base class)
        private static bool Prefix_WorkGiverScanner_HasJobOnThing(WorkGiver_Scanner __instance, Pawn pawn, Thing t, ref bool __result)
        {
            // allow runtime disable without needing to unpatch
            if (!EnableRRIntegration) return true;

            // only enforce in hardcore or extra-hardcore modes & for valid pawns
            bool enforce = SurvivalToolUtility.IsHardcoreModeEnabled || (SurvivalTools.Settings?.extraHardcoreMode == true);
            if (!enforce || !ShouldGateResearchForPawn(pawn)) return true;

            var extension = __instance?.def?.GetModExtension<WorkGiverExtension>();
            if (extension == null || extension.requiredStats == null) return true;

            var fieldResearchStat = DefDatabase<StatDef>.GetNamedSilentFail("FieldResearchSpeedMultiplier");
            bool requiresResearch = extension.requiredStats.Contains(ST_StatDefOf.ResearchSpeed);
            bool requiresFieldResearch = fieldResearchStat != null && extension.requiredStats.Contains(fieldResearchStat);

            if ((requiresResearch || requiresFieldResearch) && !PawnHasResearchTool(pawn))
            {
                __result = false;
                LogCompat($"Compat RR: Blocking job {__instance?.def?.defName} for {pawn.LabelShort} - no research tool");
                return false; // skip original
            }

            return true; // continue original
        }

        // Prefix for WorkGiver_Scanner.HasJobOnCell (declared on base class)
        private static bool Prefix_WorkGiverScanner_HasJobOnCell(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, ref bool __result)
        {
            if (!EnableRRIntegration) return true;

            bool enforce = SurvivalToolUtility.IsHardcoreModeEnabled || (SurvivalTools.Settings?.extraHardcoreMode == true);
            if (!enforce || !ShouldGateResearchForPawn(pawn)) return true;

            var extension = __instance?.def?.GetModExtension<WorkGiverExtension>();
            if (extension == null || extension.requiredStats == null) return true;

            var fieldResearchStat = DefDatabase<StatDef>.GetNamedSilentFail("FieldResearchSpeedMultiplier");
            bool requiresResearch = extension.requiredStats.Contains(ST_StatDefOf.ResearchSpeed);
            bool requiresFieldResearch = fieldResearchStat != null && extension.requiredStats.Contains(fieldResearchStat);

            if ((requiresResearch || requiresFieldResearch) && !PawnHasResearchTool(pawn))
            {
                __result = false;
                LogCompat($"Compat RR: Blocking cell job {__instance?.def?.defName} for {pawn.LabelShort} - no research tool");
                return false;
            }

            return true;
        }

        #endregion

        #region Public helper API

        private static bool ShouldGateResearchForPawn(Pawn pawn)
        {
            if (pawn == null) return false;
            if (pawn.Dead || pawn.Destroyed || pawn.Map == null) return false;
            if (!(pawn.RaceProps?.Humanlike ?? false)) return false;     // skip animals
            if (!(pawn.Faction?.IsPlayer ?? false)) return false;        // only our colonists
            if (pawn.WorkTagIsDisabled(WorkTags.Intellectual)) return false;
            if (!pawn.CanUseSurvivalTools()) return false;               // your existing gate
            return true;
        }

        public static bool PawnHasResearchTool(Pawn p)
        {
            if (p == null) return false;
            try
            {
                foreach (var thing in p.GetAllUsableSurvivalTools())
                {
                    if (IsResearchTool(thing)) return true;
                }
            }
            catch { /* swallow */ }
            return false;
        }

        private static bool IsResearchTool(Thing t)
        {
            if (t?.def == null) return false;
            if (ResearchToolDefNames.Contains(t.def.defName)) return true;

            var props = t.def.GetModExtension<SurvivalToolProperties>();
            if (props?.defaultSurvivalToolAssignmentTags != null &&
                props.defaultSurvivalToolAssignmentTags.Any(tag => ResearchTags.Contains(tag)))
                return true;

            var st = t as SurvivalTool;
            if (st != null)
            {
                foreach (var mod in st.WorkStatFactors)
                {
                    if (mod?.stat?.defName != null &&
                        mod.stat.defName.IndexOf("research", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }

            return false;
        }

        public static void DumpPatchedMethods()
        {
            try
            {
                LogCompat("ResearchReinventedCompat: DumpPatchedMethods start");
                if (_patchedupTargets.Count == 0)
                {
                    LogCompat("ResearchReinventedCompat: No methods patched by SurvivalTools compat module.");
                    return;
                }

                foreach (var m in _patchedupTargets)
                {
                    var info = Harmony.GetPatchInfo(m);
                    LogCompat($"Target -> {m.FullDescription()}");
                    if (info != null)
                    {
                        foreach (var p in info.Prefixes) LogCompat($"  PREFIX: {p.PatchMethod.FullDescriptionSafe()} (owner={p.owner}, priority={p.priority})");
                        foreach (var p in info.Postfixes) LogCompat($"  POSTFIX: {p.PatchMethod.FullDescriptionSafe()} (owner={p.owner}, priority={p.priority})");
                        foreach (var p in info.Transpilers) LogCompat($"  TRANSPILER: {p.PatchMethod.FullDescriptionSafe()} (owner={p.owner}, priority={p.priority})");
                    }
                    else
                    {
                        LogCompat("  (no Harmony patch info available)");
                    }
                }
            }
            catch (Exception e)
            {
                LogCompatError($"DumpPatchedMethods failed: {e}");
            }
        }

        #endregion

        #region Logging helpers

        private static void LogCompat(string message)
        {
            if (SurvivalTools.Settings?.debugLogging == true && SurvivalTools.Settings?.compatLogging == true)
                Log.Message($"[SurvivalTools] Compat RR: {message}");
            else if (DebugPatchLogging)
                Log.Message($"[SurvivalTools] Compat RR (debug): {message}");
        }

        private static void LogCompatWarning(string message)
        {
            if (SurvivalTools.Settings?.debugLogging == true && SurvivalTools.Settings?.compatLogging == true)
                Log.Warning($"[SurvivalTools] Compat RR: {message}");
            else if (DebugPatchLogging)
                Log.Warning($"[SurvivalTools] Compat RR (debug): {message}");
        }

        private static void LogCompatError(string message)
        {
            Log.Error($"[SurvivalTools] Compat RR: {message}");
        }

        #endregion
    }

    #region Reflection helper (pretty method descriptions)

    internal static class ReflectionExtensions
    {
        public static string FullDescription(this MethodBase mb)
        {
            if (mb == null) return "(null)";
            try
            {
                var decl = mb.DeclaringType != null ? mb.DeclaringType.FullName : "(no type)";
                var mi = mb as MethodInfo;
                if (mi != null)
                {
                    var parms = string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
                    return $"{decl}.{mi.Name}({parms}) -> {mi.ReturnType.Name}";
                }
                return $"{decl}.{mb.Name}";
            }
            catch { return mb.ToString(); }
        }

        public static string FullDescriptionSafe(this MethodBase mb) => FullDescription(mb);
    }

    #endregion
}
