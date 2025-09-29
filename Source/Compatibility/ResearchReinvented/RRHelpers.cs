// RimWorld 1.6 / C# 7.3
// Source/Compatibility/ResearchReinvented/RRHelpers.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Compat.ResearchReinvented
{
    // Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs

    public static class RRHelpers
    {
        // ---- detection cache ------------------------------------------------
        private static bool? _isActiveCached;
        // Idempotent initialization guard so Harmony hooks & logging only run once.
        private static bool _initialized;

        // RR mode snapshot (derived from SurvivalTools difficulty settings). We do not cache aggressively
        // because the underlying settings can change at runtime (player toggles Hardcore/Nightmare).
        public enum RRMode
        {
            Off,       // RR not present or compat disabled
            Normal,    // Tools provide bonus; penalty applied through RRStatPart (x0.6 default) when missing tool
            Hardcore,  // Research allowed but progress is zero without research tool (soft block)
            Nightmare  // Research jobs hard blocked without research tool (no job assignment)
        }

        public static RRMode CurrentMode
        {
            get { return Mode(); }
        }
        public static bool IsRRActive
        {
            get
            {
                if (_isActiveCached.HasValue) return _isActiveCached.Value;

                bool byPkg = ModLister.GetActiveModWithIdentifier("PeteTimesSix.ResearchReinvented", true) != null;
                bool byType = AccessTools.TypeByName("PeteTimesSix.ResearchReinvented.Utilities.PawnExtensions") != null;
                _isActiveCached = byPkg || byType;
                return _isActiveCached.Value;
            }
        }

        // ---- reflection targets --------------------------------------------
        private static Type _pawnExtensionsType;
        private static MethodInfo _miCanEverDoResearch;
        private static MethodInfo _miCanNowDoResearch;

        private static bool TryResolve()
        {
            if (_pawnExtensionsType != null) return true;

            _pawnExtensionsType = AccessTools.TypeByName("PeteTimesSix.ResearchReinvented.Utilities.PawnExtensions");
            if (_pawnExtensionsType == null) return false;

            _miCanEverDoResearch = AccessTools.Method(_pawnExtensionsType, "CanEverDoResearch", new[] { typeof(Pawn) });
            _miCanNowDoResearch = AccessTools.Method(_pawnExtensionsType, "CanNowDoResearch", new[] { typeof(Pawn) });

            return _miCanEverDoResearch != null || _miCanNowDoResearch != null;
        }

        public static void Initialize(Harmony h)
        {
            if (!IsRRActive) return; // RR not present
            if (_initialized) return; // Already initialized (idempotent)
            _initialized = true;
            try
            {
                ApplyHarmonyHooks(h);
                if (IsCompatLogging() && IsDebugLoggingEnabled)
                    Log.Message("[SurvivalTools Compat] RRHelpers.Initialize() completed (external harmony instance).");
                // Lightweight smoke (dev builds / compat logging only) so we know active RR mode
                if (ShouldLogModeOnce())
                {
                    try { Log.Message("[SurvivalTools Compat][RR] Active RR mode: " + Mode()); } catch { }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[SurvivalTools Compat] RRHelpers.Initialize() failed: {e}");
            }
        }

        private static bool _modeLogged;
        private static bool ShouldLogModeOnce()
        {
            if (_modeLogged) return false;
            if (!(Prefs.DevMode && SurvivalToolsMod.Settings?.debugLogging == true) && !IsCompatLogging()) return false;
            _modeLogged = true; return true;
        }

        public static Dictionary<string, string> GetReflectionStatus()
        {
            var info = new Dictionary<string, string>
            {
                ["IsActive"] = IsRRActive.ToString(),
                ["PawnExtensionsType"] = _pawnExtensionsType?.FullName ?? "null",
                ["HasCanEver"] = (_miCanEverDoResearch != null).ToString(),
                ["HasCanNow"] = (_miCanNowDoResearch != null).ToString()
            };

            return info;
        }

        public static void ApplyHarmonyHooks(Harmony h)
        {
            if (h == null) return;
            if (!IsRRActive) return;
            if (!TryResolve())
            {
                Log.Warning("[SurvivalTools Compat] RR types/methods not found; skipping RR patches.");
                return;
            }

            if (_miCanEverDoResearch != null)
            {
                var postfix = new HarmonyMethod(typeof(RRPostfixes), nameof(RRPostfixes.CanEverDoResearch_Postfix));
                h.Patch(_miCanEverDoResearch, postfix: postfix);
            }

            if (_miCanNowDoResearch != null)
            {
                var postfix = new HarmonyMethod(typeof(RRPostfixes), nameof(RRPostfixes.CanNowDoResearch_Postfix));
                h.Patch(_miCanNowDoResearch, postfix: postfix);
            }
        }

        // ---- runtime adapters ------------------------------------------------
        public static class Runtime
        {
            public static bool PawnHasResearchTools(Pawn p) => CompatAPI.PawnHasResearchTools(p);

            public static List<StatDef> RequiredResearchStatsFor(WorkGiverDef wgd, Job jobOrNull)
            {
                if (wgd == null) return new List<StatDef>();
                if (jobOrNull != null)
                    return SurvivalToolUtility.RelevantStatsFor(wgd, jobOrNull);
                return SurvivalToolUtility.RelevantStatsFor(wgd, (JobDef)null);
            }
        }

        // ---- helper queries used by patches ---------------------------------
        public static WorkGiverDef ResolveWorkGiverForJob(Job job)
            => job?.def != null ? JobDefToWorkGiverDefHelper.GetWorkGiverDefForJob(job.def) : null;

        public static WorkGiverDef ResolveWorkGiverForJob(JobDef jobDef)
            => JobDefToWorkGiverDefHelper.GetWorkGiverDefForJob(jobDef);

        public static List<StatDef> GetRequiredStatsForWorkGiver(WorkGiverDef wgd, Job job = null)
        {
            if (wgd == null) return new List<StatDef>();
            if (job != null) return SurvivalToolUtility.RelevantStatsFor(wgd, job);
            return SurvivalToolUtility.RelevantStatsFor(wgd, (JobDef)null);
        }

        public static bool IsRRWorkGiver(WorkGiverDef wgd)
        {
            if (wgd == null) return false;
            string defName = wgd.defName.ToLower();
            return defName.Contains("research") && !defName.Contains("field");
        }

        public static bool IsFieldResearchWorkGiver(WorkGiverDef wgd)
        {
            if (wgd == null) return false;
            string defName = wgd.defName.ToLower();
            return defName.Contains("fieldresearch") || defName.Contains("survey");
        }

        public static bool RequiresResearchTools(WorkGiverDef wgDef)
        {
            try
            {
                var ext = wgDef.GetModExtension<WorkGiverExtension>();
                if (ext == null || ext.requiredStats.NullOrEmpty()) return false;

                var researchStat = CompatAPI.GetResearchSpeedStat() ?? ST_StatDefOf.ResearchSpeed;
                if (researchStat != null && ext.requiredStats.Contains(researchStat)) return true;

                var fieldStat = CompatAPI.GetFieldResearchSpeedStat() ??
                                DefDatabase<StatDef>.GetNamedSilentFail("FieldResearchSpeedMultiplier");
                if (fieldStat != null && ext.requiredStats.Contains(fieldStat)) return true;
            }
            catch { }
            return false;
        }

        // ---- RR settings bridge ---------------------------------------------
        public static class Settings
        {
            public static bool IsRRCompatibilityEnabled
            {
                get
                {
                    if (!IsRRActive) return false;
                    return SurvivalToolsMod.Settings?.enableRRCompatibility ?? true;
                }
            }

            public static bool TreatResearchAsRequiredInExtraHardcore => SurvivalToolsMod.Settings?.rrResearchRequiredInExtraHardcore ?? false;
            public static bool TreatFieldResearchAsRequiredInExtraHardcore => SurvivalToolsMod.Settings?.rrFieldResearchRequiredInExtraHardcore ?? false;

            public static bool IsRRStatRequiredInExtraHardcore(StatDef stat)
            {
                if (!IsRRCompatibilityEnabled || stat == null) return false;
                if (SurvivalToolsMod.Settings?.extraHardcoreMode != true) return false;
                if (stat == CompatAPI.GetResearchSpeedStat()) return TreatResearchAsRequiredInExtraHardcore;
                if (stat == CompatAPI.GetFieldResearchSpeedStat()) return TreatFieldResearchAsRequiredInExtraHardcore;
                return false;
            }

            public static bool IsRRStatOptional(StatDef stat)
            {
                if (!IsRRCompatibilityEnabled || stat == null) return false;
                return stat == CompatAPI.GetResearchSpeedStat() || stat == CompatAPI.GetFieldResearchSpeedStat();
            }
        }

        // ---- AutoTool helpers (thin, cached by defName) ----------------------
        private static readonly Dictionary<string, List<StatDef>> _requiredStatsCache = new Dictionary<string, List<StatDef>>();

        public static List<StatDef> GetRequiredStatsForWorkGiverCached(WorkGiverDef wgd, Job jobOrNull)
        {
            if (wgd == null) return new List<StatDef>();
            var key = wgd.defName + (jobOrNull?.def?.defName ?? "");
            if (_requiredStatsCache.TryGetValue(key, out var cached)) return cached;
            var stats = GetRequiredStatsForWorkGiver(wgd, jobOrNull) ?? new List<StatDef>();
            _requiredStatsCache[key] = stats;
            return stats;
        }

        public static void ClearCachesOnDefsChanged()
        {
            _requiredStatsCache.Clear();
            _isActiveCached = null;
            _pawnExtensionsType = null;
            _miCanEverDoResearch = null;
            _miCanNowDoResearch = null;
        }

        // ---- postfixes ------------------------------------------------------
        internal static class RRPostfixes
        {
            // Use the deduplicated tool-gate logger to avoid spamming ResearchReinvented's frequent checks.
            // Keyed by pawn+job/stat, the logger immediately emits the first denial and suppresses repeats for
            // a short window, then prints a summary when suppressed counts exist.
            public static void CanEverDoResearch_Postfix(Pawn __0, ref bool __result)
            {
                if (!__result || __0 == null) return;
                if (ShouldHardBlockResearch(__0))
                {
                    __result = false;
                    try
                    {
                        // Use ResearchSpeed stat where available
                        var stat = CompatAPI.GetResearchSpeedStat() ?? ST_StatDefOf.ResearchSpeed;
                        ST_Logging.LogToolGateEvent(__0, null, stat, "missing research tool (RR CanEverDoResearch)");
                    }
                    catch { }
                }
            }

            public static void CanNowDoResearch_Postfix(Pawn __0, ref bool __result)
            {
                if (!__result || __0 == null) return;
                if (ShouldHardBlockResearch(__0))
                {
                    __result = false;
                    try
                    {
                        var stat = CompatAPI.GetResearchSpeedStat() ?? ST_StatDefOf.ResearchSpeed;
                        ST_Logging.LogToolGateEvent(__0, null, stat, "missing research tool (RR CanNowDoResearch)");
                    }
                    catch { }
                }
            }
        }

        // ---- New API (spec-conform) -----------------------------------------
        public static bool IsActive() => IsRRActive && Settings.IsRRCompatibilityEnabled;

        public static RRMode Mode()
        {
            if (!IsRRActive || !Settings.IsRRCompatibilityEnabled) return RRMode.Off;
            var sts = SurvivalToolsMod.Settings;
            if (sts == null) return RRMode.Normal;
            if (sts.extraHardcoreMode) return RRMode.Nightmare;
            if (sts.hardcoreMode) return RRMode.Hardcore;
            return RRMode.Normal;
        }

        public static bool IsHumanColonist(Pawn p)
        {
            if (p == null) return false;
            if (!p.RaceProps?.Humanlike ?? true) return false;
            if (p.RaceProps.IsMechanoid) return false;
            if (p.Faction == null || !p.Faction.IsPlayer) return false;
            return true;
        }

        public static bool PawnHasResearchTool(Pawn p)
        {
            if (!IsHumanColonist(p)) return false;
            try
            {
                // Prefer CompatAPI (already optimized / cached in mod)
                if (CompatAPI.PawnHasResearchTools(p)) return true;
            }
            catch { }
            // Fallback lightweight heuristic: check currently held survival tool improving ResearchSpeed > 0
            try
            {
                var stat = CompatAPI.GetResearchSpeedStat() ?? ST_StatDefOf.ResearchSpeed;
                if (stat == null) return false;
                var eq = p.equipment?.Primary; // Survival tools may not be primary; iterate inventory tools as needed
                if (eq != null && eq.def?.statBases != null)
                {
                    // Minimal: if any stat modification exists treat as having tool
                    if (eq.GetStatValue(stat, true) > p.GetStatValue(stat) * 1.01f) return true; // improvement threshold 1%
                }
                // Scan survival tool assignments (if any API exists) – omitted for brevity in fallback
            }
            catch { }
            return false;
        }

        public static bool ShouldHardBlockResearch(Pawn p)
            => Mode() == RRMode.Nightmare && IsHumanColonist(p) && !PawnHasResearchTool(p);

        public static bool ShouldZeroProgress(Pawn p)
            => Mode() == RRMode.Hardcore && IsHumanColonist(p) && !PawnHasResearchTool(p);

        public static bool ShouldApplyNormalPenalty()
            => Mode() == RRMode.Normal;

        public static float NoToolPenalty()
        {
            // Default 0.6f (x0.6 multiplier) per spec. Could later expose dedicated setting; fallback to spec value.
            const float defaultPenalty = 0.6f;
            return defaultPenalty;
        }

        // Legacy internal helpers retained for existing code paths ----------------
        internal static bool ShouldHardBlockResearch() { return Mode() == RRMode.Nightmare; }
        internal static bool ShouldZeroProgress_Internal(Pawn p) => ShouldZeroProgress(p);
        internal static float GetNormalPenaltyFactor() => NoToolPenalty();

        // ================== Extended explicit research gating (Nightmare refinement) ==================
        private static readonly HashSet<string> _rrAsmNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "researchreinvented" };
        private static Type _wgResearcherType = typeof(WorkGiver_Researcher);
        private static readonly Dictionary<WorkGiverDef, bool> _benchWGCache = new Dictionary<WorkGiverDef, bool>();

        public static bool IsRRBenchResearchWG(WorkGiverDef wgd, WorkGiver scanner = null)
        {
            if (!IsActive()) return false;
            if (wgd == null) return false;
            bool cached;
            if (_benchWGCache.TryGetValue(wgd, out cached)) return cached;
            bool res = false;
            try
            {
                // Vanilla researcher
                Type wc = null;
                try
                {
                    var fi = typeof(WorkGiverDef).GetField("workerClass", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (fi != null) wc = fi.GetValue(wgd) as Type;
                    if (wc == null)
                    {
                        var fi2 = typeof(WorkGiverDef).GetField("workerClassInt", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (fi2 != null) wc = fi2.GetValue(wgd) as Type;
                    }
                }
                catch { }
                if (wc == _wgResearcherType || (_wgResearcherType != null && wc != null && _wgResearcherType.IsAssignableFrom(wc))) res = true;
                // Name heuristic
                if (!res)
                {
                    var dn = wgd.defName ?? string.Empty;
                    if (dn.IndexOf("research", StringComparison.OrdinalIgnoreCase) >= 0 && dn.IndexOf("field", StringComparison.OrdinalIgnoreCase) < 0)
                        res = true;
                }
                // Assembly heuristic (RR custom scanners)
                if (!res && scanner != null)
                {
                    var asmName = scanner.GetType().Assembly?.GetName()?.Name ?? string.Empty;
                    foreach (var an in _rrAsmNames)
                    {
                        if (asmName.IndexOf(an, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var tName = scanner.GetType().Name ?? string.Empty;
                            if (tName.IndexOf("Research", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                res = true; break;
                            }
                        }
                    }
                }
            }
            catch { }
            _benchWGCache[wgd] = res;
            return res;
        }

        public static bool IsExplicitResearchContext(Pawn pawn, Thing target, WorkGiverDef wgd)
        {
            if (!IsActive()) return false;
            try
            {
                if (pawn == null) return false;
                if (wgd != null && IsRRBenchResearchWG(wgd)) return true;
                // Target bench heuristic: building def name contains Research & has ResearchSpeed stat influence
                var b = target as Building;
                if (b != null)
                {
                    var dn = b.def?.defName ?? string.Empty;
                    if (dn.IndexOf("research", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            catch { }
            return false;
        }

        public static bool ShouldHardBlockBenchResearch(Pawn p)
            => Mode() == RRMode.Nightmare && IsHumanColonist(p) && !PawnHasResearchTool(p);

        public static bool ShouldZeroRRProgress(Pawn p)
            => (Mode() == RRMode.Hardcore || Mode() == RRMode.Nightmare) && IsHumanColonist(p) && !PawnHasResearchTool(p);
    }
}

// Bridge namespace requested by spec (non-breaking). Provides identical API forwarding to existing implementation.
namespace SurvivalTools.Compat.RR
{
    using SurvivalTools.Compat.ResearchReinvented;
    using Verse;

    public static class RRHelpers
    {
        public static bool IsActive() => ResearchReinvented.RRHelpers.IsActive();
        public static ResearchReinvented.RRHelpers.RRMode Mode() => ResearchReinvented.RRHelpers.Mode();
        public static bool PawnHasResearchTool(Pawn p) => ResearchReinvented.RRHelpers.PawnHasResearchTool(p);
        public static bool IsHumanColonist(Pawn p) => ResearchReinvented.RRHelpers.IsHumanColonist(p);
        public static bool ShouldHardBlockResearch(Pawn p) => ResearchReinvented.RRHelpers.ShouldHardBlockResearch(p);
        public static bool ShouldZeroProgress(Pawn p) => ResearchReinvented.RRHelpers.ShouldZeroProgress(p);
        public static bool ShouldApplyNormalPenalty() => ResearchReinvented.RRHelpers.ShouldApplyNormalPenalty();
        public static float NoToolPenalty() => ResearchReinvented.RRHelpers.NoToolPenalty();
    }
}
