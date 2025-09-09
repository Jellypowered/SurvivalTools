// RimWorld 1.6 / C# 7.3
// Source/Compat/ResearchReinvented/RRReflectionAPI.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using SurvivalTools.Helpers; // StatGatingHelper
using static SurvivalTools.ST_Logging;
using Verse.AI;

namespace SurvivalTools.Compat
{
    /// <summary>
    /// Single surface for RR detection + reflection targets + tool-gated helpers.
    /// </summary>
    public static class RRReflectionAPI
    {
        // ---- detection ------------------------------------------------------

        private static bool? _isActiveCached;
        public static bool IsRRActive
        {
            get
            {
                if (_isActiveCached.HasValue) return _isActiveCached.Value;

                // packageId is stable across RR versions
                bool byPkg = ModLister.GetActiveModWithIdentifier("PeteTimesSix.ResearchReinvented", true) != null;

                // type fallback to be extra safe
                bool byType = AccessTools.TypeByName("PeteTimesSix.ResearchReinvented.Utilities.PawnExtensions") != null;

                _isActiveCached = byPkg || byType;
                return _isActiveCached.Value;
            }
        }

        // Inside RRReflectionAPI (top-level, not in Extensions or Postfixes)
        public static void Initialize()
        {
            if (!IsRRActive) return;

            try
            {
                var h = new Harmony("SurvivalTools.Compat.ResearchReinvented");
                ApplyHarmonyHooks(h);

#if DEBUG
                if (IsCompatLogging() && IsDebugLoggingEnabled)
                    Log.Message("[SurvivalTools Compat] RRReflectionAPI.Initialize() completed.");
#endif
            }
            catch (Exception e)
            {
                Log.Warning($"[SurvivalTools Compat] RRReflectionAPI.Initialize() failed: {e}");
            }
        }

        /// <summary>
        /// Return basic reflection/debug status so CompatAPI can dump info.
        /// </summary>
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
                var postfix = new HarmonyMethod(typeof(RR_Postfixes), nameof(RR_Postfixes.CanEverDoResearch_Postfix));
                h.Patch(_miCanEverDoResearch, postfix: postfix);
            }

            if (_miCanNowDoResearch != null)
            {
                var postfix = new HarmonyMethod(typeof(RR_Postfixes), nameof(RR_Postfixes.CanNowDoResearch_Postfix));
                h.Patch(_miCanNowDoResearch, postfix: postfix);
            }
        }

        // ---- tool helpers (called by postfixes) -----------------------------

        // --- extra API surface restored for AutoToolIntegration / RuntimeIntegration ---

        public static class RRReflectionAPI_Extensions
        {
            /// <summary>
            /// Resolve a WorkGiverDef for a given Job or JobDef.
            /// </summary>
            public static WorkGiverDef ResolveWorkGiverForJob(Job job)
            {
                return job?.def != null
                    ? JobDefToWorkGiverDefHelper.GetWorkGiverDefForJob(job.def)
                    : null;
            }

            public static WorkGiverDef ResolveWorkGiverForJob(JobDef jobDef)
            {
                return JobDefToWorkGiverDefHelper.GetWorkGiverDefForJob(jobDef);
            }

            /// <summary>
            /// Get the relevant stats Survival Tools cares about for this job/workgiver.
            /// </summary>
            public static List<StatDef> GetRequiredStatsForWorkGiver(WorkGiverDef wgd, Job job = null)
            {
                if (wgd == null) return new List<StatDef>();

                // Explicit overload selection to avoid ambiguity
                if (job != null)
                    return SurvivalToolUtility.RelevantStatsFor(wgd, job);
                else
                    return SurvivalToolUtility.RelevantStatsFor(wgd, (JobDef)null);
            }

            /// <summary>
            /// Is this a RR research workgiver?
            /// </summary>
            public static bool IsRRWorkGiver(WorkGiverDef wgd)
            {
                if (wgd == null) return false;
                string defName = wgd.defName.ToLower();
                return defName.Contains("research") && !defName.Contains("field");
            }

            /// <summary>
            /// Is this a RR field research workgiver?
            /// </summary>
            public static bool IsFieldResearchWorkGiver(WorkGiverDef wgd)
            {
                if (wgd == null) return false;
                string defName = wgd.defName.ToLower();
                return defName.Contains("fieldresearch") || defName.Contains("survey");
            }

            public static bool PawnHasResearchTool(Pawn pawn)
            {
                if (pawn == null || pawn.Dead || pawn.Destroyed) return false;
                // Keep ST’s rule: Research should NOT hard-block normal jobs.
                // We only check tools to gate RR’s *progress* mechanics.
                return pawn.HasSurvivalToolFor(ST_StatDefOf.ResearchSpeed);
            }

            public static bool HardcoreBlocksResearchProgress
            {
                get
                {
                    var s = SurvivalTools.Settings;
                    return s != null && (s.hardcoreMode || s.extraHardcoreMode);
                }
            }

            // When we *don’t* want to fully block research activity (job can run),
            // we still want RR to grant 0 progress. We implement this by letting
            // Can*( ) stay TRUE, but making our StatPart drive ResearchSpeed to ~0
            // when no tool is present (your current StatPart already does that).
            //
            // The postfixes below only flip the bool to FALSE when the user
            // explicitly enables “hard block RR progress” via hardcore/extra-hardcore.
            // This preserves your original “jobs still run, no progress” goal by default.

            internal static bool ShouldRRReturnFalseWhenNoTool()
            {
                // In extra-hardcore we *do* block RR (strict mode).
                return SurvivalTools.Settings?.extraHardcoreMode == true;
            }
        }

        // ---- postfix container --------------------------------------------------

        internal static class RR_Postfixes
        {
            // cooldown to avoid spammy logs on tick checks
            private static readonly Dictionary<int, int> _lastLoggedTick = new Dictionary<int, int>();

            private static bool CanLogNow(Pawn p, int cooldownTicks = 120)
            {
                if (!ST_Logging.IsDebugLoggingEnabled) return false;
                int now = Find.TickManager.TicksGame;
                int id = p?.thingIDNumber ?? -1;
                if (id < 0) return false;

                if (_lastLoggedTick.TryGetValue(id, out int last) && now - last < cooldownTicks)
                    return false;

                _lastLoggedTick[id] = now;
                return true;
            }

            // signature matches RR’s static bool PawnExtensions.CanEverDoResearch(Pawn)
            public static void CanEverDoResearch_Postfix(Pawn __0, ref bool __result)
            {
                if (!__result || __0 == null) return; // already false or no pawn

                // Don’t force-false in normal hardcore; allow job to run w/ zero progress via StatPart.
                if (!CompatAPI.PawnHasResearchTools(__0) && RRReflectionAPI.RRReflectionAPI_Extensions.ShouldRRReturnFalseWhenNoTool())
                {
                    __result = false; // strict: RR considers pawn unable to research *ever* (tool missing)
                    if (CanLogNow(__0))
                        Log.Message($"[SurvivalTools RR] CanEverDoResearch=false for {__0.LabelShort} (missing research tool, extra-hardcore).");
                }
            }

            // signature matches RR’s static bool PawnExtensions.CanNowDoResearch(Pawn)
            public static void CanNowDoResearch_Postfix(Pawn __0, ref bool __result)
            {
                if (!__result || __0 == null) return; // RR already decided 'no'

                // In normal hardcore: leave TRUE so jobs can run; StatPart makes progress ~0.
                // In extra-hardcore: flip to FALSE so RR refuses to grant progress right now.
                if (!CompatAPI.PawnHasResearchTools(__0) && RRReflectionAPI.RRReflectionAPI_Extensions.ShouldRRReturnFalseWhenNoTool())
                {
                    __result = false;
                    if (CanLogNow(__0))
                        Log.Message($"[SurvivalTools RR] CanNowDoResearch=false for {__0.LabelShort} (missing research tool, extra-hardcore).");
                }
            }
        }
    }
}
