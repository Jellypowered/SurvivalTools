// RimWorld 1.6 / C# 7.3
// Source/Compatibility/ResearchReinvented/RRPatches.cs
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Compat.ResearchReinvented
{
    // Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs
    internal static class RRPatches
    {
        // --- Nightmare skip log dedupe (15s cooldown) -----------------------
        private struct SkipLogEntry { public int lastTick; public int suppressed; }
        private static readonly System.Collections.Generic.Dictionary<string, SkipLogEntry> _skipLog = new System.Collections.Generic.Dictionary<string, SkipLogEntry>();
        private const int SkipLogCooldownTicks = 900; // ~15s at 60 ticks/sec

        private static void MaybeLogNightmareSkip(Pawn pawn, WorkGiver wg, Type typeWhenNoWG)
        {
            if (!(Prefs.DevMode && SurvivalToolsMod.Settings?.debugLogging == true)) return; // only in debug
            if (pawn == null) return;
            string wgLabel = wg?.def?.defName ?? typeWhenNoWG?.Name ?? "(unknown WG)";
            int tick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            string key = pawn.ThingID + "|" + wgLabel;
            SkipLogEntry entry;
            if (_skipLog.TryGetValue(key, out entry))
            {
                if (tick - entry.lastTick < SkipLogCooldownTicks)
                {
                    entry.suppressed++;
                    _skipLog[key] = entry;
                    return; // still cooling down
                }
                // Cooldown elapsed – emit with suppressed count (if any)
                var suffix = entry.suppressed > 0 ? $" (+{entry.suppressed} suppressed)" : string.Empty;
                LogCompat($"RR Nightmare: skipping WG {wgLabel} for {pawn.LabelShort} (no research tool){suffix}");
                entry.lastTick = tick;
                entry.suppressed = 0;
                _skipLog[key] = entry;
            }
            else
            {
                LogCompat($"RR Nightmare: skipping WG {wgLabel} for {pawn.LabelShort} (no research tool)");
                _skipLog[key] = new SkipLogEntry { lastTick = tick, suppressed = 0 };
            }
        }
        internal static void Init(Harmony h)
        {
            try
            {
                if (!CompatAPI.IsResearchReinventedActive)
                {
                    if (IsCompatLogging()) LogCompat("RR gating: Research Reinvented not detected skipping patches.");
                    return;
                }

                if (IsCompatLogging()) LogCompat("RR gating: Research Reinvented detected, applying patches.");

                // Patch RR pawn extension methods (postfixes are applied by RRHelpers.Initialize which calls ApplyHarmonyHooks)
                RRHelpers.Initialize(h);

                // PHASE 12 FIX: Patch the actual RR progress methods (ResearchOpportunity.ResearchPerformed)
                // This is the CENTRAL chokepoint for ALL RR research progress
                try
                {
                    var rrOpportunityType = AccessTools.TypeByName("PeteTimesSix.ResearchReinvented.Opportunities.ResearchOpportunity");
                    if (rrOpportunityType != null)
                    {
                        // Main progress method - ALL RR progress goes through here
                        var miResearchPerformed = AccessTools.Method(rrOpportunityType, "ResearchPerformed", new[] {
                            typeof(float),  // amount
                            typeof(Pawn),   // researcher
                            typeof(float?), // moteAmount
                            typeof(string), // moteSubjectName
                            typeof(float)   // moteOffsetHint
                        });

                        if (miResearchPerformed != null)
                        {
                            h.Patch(miResearchPerformed, prefix: new HarmonyMethod(typeof(RRPatches), nameof(Prefix_ResearchOpportunity_ResearchPerformed)));
                            if (IsCompatLogging()) LogCompat("RR gating: Patched ResearchOpportunity.ResearchPerformed (main progress method)");
                        }

                        // Backup: Tick-based progress
                        var miResearchTickPerformed = AccessTools.Method(rrOpportunityType, "ResearchTickPerformed");
                        if (miResearchTickPerformed != null)
                        {
                            h.Patch(miResearchTickPerformed, prefix: new HarmonyMethod(typeof(RRPatches), nameof(Prefix_ResearchOpportunity_ResearchTickPerformed)));
                            if (IsCompatLogging()) LogCompat("RR gating: Patched ResearchOpportunity.ResearchTickPerformed (tick method)");
                        }

                        // Backup: Chunk-based progress (one-time events)
                        var miResearchChunkPerformed = AccessTools.Method(rrOpportunityType, "ResearchChunkPerformed");
                        if (miResearchChunkPerformed != null)
                        {
                            h.Patch(miResearchChunkPerformed, prefix: new HarmonyMethod(typeof(RRPatches), nameof(Prefix_ResearchOpportunity_ResearchChunkPerformed)));
                            if (IsCompatLogging()) LogCompat("RR gating: Patched ResearchOpportunity.ResearchChunkPerformed (chunk method)");
                        }
                    }
                    else
                    {
                        LogCompatWarning("RR gating: Could not find ResearchOpportunity type - RR progress will not be gated!");
                    }
                }
                catch (Exception e)
                {
                    LogCompatWarning($"RR gating: failed to patch ResearchOpportunity methods: {e}");
                }

                // Legacy patch (kept for compatibility, but RR doesn't use this)
                try
                {
                    var rmType = typeof(ResearchManager);
                    var miResearchPerformed = AccessTools.Method(rmType, nameof(ResearchManager.ResearchPerformed), new[] { typeof(float), typeof(Pawn) });
                    if (miResearchPerformed != null)
                    {
                        h.Patch(miResearchPerformed, prefix: new HarmonyMethod(typeof(RRPatches), nameof(Prefix_ResearchManager_ResearchPerformed)));
                    }
                }
                catch (Exception e)
                {
                    LogCompatWarning($"RR gating: failed to patch ResearchManager.ResearchPerformed: {e}");
                }

                // Dynamic discovery: patch RR award / progress utility methods (once) to zero side-channel research gains.
                try { DiscoverAndPatchRRAwardMethods(h); } catch (Exception exDisc) { if (IsCompatLogging()) LogCompatWarning($"RR gating: award discovery failed: {exDisc.Message}"); }

                // Nightmare hard-gating: patch the DECLARED virtual on WorkGiver.ShouldSkip(...)
                // and filter inside the prefix to RR scanners or vanilla WorkGiver_Researcher.
                try
                {
                    var baseWG = typeof(WorkGiver);
                    var miShouldSkip = AccessTools.Method(baseWG, nameof(WorkGiver.ShouldSkip), new[] { typeof(Pawn), typeof(bool) });
                    if (miShouldSkip != null)
                    {
                        h.Patch(miShouldSkip, prefix: new HarmonyMethod(typeof(RRPatches), nameof(Prefix_WorkGiver_ShouldSkip)));
                    }
                }
                catch (Exception e)
                {
                    LogCompatWarning($"RR gating: failed to patch WorkGiver.ShouldSkip: {e}");
                }

                // (Legacy) WorkGiver_Scanner.HasJobOnThing/Cell gating prefixes retired; rely on JobGate + stat parts.

                // Patch recipe toils so research/crafting progress respects tool tiers (defensive)
                var toilsType = AccessTools.TypeByName("Toils_Recipe");
                if (toilsType != null)
                {
                    var doWork = AccessTools.Method(toilsType, "DoRecipeWork");
                    if (doWork != null)
                    {
                        try
                        {
                            h.Patch(doWork, prefix: new HarmonyMethod(typeof(RRPatches), nameof(Prefix_Toils_Recipe_DoRecipeWork)));
                        }
                        catch (Exception e)
                        {
                            LogCompatWarning($"RR gating: failed to patch Toils_Recipe.DoRecipeWork: {e}");
                        }
                    }

                    var makeUnfinished = AccessTools.Method(toilsType, "MakeUnfinishedThingIfNeeded");
                    if (makeUnfinished != null)
                    {
                        try
                        {
                            h.Patch(makeUnfinished, prefix: new HarmonyMethod(typeof(RRPatches), nameof(Prefix_Toils_Recipe_MakeUnfinishedThingIfNeeded)));
                        }
                        catch (Exception e)
                        {
                            LogCompatWarning($"RR gating: failed to patch Toils_Recipe.MakeUnfinishedThingIfNeeded: {e}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"[SurvivalTools Compat] RR gating patch init failed: {e}");
            }
        }

        // ========== PHASE 12 FIX: Core RR Progress Patches ==========

        /// <summary>
        /// Main patch for RR progress - ALL research progress goes through ResearchOpportunity.ResearchPerformed
        /// This is the single chokepoint for research, field research, analysis, everything.
        /// </summary>
        private static void Prefix_ResearchOpportunity_ResearchPerformed(ref float amount, Pawn researcher)
        {
            try
            {
                if (!RRHelpers.IsActive()) return;
                if (researcher == null) return;

                // Check if pawn has research tool - block ALL progress when lacking tool
                if (!RRHelpers.PawnHasResearchTool(researcher))
                {
                    if (amount > 0f)
                    {
                        float originalAmount = amount;
                        amount = 0f;

                        if (Prefs.DevMode && SurvivalToolsMod.Settings?.debugLogging == true)
                            LogCompat($"RR Progress BLOCKED: {researcher.LabelShort} (no research tool, {originalAmount:F2} → 0)");
                    }
                }
            }
            catch (Exception e)
            {
                LogCompatWarning($"Prefix_ResearchOpportunity_ResearchPerformed error: {e}");
            }
        }

        /// <summary>
        /// Backup patch for tick-based progress (called during work ticks)
        /// In Nightmare mode, skip the entire method to prevent any processing
        /// </summary>
        private static bool Prefix_ResearchOpportunity_ResearchTickPerformed(Pawn researcher)
        {
            try
            {
                if (!RRHelpers.IsActive()) return true;
                if (researcher == null) return true;

                // In Nightmare mode, block the entire tick if lacking tool
                if (RRHelpers.Mode() == RRHelpers.RRMode.Nightmare)
                {
                    if (!RRHelpers.PawnHasResearchTool(researcher))
                    {
                        return false; // Skip method entirely
                    }
                }

                // In other modes, allow (ResearchPerformed prefix will zero the amount)
                return true;
            }
            catch (Exception e)
            {
                LogCompatWarning($"Prefix_ResearchOpportunity_ResearchTickPerformed error: {e}");
                return true;
            }
        }

        /// <summary>
        /// Backup patch for chunk-based progress (one-time events like analyzing items)
        /// Zero the amount before it reaches ResearchPerformed
        /// </summary>
        private static void Prefix_ResearchOpportunity_ResearchChunkPerformed(ref float amount, Pawn researcher)
        {
            try
            {
                if (!RRHelpers.IsActive()) return;
                if (researcher == null) return;

                if (!RRHelpers.PawnHasResearchTool(researcher))
                {
                    if (amount > 0f)
                    {
                        float originalAmount = amount;
                        amount = 0f;

                        if (Prefs.DevMode && SurvivalToolsMod.Settings?.debugLogging == true)
                            LogCompat($"RR Chunk BLOCKED: {researcher.LabelShort} (no research tool, {originalAmount:F2} → 0)");
                    }
                }
            }
            catch (Exception e)
            {
                LogCompatWarning($"Prefix_ResearchOpportunity_ResearchChunkPerformed error: {e}");
            }
        }

        // ========== Legacy/Defensive Patches ==========

        // Defensive prefix for Toils_Recipe.DoRecipeWork – adjust or block progress when research tools are required
        private static bool Prefix_Toils_Recipe_DoRecipeWork(object __instance, JobDriver __state)
        {
            try
            {
                if (!RRHelpers.IsActive()) return true;

                // Attempt to locate the pawn performing the toil
                Pawn pawn = null;
                try { pawn = (Pawn)AccessTools.Field(__instance.GetType(), "actor").GetValue(__instance); } catch { }
                if (pawn == null) return true;

                // Resolve the job/workgiver context
                Job job = pawn.CurJob;
                var wgd = RRHelpers.ResolveWorkGiverForJob(job);

                // Required stats for RR-sensitive workgivers
                var required = RRHelpers.GetRequiredStatsForWorkGiverCached(wgd, job);
                if (required == null || required.Count == 0) return true;

                // If in extra-hardcore RR mode and pawn lacks research tools, block
                if (SurvivalToolsMod.Settings?.extraHardcoreMode == true && RRHelpers.Settings.IsRRCompatibilityEnabled)
                {
                    foreach (var st in required)
                    {
                        if (RRHelpers.Settings.IsRRStatRequiredInExtraHardcore(st) && !CompatAPI.PawnHasResearchTools(pawn))
                        {
                            if (IsCompatLogging()) LogCompat($"Blocking recipe/research toil for {pawn.LabelShort}: missing RR research tool for stat {st.defName}.");
                            return false; // skip original toil
                        }
                    }
                }

                // Otherwise allow original and let StatPart_SurvivalTool / WorkSpeedGlobal adjust speed
                return true;
            }
            catch (Exception e)
            {
                LogCompatWarning($"Prefix_Toils_Recipe_DoRecipeWork exception: {e}");
                return true;
            }
        }

        // Defensive prefix for Toils_Recipe.MakeUnfinishedThingIfNeeded – block creation of unfinished items if research tools are missing under extra-hardcore
        private static bool Prefix_Toils_Recipe_MakeUnfinishedThingIfNeeded(object __instance)
        {
            try
            {
                if (!RRHelpers.IsActive()) return true;

                Pawn pawn = null;
                try { pawn = (Pawn)AccessTools.Field(__instance.GetType(), "actor").GetValue(__instance); } catch { }
                if (pawn == null) return true;

                Job job = pawn.CurJob;
                var wgd = RRHelpers.ResolveWorkGiverForJob(job);
                var required = RRHelpers.GetRequiredStatsForWorkGiverCached(wgd, job);
                if (required == null || required.Count == 0) return true;

                if (SurvivalToolsMod.Settings?.extraHardcoreMode == true && RRHelpers.Settings.IsRRCompatibilityEnabled)
                {
                    foreach (var st in required)
                    {
                        if (RRHelpers.Settings.IsRRStatRequiredInExtraHardcore(st) && !CompatAPI.PawnHasResearchTools(pawn))
                        {
                            if (IsCompatLogging()) LogCompat($"Blocking MakeUnfinished creation for {pawn.LabelShort}: missing RR research tool for stat {st.defName}.");
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                LogCompatWarning($"Prefix_Toils_Recipe_MakeUnfinishedThingIfNeeded exception: {e}");
                return true;
            }
        }

        // Zero research progress in Hardcore/Nightmare when pawn lacks tool (soft gate for non-bench jobs)
        private static bool Prefix_ResearchManager_ResearchPerformed(ref float amount, Pawn researcher)
        {
            try
            {
                if (!RRHelpers.IsActive()) return true;
                if (researcher == null) return true;
                if (RRHelpers.ShouldZeroRRProgress(researcher))
                {
                    if (amount > 0f) amount = 0f; // swallow progress
                    if (Prefs.DevMode && SurvivalToolsMod.Settings?.debugLogging == true)
                        LogCompat($"RR ZeroProgress: {researcher.LabelShort} (no research tool, mode={RRHelpers.Mode()})");
                }
            }
            catch (Exception e)
            {
                LogCompatWarning($"Prefix_ResearchManager_ResearchPerformed exception: {e}");
            }
            return true; // never block original (we just mutate amount)
        }

        // Nightmare: cause research WorkGivers to be skipped entirely when pawn lacks tool (hard gate).
        // IMPORTANT: this patches the DECLARED base method (WorkGiver.ShouldSkip),
        // and filters to RR scanners OR vanilla WorkGiver_Researcher at runtime.
        [HarmonyAfter(new[] { "ResearchReinvented" })]
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix_WorkGiver_ShouldSkip(WorkGiver __instance, Pawn pawn, bool forced, ref bool __result)
        {
            try
            {
                if (__instance == null || pawn == null) return true;
                if (!RRHelpers.IsActive()) return true;
                if (!RRHelpers.ShouldHardBlockBenchResearch(pawn)) return true; // only bench research in Nightmare lacking tool
                var wgd = __instance.def;
                if (!RRHelpers.IsRRBenchResearchWG(wgd, __instance)) return true; // not explicit research
                __result = true;
                MaybeLogNightmareSkip(pawn, __instance, __instance.GetType());
                return false;
            }
            catch (Exception e)
            {
                LogCompatWarning($"Prefix_WorkGiver_ShouldSkip exception: {e}");
            }
            return true;
        }

        // --- WG_Researcher safety net (postfix, lowest priority) -------------
        [HarmonyPatch(typeof(WorkGiver_Researcher), nameof(WorkGiver_Researcher.HasJobOnThing))]
        [HarmonyPriority(Priority.VeryLow)]
        [HarmonyAfter(new[] { "ResearchReinvented", "Dubwise.DubsMintMenus", "UnlimitedHugs.HugsLib", "brrainz.harmony" })]
        private static class Patch_WGResearcher_HasJobOnThing
        {
            private static void Postfix(Pawn pawn, Thing t, bool forced, ref bool __result)
            {
                try
                {
                    if (!RRHelpers.IsActive()) return;
                    if (!RRHelpers.ShouldHardBlockBenchResearch(pawn)) return;
                    if (__result) __result = false; // force denial in Nightmare when lacking tool
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(WorkGiver_Researcher), nameof(WorkGiver_Researcher.JobOnThing))]
        [HarmonyPriority(Priority.VeryLow)]
        [HarmonyAfter(new[] { "ResearchReinvented", "Dubwise.DubsMintMenus", "UnlimitedHugs.HugsLib", "brrainz.harmony" })]
        private static class Patch_WGResearcher_JobOnThing
        {
            private static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
            {
                try
                {
                    if (!RRHelpers.IsActive()) return;
                    if (!RRHelpers.ShouldHardBlockBenchResearch(pawn)) return;
                    if (__result != null) __result = null; // null out job creation
                }
                catch { }
            }
        }

        // ---------------- Dynamic RR award discovery & patching ----------------
        private static bool _awardDiscoveryDone;
        private static void DiscoverAndPatchRRAwardMethods(Harmony h)
        {
            if (_awardDiscoveryDone) return; _awardDiscoveryDone = true;
            if (h == null) return; if (!RRHelpers.IsActive()) return;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string an = null; try { an = asm.GetName()?.Name ?? string.Empty; } catch { }
                    if (string.IsNullOrEmpty(an) || an.IndexOf("researchreinvented", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Type[] types;
                    try { types = asm.GetTypes(); } catch { continue; }
                    foreach (var t in types)
                    {
                        MethodInfo[] methods;
                        try { methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance); } catch { continue; }
                        foreach (var m in methods)
                        {
                            if (m == null) continue;
                            string name = m.Name;
                            if (name.IndexOf("Research", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("Progress", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            var pars = m.GetParameters(); if (pars == null || pars.Length == 0) continue;
                            bool hasPawn = false; bool hasAmount = false; int amountIndex = -1;
                            for (int i = 0; i < pars.Length; i++)
                            {
                                var pt = pars[i].ParameterType;
                                if (!hasPawn && typeof(Pawn).IsAssignableFrom(pt)) hasPawn = true;
                                if (!hasAmount && (pt == typeof(float) || pt == typeof(int))) { hasAmount = true; amountIndex = i; }
                            }
                            if (!(hasPawn && hasAmount)) continue;
                            // Exclude ResearchManager.ResearchPerformed (already patched)
                            if (m.DeclaringType == typeof(ResearchManager) && name == nameof(ResearchManager.ResearchPerformed)) continue;
                            try
                            {
                                h.Patch(m, prefix: new HarmonyMethod(typeof(RRPatches), nameof(GenericAwardZeroPrefix)));
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (IsCompatLogging()) LogCompatWarning($"RR award method discovery error: {e.Message}");
            }
        }

        // Generic prefix: zero 'amount' argument when ShouldZeroRRProgress(pawn)
        private static void GenericAwardZeroPrefix(object __instance, ref float __state)
        {
            // This overload intentionally left empty; real logic uses below method via argument matching.
        }

        // Harmony will match this prefix when method signature contains (float amount, Pawn ...) or (Pawn ..., float amount)
        private static void GenericAwardZeroPrefix(ref float __0, Pawn __1)
        {
            try
            {
                if (!RRHelpers.IsActive()) return;
                Pawn p = __1;
                if (p != null && RRHelpers.ShouldZeroRRProgress(p))
                {
                    if (__0 > 0f) __0 = 0f;
                }
            }
            catch { }
        }
        private static void GenericAwardZeroPrefix(Pawn __0, ref float __1)
        {
            try
            {
                if (!RRHelpers.IsActive()) return;
                Pawn p = __0;
                if (p != null && RRHelpers.ShouldZeroRRProgress(p))
                {
                    if (__1 > 0f) __1 = 0f;
                }
            }
            catch { }
        }
        // Int variants
        private static void GenericAwardZeroPrefix(ref int __0, Pawn __1)
        {
            try
            {
                if (!RRHelpers.IsActive()) return;
                Pawn p = __1;
                if (p != null && RRHelpers.ShouldZeroRRProgress(p))
                {
                    if (__0 > 0) __0 = 0;
                }
            }
            catch { }
        }
        private static void GenericAwardZeroPrefix(Pawn __0, ref int __1)
        {
            try
            {
                if (!RRHelpers.IsActive()) return;
                Pawn p = __0;
                if (p != null && RRHelpers.ShouldZeroRRProgress(p))
                {
                    if (__1 > 0) __1 = 0;
                }
            }
            catch { }
        }
    }
}
