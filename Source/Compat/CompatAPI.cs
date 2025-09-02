// RimWorld 1.6 / C# 7.3
// Source/Compat/CompatAPI.cs
//
// SurvivalTools Compatibility API (reflection-backed, resilient + delegate caching)
//
// Notes:
// - Attempts to bind delegates for common RR compat methods for speed.
// - Falls back to MethodInfo.Invoke with safe TargetInvocationException unwrapping.
// - Debug action available to dump which reflection names/methods/delegates succeeded or failed.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LudeonTK;
using RimWorld;
using Verse;

namespace SurvivalTools.Compat
{
    /// <summary>
    /// Public API for SurvivalTools mod compatibility features.
    /// This implementation tries to call into ResearchReinventedCompat using reflection
    /// and falls back to safe defaults if methods aren't present. Delegate caching
    /// is used where possible for better performance.
    /// </summary>
    public static class CompatAPI
    {
        #region Reflection / delegate caches

        // Type we're reflecting against
        private static readonly Type RRCompatType = typeof(ResearchReinventedCompat);

        // MethodInfo caches (null = not found)
        private static MethodInfo _mi_IsRRActive;
        private static MethodInfo _mi_PawnHasResearchTools;
        private static MethodInfo _mi_GetPawnResearchSpeedFromTools;
        private static MethodInfo _mi_GetPawnFieldResearchSpeedFromTools;
        private static MethodInfo _mi_ShouldOptimizeForResearchSpeed;
        private static MethodInfo _mi_ShouldOptimizeForFieldResearchSpeed;
        private static MethodInfo _mi_GetResearchSpeedStat;
        private static MethodInfo _mi_GetFieldResearchSpeedStat;
        private static MethodInfo _mi_IsRRWorkGiver;
        private static MethodInfo _mi_IsFieldResearchWorkGiver;

        // Delegate caches (fast path)
        private static Func<bool> _d_IsRRActive;
        private static Func<Pawn, bool> _d_PawnHasResearchTools;
        private static Func<Pawn, float> _d_GetPawnResearchSpeedFromTools_Float;
        private static Func<Pawn, double> _d_GetPawnResearchSpeedFromTools_Double; // fallback if RR returns double
        private static Func<Pawn, float> _d_GetPawnFieldResearchSpeedFromTools_Float;
        private static Func<Pawn, double> _d_GetPawnFieldResearchSpeedFromTools_Double;
        private static Func<bool> _d_ShouldOptimizeForResearchSpeed;
        private static Func<bool> _d_ShouldOptimizeForFieldResearchSpeed;
        private static Func<StatDef> _d_GetResearchSpeedStat;
        private static Func<StatDef> _d_GetFieldResearchSpeedStat;
        private static Func<WorkGiverDef, bool> _d_IsRRWorkGiver;
        private static Func<WorkGiverDef, bool> _d_IsFieldResearchWorkGiver;

        // Tracks resolution status for debug (name -> "OK:methodName" or error)
        private static readonly Dictionary<string, string> _reflectionStatus = new Dictionary<string, string>();

        // Ensure delegates/methodinfos attempted once (lazy init)
        private static bool _initialized = false;
        private static readonly object _initLock = new object();

        #endregion

        #region Initialization helpers

        // Find a static method on RRCompatType with optional param type(s) and optional return type.
        private static MethodInfo FindStaticMethod(string[] names, Type[] paramTypes = null, Type returnType = null)
        {
            if (RRCompatType == null) return null;

            var methods = RRCompatType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var name in names)
            {
                foreach (var mi in methods)
                {
                    if (!string.Equals(mi.Name, name, StringComparison.Ordinal)) continue;

                    // Check parameter types if requested
                    if (paramTypes != null)
                    {
                        var pars = mi.GetParameters().Select(p => p.ParameterType).ToArray();
                        if (pars.Length != paramTypes.Length) continue;
                        var ok = true;
                        for (int i = 0; i < pars.Length; i++)
                        {
                            if (pars[i] != paramTypes[i]) { ok = false; break; }
                        }
                        if (!ok) continue;
                    }

                    // Check return type if requested
                    if (returnType != null && mi.ReturnType != returnType) continue;

                    return mi;
                }
            }

            return null;
        }

        // Create delegate safely, returns null on failure
        private static Delegate TryCreateDelegate(MethodInfo mi, Type delegateType)
        {
            if (mi == null) return null;
            try { return Delegate.CreateDelegate(delegateType, mi); }
            catch { return null; }
        }

        // Unwrap TargetInvocationException and log inner exception; return null on failure
        private static object InvokeStatic(MethodInfo mi, params object[] args)
        {
            if (mi == null) return null;
            try
            {
                return mi.Invoke(null, args);
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException ?? tie;
                LogCompatWarning($"Reflection invocation of {mi.Name} threw: {inner.GetType().Name}: {inner.Message}");
                if (IsCompatLoggingEnabled && !string.IsNullOrEmpty(inner.StackTrace))
                    LogCompat($"StackTrace: {inner.StackTrace}");
                return null;
            }
            catch (Exception e)
            {
                LogCompatWarning($"Reflection invocation of {mi.Name} threw: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        // Do the one-time attempt to resolve MethodInfos and delegates
        private static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (_initLock)
            {
                if (_initialized) return;

                _reflectionStatus.Clear();

                try
                {
                    // IsRRActive (no params -> bool)
                    _mi_IsRRActive = FindStaticMethod(new[] { "IsRRActive" }, paramTypes: null, returnType: typeof(bool));
                    if (_mi_IsRRActive != null)
                    {
                        _d_IsRRActive = TryCreateDelegate(_mi_IsRRActive, typeof(Func<bool>)) as Func<bool>;
                        _reflectionStatus["IsRRActive"] = $"OK: {_mi_IsRRActive.Name} (delegate={(_d_IsRRActive != null ? "yes" : "no")})";
                    }
                    else _reflectionStatus["IsRRActive"] = "Missing";

                    // PawnHasResearchTools (Pawn -> bool)
                    _mi_PawnHasResearchTools = FindStaticMethod(
                        new[] { "PawnHasResearchTools", "PawnHasResearchTool", "PawnHasAnalysisTools", "PawnHasResearchToolForPawn" },
                        paramTypes: new[] { typeof(Pawn) }, returnType: typeof(bool));
                    if (_mi_PawnHasResearchTools != null)
                    {
                        _d_PawnHasResearchTools = TryCreateDelegate(_mi_PawnHasResearchTools, typeof(Func<Pawn, bool>)) as Func<Pawn, bool>;
                        _reflectionStatus["PawnHasResearchTools"] = $"OK: {_mi_PawnHasResearchTools.Name} (delegate={(_d_PawnHasResearchTools != null ? "yes" : "no")})";
                    }
                    else _reflectionStatus["PawnHasResearchTools"] = "Missing";

                    // GetPawnResearchSpeedFromTools (Pawn -> float/double)
                    _mi_GetPawnResearchSpeedFromTools = FindStaticMethod(
                        new[] { "GetPawnResearchSpeedFromTools", "GetPawnResearchSpeedFromToolsIfActive", "GetResearchSpeedFromTools" },
                        paramTypes: new[] { typeof(Pawn) });
                    if (_mi_GetPawnResearchSpeedFromTools != null)
                    {
                        _d_GetPawnResearchSpeedFromTools_Float = TryCreateDelegate(_mi_GetPawnResearchSpeedFromTools, typeof(Func<Pawn, float>)) as Func<Pawn, float>;
                        _d_GetPawnResearchSpeedFromTools_Double = TryCreateDelegate(_mi_GetPawnResearchSpeedFromTools, typeof(Func<Pawn, double>)) as Func<Pawn, double>;
                        _reflectionStatus["GetPawnResearchSpeedFromTools"] =
                            $"OK: {_mi_GetPawnResearchSpeedFromTools.Name} (delegateFloat={_d_GetPawnResearchSpeedFromTools_Float != null}, delegateDouble={_d_GetPawnResearchSpeedFromTools_Double != null})";
                    }
                    else _reflectionStatus["GetPawnResearchSpeedFromTools"] = "Missing";

                    // GetPawnFieldResearchSpeedFromTools (Pawn -> float/double)
                    _mi_GetPawnFieldResearchSpeedFromTools = FindStaticMethod(
                        new[] { "GetPawnFieldResearchSpeedFromTools", "GetFieldResearchSpeedFromTools" },
                        paramTypes: new[] { typeof(Pawn) });
                    if (_mi_GetPawnFieldResearchSpeedFromTools != null)
                    {
                        _d_GetPawnFieldResearchSpeedFromTools_Float = TryCreateDelegate(_mi_GetPawnFieldResearchSpeedFromTools, typeof(Func<Pawn, float>)) as Func<Pawn, float>;
                        _d_GetPawnFieldResearchSpeedFromTools_Double = TryCreateDelegate(_mi_GetPawnFieldResearchSpeedFromTools, typeof(Func<Pawn, double>)) as Func<Pawn, double>;
                        _reflectionStatus["GetPawnFieldResearchSpeedFromTools"] =
                            $"OK: {_mi_GetPawnFieldResearchSpeedFromTools.Name} (delegateFloat={_d_GetPawnFieldResearchSpeedFromTools_Float != null}, delegateDouble={_d_GetPawnFieldResearchSpeedFromTools_Double != null})";
                    }
                    else _reflectionStatus["GetPawnFieldResearchSpeedFromTools"] = "Missing";

                    // ShouldOptimizeForResearchSpeed (-> bool)
                    _mi_ShouldOptimizeForResearchSpeed = FindStaticMethod(
                        new[] { "ShouldOptimizeForResearchSpeed", "ShouldOptimizeForResearchSpeedIfActive" },
                        paramTypes: null, returnType: typeof(bool));
                    if (_mi_ShouldOptimizeForResearchSpeed != null)
                    {
                        _d_ShouldOptimizeForResearchSpeed = TryCreateDelegate(_mi_ShouldOptimizeForResearchSpeed, typeof(Func<bool>)) as Func<bool>;
                        _reflectionStatus["ShouldOptimizeForResearchSpeed"] = $"OK: {_mi_ShouldOptimizeForResearchSpeed.Name} (delegate={(_d_ShouldOptimizeForResearchSpeed != null ? "yes" : "no")})";
                    }
                    else _reflectionStatus["ShouldOptimizeForResearchSpeed"] = "Missing";

                    // ShouldOptimizeForFieldResearchSpeed (-> bool)
                    _mi_ShouldOptimizeForFieldResearchSpeed = FindStaticMethod(
                        new[] { "ShouldOptimizeForFieldResearchSpeed", "ShouldOptimizeForFieldResearchSpeedIfActive" },
                        paramTypes: null, returnType: typeof(bool));
                    if (_mi_ShouldOptimizeForFieldResearchSpeed != null)
                    {
                        _d_ShouldOptimizeForFieldResearchSpeed = TryCreateDelegate(_mi_ShouldOptimizeForFieldResearchSpeed, typeof(Func<bool>)) as Func<bool>;
                        _reflectionStatus["ShouldOptimizeForFieldResearchSpeed"] = $"OK: {_mi_ShouldOptimizeForFieldResearchSpeed.Name} (delegate={(_d_ShouldOptimizeForFieldResearchSpeed != null ? "yes" : "no")})";
                    }
                    else _reflectionStatus["ShouldOptimizeForFieldResearchSpeed"] = "Missing";

                    // GetResearchSpeedStat (-> StatDef)
                    _mi_GetResearchSpeedStat = FindStaticMethod(new[] { "GetResearchSpeedStat", "GetResearchSpeedStatIfActive" }, paramTypes: null);
                    if (_mi_GetResearchSpeedStat != null)
                    {
                        _d_GetResearchSpeedStat = TryCreateDelegate(_mi_GetResearchSpeedStat, typeof(Func<StatDef>)) as Func<StatDef>;
                        _reflectionStatus["GetResearchSpeedStat"] = $"OK: {_mi_GetResearchSpeedStat.Name} (delegate={(_d_GetResearchSpeedStat != null ? "yes" : "no")})";
                    }
                    else _reflectionStatus["GetResearchSpeedStat"] = "Missing";

                    // GetFieldResearchSpeedStat (-> StatDef)
                    _mi_GetFieldResearchSpeedStat = FindStaticMethod(new[] { "GetFieldResearchSpeedStat", "GetFieldResearchSpeedStatIfActive" }, paramTypes: null);
                    if (_mi_GetFieldResearchSpeedStat != null)
                    {
                        _d_GetFieldResearchSpeedStat = TryCreateDelegate(_mi_GetFieldResearchSpeedStat, typeof(Func<StatDef>)) as Func<StatDef>;
                        _reflectionStatus["GetFieldResearchSpeedStat"] = $"OK: {_mi_GetFieldResearchSpeedStat.Name} (delegate={(_d_GetFieldResearchSpeedStat != null ? "yes" : "no")})";
                    }
                    else _reflectionStatus["GetFieldResearchSpeedStat"] = "Missing";

                    // IsRRWorkGiver (WorkGiverDef -> bool)
                    _mi_IsRRWorkGiver = FindStaticMethod(
                        new[] { "IsRRWorkGiver", "IsResearchReinventedWorkGiver" },
                        paramTypes: new[] { typeof(WorkGiverDef) }, returnType: typeof(bool));
                    if (_mi_IsRRWorkGiver != null)
                    {
                        _d_IsRRWorkGiver = TryCreateDelegate(_mi_IsRRWorkGiver, typeof(Func<WorkGiverDef, bool>)) as Func<WorkGiverDef, bool>;
                        _reflectionStatus["IsRRWorkGiver"] = $"OK: {_mi_IsRRWorkGiver.Name} (delegate={(_d_IsRRWorkGiver != null ? "yes" : "no")})";
                    }
                    else _reflectionStatus["IsRRWorkGiver"] = "Missing";

                    // IsFieldResearchWorkGiver (WorkGiverDef -> bool)
                    _mi_IsFieldResearchWorkGiver = FindStaticMethod(
                        new[] { "IsFieldResearchWorkGiver" },
                        paramTypes: new[] { typeof(WorkGiverDef) }, returnType: typeof(bool));
                    if (_mi_IsFieldResearchWorkGiver != null)
                    {
                        _d_IsFieldResearchWorkGiver = TryCreateDelegate(_mi_IsFieldResearchWorkGiver, typeof(Func<WorkGiverDef, bool>)) as Func<WorkGiverDef, bool>;
                        _reflectionStatus["IsFieldResearchWorkGiver"] = $"OK: {_mi_IsFieldResearchWorkGiver.Name} (delegate={(_d_IsFieldResearchWorkGiver != null ? "yes" : "no")})";
                    }
                    else _reflectionStatus["IsFieldResearchWorkGiver"] = "Missing";
                }
                catch (Exception e)
                {
                    LogCompatWarning($"Compat reflection initialization failed: {e.Message}");
                }

                _initialized = true;
            }
        }

        #endregion

        #region Public API wrappers (use delegates when available, otherwise fallback)

        public static bool IsResearchReinventedActive
        {
            get
            {
                EnsureInitialized();
                try
                {
                    if (_d_IsRRActive != null) return _d_IsRRActive();

                    if (_mi_IsRRActive == null)
                        _mi_IsRRActive = FindStaticMethod(new[] { "IsRRActive" }, paramTypes: null, returnType: typeof(bool));

                    var res = InvokeStatic(_mi_IsRRActive);
                    if (res is bool b) return b;
                }
                catch (Exception e)
                {
                    LogCompatWarning($"IsResearchReinventedActive reflection call failed: {e.Message}");
                }
                return false;
            }
        }

        public static bool PawnHasResearchTools(Pawn pawn)
        {
            if (pawn == null) return false;
            EnsureInitialized();
            try
            {
                if (_d_PawnHasResearchTools != null)
                    return _d_PawnHasResearchTools(pawn);

                if (_mi_PawnHasResearchTools == null)
                    _mi_PawnHasResearchTools = FindStaticMethod(
                        new[] { "PawnHasResearchTools", "PawnHasResearchTool", "PawnHasAnalysisTools", "PawnHasResearchToolForPawn" },
                        paramTypes: new[] { typeof(Pawn) }, returnType: typeof(bool));

                var res = InvokeStatic(_mi_PawnHasResearchTools, pawn);
                if (res is bool b) return b;
            }
            catch (Exception e)
            {
                LogCompatWarning($"PawnHasResearchTools reflection call threw: {e.Message}");
            }
            return false;
        }

        public static float GetPawnResearchSpeedFromTools(Pawn pawn)
        {
            if (pawn == null) return 1.0f;
            EnsureInitialized();
            try
            {
                if (_d_GetPawnResearchSpeedFromTools_Float != null)
                    return _d_GetPawnResearchSpeedFromTools_Float(pawn);

                if (_d_GetPawnResearchSpeedFromTools_Double != null)
                    return (float)_d_GetPawnResearchSpeedFromTools_Double(pawn);

                if (_mi_GetPawnResearchSpeedFromTools == null)
                    _mi_GetPawnResearchSpeedFromTools = FindStaticMethod(
                        new[] { "GetPawnResearchSpeedFromTools", "GetPawnResearchSpeedFromToolsIfActive", "GetResearchSpeedFromTools" },
                        paramTypes: new[] { typeof(Pawn) });

                var res = InvokeStatic(_mi_GetPawnResearchSpeedFromTools, pawn);
                if (res is float f) return f;
                if (res is double d) return (float)d;
            }
            catch (Exception e)
            {
                LogCompatWarning($"GetPawnResearchSpeedFromTools reflection call threw: {e.Message}");
            }
            return 1.0f;
        }

        public static float GetPawnFieldResearchSpeedFromTools(Pawn pawn)
        {
            if (pawn == null) return 1.0f;
            EnsureInitialized();
            try
            {
                if (_d_GetPawnFieldResearchSpeedFromTools_Float != null)
                    return _d_GetPawnFieldResearchSpeedFromTools_Float(pawn);

                if (_d_GetPawnFieldResearchSpeedFromTools_Double != null)
                    return (float)_d_GetPawnFieldResearchSpeedFromTools_Double(pawn);

                if (_mi_GetPawnFieldResearchSpeedFromTools == null)
                    _mi_GetPawnFieldResearchSpeedFromTools = FindStaticMethod(
                        new[] { "GetPawnFieldResearchSpeedFromTools", "GetFieldResearchSpeedFromTools" },
                        paramTypes: new[] { typeof(Pawn) });

                var res = InvokeStatic(_mi_GetPawnFieldResearchSpeedFromTools, pawn);
                if (res is float f) return f;
                if (res is double d) return (float)d;
            }
            catch (Exception e)
            {
                LogCompatWarning($"GetPawnFieldResearchSpeedFromTools reflection call threw: {e.Message}");
            }
            return 1.0f;
        }

        public static bool ShouldOptimizeForResearchSpeed()
        {
            EnsureInitialized();
            try
            {
                if (_d_ShouldOptimizeForResearchSpeed != null)
                    return _d_ShouldOptimizeForResearchSpeed();

                if (_mi_ShouldOptimizeForResearchSpeed == null)
                    _mi_ShouldOptimizeForResearchSpeed = FindStaticMethod(
                        new[] { "ShouldOptimizeForResearchSpeed", "ShouldOptimizeForResearchSpeedIfActive" },
                        paramTypes: null, returnType: typeof(bool));

                var res = InvokeStatic(_mi_ShouldOptimizeForResearchSpeed);
                if (res is bool b) return b;
            }
            catch (Exception e)
            {
                LogCompatWarning($"ShouldOptimizeForResearchSpeed reflection call threw: {e.Message}");
            }
            return false;
        }

        public static bool ShouldOptimizeForFieldResearchSpeed()
        {
            EnsureInitialized();
            try
            {
                if (_d_ShouldOptimizeForFieldResearchSpeed != null)
                    return _d_ShouldOptimizeForFieldResearchSpeed();

                if (_mi_ShouldOptimizeForFieldResearchSpeed == null)
                    _mi_ShouldOptimizeForFieldResearchSpeed = FindStaticMethod(
                        new[] { "ShouldOptimizeForFieldResearchSpeed", "ShouldOptimizeForFieldResearchSpeedIfActive" },
                        paramTypes: null, returnType: typeof(bool));

                var res = InvokeStatic(_mi_ShouldOptimizeForFieldResearchSpeed);
                if (res is bool b) return b;
            }
            catch (Exception e)
            {
                LogCompatWarning($"ShouldOptimizeForFieldResearchSpeed reflection call threw: {e.Message}");
            }
            return false;
        }

        public static StatDef GetResearchSpeedStat()
        {
            EnsureInitialized();
            try
            {
                if (_d_GetResearchSpeedStat != null)
                    return _d_GetResearchSpeedStat();

                if (_mi_GetResearchSpeedStat == null)
                    _mi_GetResearchSpeedStat = FindStaticMethod(new[] { "GetResearchSpeedStat", "GetResearchSpeedStatIfActive" }, paramTypes: null);

                var res = InvokeStatic(_mi_GetResearchSpeedStat);
                return res as StatDef;
            }
            catch (Exception e)
            {
                LogCompatWarning($"GetResearchSpeedStat reflection call threw: {e.Message}");
            }
            return null;
        }

        public static StatDef GetFieldResearchSpeedStat()
        {
            EnsureInitialized();
            try
            {
                if (_d_GetFieldResearchSpeedStat != null)
                    return _d_GetFieldResearchSpeedStat();

                if (_mi_GetFieldResearchSpeedStat == null)
                    _mi_GetFieldResearchSpeedStat = FindStaticMethod(new[] { "GetFieldResearchSpeedStat", "GetFieldResearchSpeedStatIfActive" }, paramTypes: null);

                var res = InvokeStatic(_mi_GetFieldResearchSpeedStat);
                return res as StatDef;
            }
            catch (Exception e)
            {
                LogCompatWarning($"GetFieldResearchSpeedStat reflection call threw: {e.Message}");
            }
            return null;
        }

        public static bool IsRRWorkGiver(WorkGiverDef workGiver)
        {
            if (workGiver == null) return false;
            EnsureInitialized();
            try
            {
                if (_d_IsRRWorkGiver != null)
                    return _d_IsRRWorkGiver(workGiver);

                if (_mi_IsRRWorkGiver == null)
                    _mi_IsRRWorkGiver = FindStaticMethod(
                        new[] { "IsRRWorkGiver", "IsResearchReinventedWorkGiver" },
                        paramTypes: new[] { typeof(WorkGiverDef) }, returnType: typeof(bool));

                var res = InvokeStatic(_mi_IsRRWorkGiver, workGiver);
                if (res is bool b) return b;
            }
            catch (Exception e)
            {
                LogCompatWarning($"IsRRWorkGiver reflection call threw: {e.Message}");
            }
            return false;
        }

        public static bool IsFieldResearchWorkGiver(WorkGiverDef workGiver)
        {
            if (workGiver == null) return false;
            EnsureInitialized();
            try
            {
                if (_d_IsFieldResearchWorkGiver != null)
                    return _d_IsFieldResearchWorkGiver(workGiver);

                if (_mi_IsFieldResearchWorkGiver == null)
                    _mi_IsFieldResearchWorkGiver = FindStaticMethod(
                        new[] { "IsFieldResearchWorkGiver" },
                        paramTypes: new[] { typeof(WorkGiverDef) }, returnType: typeof(bool));

                var res = InvokeStatic(_mi_IsFieldResearchWorkGiver, workGiver);
                if (res is bool b) return b;
            }
            catch (Exception e)
            {
                LogCompatWarning($"IsFieldResearchWorkGiver reflection call threw: {e.Message}");
            }
            return false;
        }

        #endregion

        #region Compatibility aggregation helpers

        /// <summary>
        /// Return all compatibility-related StatDefs known from active compat layers.
        /// Currently aggregates Research Reinvented stats (if available). Future mods
        /// can be added here.
        /// </summary>
        public static List<StatDef> GetAllCompatibilityStats()
        {
            var all = new List<StatDef>();

            // ResearchReinvented stats
            all.AddRange(GetAllResearchStats());

            // Future: append stats from other compatibility modules here.

            return all.Distinct().ToList();
        }

        /// <summary>
        /// Helper: gather RR research-related stats (ResearchSpeed, FieldResearchSpeedMultiplier).
        /// </summary>
        public static List<StatDef> GetAllResearchStats()
        {
            var stats = new List<StatDef>();
            var research = GetResearchSpeedStat();
            if (research != null) stats.Add(research);
            var fieldResearch = GetFieldResearchSpeedStat();
            if (fieldResearch != null) stats.Add(fieldResearch);
            return stats;
        }

        #endregion

        #region Debug action & status dump

        /// <summary>
        /// Debug action to dump reflection binding status and delegate creation results.
        /// NOTE: using simple positional DebugAction overload for RimWorld 1.6 compatibility.
        /// </summary>
        [DebugAction("SurvivalTools", "Dump compat reflection status")]
        public static void DumpCompatReflectionStatus()
        {
            EnsureInitialized();

            Log.Message("[SurvivalTools] Compat reflection status dump START");
            foreach (var kv in _reflectionStatus)
                Log.Message($"[SurvivalTools] CompatStatus: {kv.Key} => {kv.Value}");

            // Additionally show if delegates exist for quick view
            Log.Message($"[SurvivalTools] CompatDelegates: IsRRActive={(_d_IsRRActive != null)}, PawnHasResearchTools={(_d_PawnHasResearchTools != null)}, GetResearchSpeedFromTools_float={(_d_GetPawnResearchSpeedFromTools_Float != null)}, GetResearchSpeedFromTools_double={(_d_GetPawnResearchSpeedFromTools_Double != null)}");
            Log.Message($"[SurvivalTools] CompatDelegates: GetFieldResearchSpeedFromTools_float={(_d_GetPawnFieldResearchSpeedFromTools_Float != null)}, GetFieldResearchSpeedFromTools_double={(_d_GetPawnFieldResearchSpeedFromTools_Double != null)}");
            Log.Message($"[SurvivalTools] CompatDelegates: ShouldOptimizeResearch={(_d_ShouldOptimizeForResearchSpeed != null)}, ShouldOptimizeFieldResearch={(_d_ShouldOptimizeForFieldResearchSpeed != null)}");
            Log.Message($"[SurvivalTools] CompatDelegates: GetResearchSpeedStat={(_d_GetResearchSpeedStat != null)}, GetFieldResearchSpeedStat={(_d_GetFieldResearchSpeedStat != null)}");
            Log.Message($"[SurvivalTools] CompatDelegates: IsRRWorkGiver={(_d_IsRRWorkGiver != null)}, IsFieldResearchWorkGiver={(_d_IsFieldResearchWorkGiver != null)}");

            Log.Message("[SurvivalTools] Compat reflection status dump COMPLETE");
        }

        #endregion

        #region Utility / logging

        public static void LogCompat(string message)
        {
            if (SurvivalTools.Settings?.debugLogging == true && SurvivalTools.Settings?.compatLogging == true)
                Log.Message($"[SurvivalTools Compat] {message}");
        }

        public static void LogCompatWarning(string message)
        {
            // Always warn; compat issues are important even when compatLogging is off
            Log.Warning($"[SurvivalTools Compat] {message}");
        }

        public static void LogCompatError(string message)
        {
            Log.Error($"[SurvivalTools Compat] {message}");
        }

        public static bool IsCompatLoggingEnabled =>
            SurvivalTools.Settings?.debugLogging == true && SurvivalTools.Settings?.compatLogging == true;

        #endregion
    }
}
