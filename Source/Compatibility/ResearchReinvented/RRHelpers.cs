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

        public static void Initialize()
        {
            if (!IsRRActive) return;

            try
            {
                var h = new Harmony("SurvivalTools.Compat.ResearchReinvented");
                ApplyHarmonyHooks(h);

                if (IsCompatLogging() && IsDebugLoggingEnabled)
                    Log.Message("[SurvivalTools Compat] RRHelpers.Initialize() completed.");
            }
            catch (Exception e)
            {
                Log.Warning($"[SurvivalTools Compat] RRHelpers.Initialize() failed: {e}");
            }
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
                if (!CompatAPI.PawnHasResearchTools(__0) && ShouldRRReturnFalseWhenNoTool())
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
                if (!CompatAPI.PawnHasResearchTools(__0) && ShouldRRReturnFalseWhenNoTool())
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

        internal static bool ShouldRRReturnFalseWhenNoTool() => SurvivalToolsMod.Settings?.extraHardcoreMode == true;
    }
}
