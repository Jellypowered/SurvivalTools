// RimWorld 1.6 / C# 7.3
// Source/UI/RightClickRescue/FloatMenu_PrioritizeWithRescue.cs
// Postfix on FloatMenuMakerMap.GetOptions to inject enabled "Prioritize <job> (will fetch <tool>)" rescue options
// when vanilla would show a disabled prioritized job due to tool gating in Hardcore/Nightmare.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using SurvivalTools.Assign;
using SurvivalTools.Gating;
using SurvivalTools.Helpers;
using SurvivalTools.Scoring;
using SurvivalTools.Compat.ResearchReinvented; // RRHelpers

namespace SurvivalTools.UI.RightClickRescue
{
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    internal static partial class FloatMenu_PrioritizeWithRescue
    {
        // ========= Mod-source resolution caches/helpers =========
        private static readonly Dictionary<Assembly, string> _asmToModName = new Dictionary<Assembly, string>();
        private static readonly Dictionary<Type, string> _closureTypeToModName = new Dictionary<Type, string>();
        private static readonly FieldInfo _fiOptionAction =
            typeof(FloatMenuOption).GetField("action", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo _fiLabelInt =
            typeof(FloatMenuOption).GetField("labelInt", BindingFlags.Instance | BindingFlags.NonPublic);

        private static string SafeLower(string s) { try { return s == null ? string.Empty : s.ToLowerInvariant(); } catch { return string.Empty; } }

        private static string MapAssemblyToModName(Assembly asm)
        {
            try
            {
                if (asm == null) return null;
                if (_asmToModName.TryGetValue(asm, out var cached)) return cached;

                var mods = LoadedModManager.RunningModsListForReading;
                if (mods != null)
                {
                    for (int i = 0; i < mods.Count; i++)
                    {
                        var m = mods[i];
                        if (m == null || m.assemblies == null) continue;
                        var la = m.assemblies.loadedAssemblies;
                        if (la == null) continue;
                        for (int j = 0; j < la.Count; j++)
                        {
                            if (ReferenceEquals(la[j], asm))
                            {
                                var name = m.Name ?? "Unknown";
                                _asmToModName[asm] = name;
                                return name;
                            }
                        }
                    }
                }

                var anLower = SafeLower(asm.GetName()?.Name);
                if (anLower == "assembly-csharp" || anLower == "rimworld" || anLower == "verse")
                {
                    _asmToModName[asm] = "Core";
                    return "Core";
                }

                if (anLower.Contains("separatetree")) { _asmToModName[asm] = "Separate Tree Chopping"; return "Separate Tree Chopping"; }
                if (anLower.Contains("survivaltools")) { _asmToModName[asm] = "Survival Tools Reborn"; return "Survival Tools Reborn"; }

                _asmToModName[asm] = null; // unknown (don’t mislabel as Core)
                return null;
            }
            catch { return null; }
        }

        private static string TryResolveFromAction(FloatMenuOption opt)
        {
            try
            {
                var del = _fiOptionAction != null ? _fiOptionAction.GetValue(opt) as Action : null;
                if (del == null) return null;

                // 1) Delegate method assembly
                var m = del.Method;
                var dt = m == null ? null : m.DeclaringType;
                var asm = dt == null ? null : dt.Assembly;
                var mod = MapAssemblyToModName(asm);
                if (!string.IsNullOrEmpty(mod)) return mod;

                // 2) Closure object inspection (cheap & cached per-closure-type)
                var target = del.Target;
                if (target == null) return null;

                var t = target.GetType();
                if (_closureTypeToModName.TryGetValue(t, out var cachedMod))
                    return cachedMod;

                const int FIELD_SCAN_LIMIT = 16;
                var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                int scanned = 0;
                for (int i = 0; i < fields.Length && scanned < FIELD_SCAN_LIMIT; i++, scanned++)
                {
                    var f = fields[i];
                    object v = null;
                    try { v = f.GetValue(target); } catch { continue; }
                    if (v == null) continue;

                    // Defs carry modContentPack
                    if (v is Def def)
                    {
                        var pack = def.modContentPack;
                        if (pack != null && !string.IsNullOrEmpty(pack.Name))
                        {
                            _closureTypeToModName[t] = pack.Name;
                            return pack.Name;
                        }
                    }

                    // WorkGiver instance ⇒ def / assembly
                    if (v is WorkGiver wg)
                    {
                        try
                        {
                            var d = wg.def;
                            if (d != null && d.modContentPack != null && !string.IsNullOrEmpty(d.modContentPack.Name))
                            {
                                _closureTypeToModName[t] = d.modContentPack.Name;
                                return d.modContentPack.Name;
                            }
                        }
                        catch { }
                        var wgMod = MapAssemblyToModName(wg.GetType().Assembly);
                        if (!string.IsNullOrEmpty(wgMod))
                        {
                            _closureTypeToModName[t] = wgMod;
                            return wgMod;
                        }
                    }

                    // Any captured delegate/type?
                    if (v is Delegate d2)
                    {
                        var mod2 = MapAssemblyToModName(d2.Method?.DeclaringType?.Assembly);
                        if (!string.IsNullOrEmpty(mod2))
                        {
                            _closureTypeToModName[t] = mod2;
                            return mod2;
                        }
                    }
                    if (v is Type ty)
                    {
                        var mod3 = MapAssemblyToModName(ty.Assembly);
                        if (!string.IsNullOrEmpty(mod3))
                        {
                            _closureTypeToModName[t] = mod3;
                            return mod3;
                        }
                    }
                }

                _closureTypeToModName[t] = null; // unknown for this closure type
                return null;
            }
            catch { return null; }
        }

        private static bool IsSTCAuthorityActive()
        {
            try
            {
                var auth = SurvivalTools.Compatibility.TreeStack.TreeSystemArbiter.Authority;
                return auth == SurvivalTools.Compatibility.TreeStack.TreeAuthority.SeparateTreeChopping;
            }
            catch { return false; }
        }

        private static bool IsTCSSAuthorityActive()
        {
            try
            {
                var auth = SurvivalTools.Compatibility.TreeStack.TreeSystemArbiter.Authority;
                return auth == SurvivalTools.Compatibility.TreeStack.TreeAuthority.TreeChoppingSpeedStat ||
                       auth == SurvivalTools.Compatibility.TreeStack.TreeAuthority.PrimitiveTools_TCSS;
            }
            catch { return false; }
        }

        private static string HeuristicModFromOption(FloatMenuOption opt)
        {
            try
            {
                if (opt == null) return null;

                // Label must look like a "chop tree" command…
                var lab = opt.Label;
                if (string.IsNullOrEmpty(lab)) return null;
                var lower = lab.ToLowerInvariant();
                if (lower.IndexOf("chop", StringComparison.Ordinal) < 0) return null;
                if (lower.IndexOf("tree", StringComparison.Ordinal) < 0) return null;

                // …and the target must actually be a tree.
                var plant = opt.iconThing as Plant;
                if (plant?.def?.plant?.IsTree != true) return null;

                // Check which system has authority
                if (IsSTCAuthorityActive())
                    return "Separate Tree Chopping";
                
                if (IsTCSSAuthorityActive())
                    return "TCSS";

                return null;
            }
            catch { return null; }
        }

        private static string ResolveModNameForOption(FloatMenuOption opt)
        {
            // Order: Action/Assembly → closure fields → iconThing (non-Core only) → STC heuristic → null
            var mod = TryResolveFromAction(opt);
            if (!string.IsNullOrEmpty(mod)) return mod;

            try
            {
                var pack = opt.iconThing?.def?.modContentPack;
                if (pack != null && !string.IsNullOrEmpty(pack.Name) &&
                    !string.Equals(pack.Name, "Core", StringComparison.OrdinalIgnoreCase))
                    return pack.Name;
            }
            catch { }

            mod = HeuristicModFromOption(opt);
            if (!string.IsNullOrEmpty(mod)) return mod;

            // Do NOT default to "Core" here to avoid mislabeling.
            return null;
        }
        // ================== end helpers ==================

        // Cooldown for noisy debug lines (per label/basis) to avoid spam when rapidly right-clicking.
        private static readonly Dictionary<int, int> _debugLogCooldown = new Dictionary<int, int>();
        private const int DEBUG_LOG_SPAM_CD_TICKS = 600; // ~10 seconds @60 tps
        private const bool ENABLE_DEDUP_LOGS = true;
        private const bool ENABLE_MODTAG_LOGS = true;

        private static bool ShouldDebugLog(string key)
        {
            try
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                if (string.IsNullOrEmpty(key)) key = "?";
                int h = key.GetHashCode();
                if (_debugLogCooldown.TryGetValue(h, out var until) && now < until) return false;
                _debugLogCooldown[h] = now + DEBUG_LOG_SPAM_CD_TICKS;
                return true;
            }
            catch { return true; }
        }

        // Postfix signature (1.6) – context passed by ref
        static void Postfix(List<Pawn> selectedPawns, Vector3 clickPos, ref FloatMenuContext context, ref List<FloatMenuOption> __result)
        {
            try
            {
                var s = SurvivalToolsMod.Settings;
                if (context == null || __result == null || selectedPawns == null || selectedPawns.Count == 0) return;
                if (s == null || !s.enableRightClickRescue) return;
                bool rrActive = RRHelpers.IsActive();
                // Allow pass-through even in Normal mode when RR active (research-only rescue). Otherwise we would return too early.
                if (!(s.hardcoreMode || s.extraHardcoreMode) && !rrActive) return;
                if (context.IsMultiselect) return; // keep v1 simple

                // Duplicate guard: if provider (or another pass) already added a rescue option, skip.
                for (int i = 0; i < __result.Count; i++)
                {
                    var lab0 = __result[i]?.Label;
                    if (!string.IsNullOrEmpty(lab0) && lab0.IndexOf("(will fetch", StringComparison.OrdinalIgnoreCase) >= 0)
                        return;
                }

                var pawn = context.FirstSelectedPawn;
                if (pawn == null || pawn.Map != Find.CurrentMap || pawn.Downed || !pawn.CanTakeOrder) return;
                if (!pawn.RaceProps?.Humanlike ?? true) return;

                if (s.hardcoreMode || s.extraHardcoreMode)
                {
                    // Standard (non-research) rescues only in Hardcore/Nightmare.
                    RightClickRescueBuilder.TryAddRescueOptions(pawn, context, __result);
                }

                // RR research-specific rescue (all modes when RR active; semantics depend on mode)
                if (rrActive)
                {
                    int preCount = __result.Count;
                    TryAddRRResearchRescue(pawn, context, __result);
                    // Fallback: if Nightmare + pawn lacks tool + no rescue/disabled added (count unchanged)
                    // still hard-gate vanilla bench research by removing its option and adding disabled line.
                    try
                    {
                        if (RRHelpers.Mode() == RRHelpers.RRMode.Nightmare && !RRHelpers.PawnHasResearchTool(pawn) && preCount == __result.Count)
                        {
                            // Scan for vanilla research option label
                            const string basePrefix = "Prioritize researching"; // same as in TryAddRRResearchRescue
                            int removed = 0;
                            for (int i = __result.Count - 1; i >= 0; i--)
                            {
                                var lab = __result[i]?.Label; if (string.IsNullOrEmpty(lab)) continue;
                                if (lab.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase) && lab.IndexOf("(will fetch", StringComparison.OrdinalIgnoreCase) < 0)
                                { __result.RemoveAt(i); removed++; }
                            }
                            if (removed > 0)
                            {
                                // Add disabled explanatory line if we removed something.
                                var disabled = new FloatMenuOption(basePrefix + " (needs research tool)", null) { autoTakeable = false };
                                disabled.tooltip = "This pawn can't start research in Nightmare without a research tool.";
                                __result.Add(disabled);
                            }
                        }
                    }
                    catch { }
                }

                // If STC or TCSS owns authority, purge any SurvivalTools-added *tree felling* rescue options.
                try
                {
                    bool externalTree = SurvivalTools.Compatibility.TreeStack.TreeSystemArbiter.Authority
                                      == SurvivalTools.Compatibility.TreeStack.TreeAuthority.SeparateTreeChopping ||
                                        SurvivalTools.Compatibility.TreeStack.TreeSystemArbiter.Authority
                                      == SurvivalTools.Compatibility.TreeStack.TreeAuthority.TreeChoppingSpeedStat ||
                                        SurvivalTools.Compatibility.TreeStack.TreeSystemArbiter.Authority
                                      == SurvivalTools.Compatibility.TreeStack.TreeAuthority.PrimitiveTools_TCSS;
                    
                    if (externalTree && __result.Count > 0)
                    {
                        for (int i = __result.Count - 1; i >= 0; i--)
                        {
                            var lab = __result[i]?.Label; if (string.IsNullOrEmpty(lab)) continue;
                            if (lab.IndexOf("(will fetch", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var lower = lab.ToLowerInvariant();
                                // Keep this strict so we only remove ST's felling rescues, not external mod's native chop entries.
                                if (lower.Contains("fell") || lower.Contains("felling"))
                                {
                                    __result.RemoveAt(i);
                                    continue;
                                }
                            }
                        }
                    }
                }
                catch { }

                // Post-add dedup: if we now have a rescue option ("(will fetch"), remove the corresponding vanilla prioritized option.
                try
                {
                    bool debug = Prefs.DevMode && s.debugLogging;
                    var rescueBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < __result.Count; i++)
                    {
                        var opt = __result[i]; if (opt == null) continue;
                        var lab = opt.Label; if (string.IsNullOrEmpty(lab)) continue;
                        int paren = lab.IndexOf("(will fetch", StringComparison.OrdinalIgnoreCase);
                        if (paren > 0)
                        {
                            var basis = lab.Substring(0, paren).TrimEnd();
                            if (!string.IsNullOrEmpty(basis)) rescueBases.Add(basis);
                        }
                    }
                    if (rescueBases.Count > 0)
                    {
                        bool rrNightmare = rrActive && RRHelpers.Mode() == RRHelpers.RRMode.Nightmare;
                        for (int i = __result.Count - 1; i >= 0; i--)
                        {
                            var lab = __result[i]?.Label; if (string.IsNullOrEmpty(lab)) continue;
                            if (lab.IndexOf("(will fetch", StringComparison.OrdinalIgnoreCase) >= 0) continue; // keep rescue
                            foreach (var basis in rescueBases)
                            {
                                bool isResearchBasis = basis.IndexOf("research", StringComparison.OrdinalIgnoreCase) >= 0;
                                // Only remove vanilla research line in Nightmare (hard gate). For other bases (mining etc.) always remove.
                                if (lab.StartsWith(basis, StringComparison.OrdinalIgnoreCase) && (rrNightmare || !isResearchBasis))
                                {
                                    if (ENABLE_DEDUP_LOGS && debug && ShouldDebugLog("DedupRemoved:" + basis))
                                        Log.Message($"[RightClick] DedupRemoved | base='{basis}' | removed='{lab}'");
                                    __result.RemoveAt(i);
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }

                // Append mod source tags to prioritized options (post-dedup).
                try
                {
                    bool debug = Prefs.DevMode && s.debugLogging;
                    if (_fiLabelInt != null)
                    {
                        for (int i = 0; i < __result.Count; i++)
                        {
                            var opt = __result[i]; if (opt == null) continue;
                            var lab = opt.Label; if (string.IsNullOrEmpty(lab)) continue;
                            if (!lab.StartsWith("Prioritize", StringComparison.OrdinalIgnoreCase)) continue;

                            string modName = ResolveModNameForOption(opt);
                            if (string.IsNullOrEmpty(modName)) continue; // don't append "(Core)" by default

                            string suffix = " (" + modName + ")";
                            if (lab.EndsWith(suffix, StringComparison.Ordinal)) continue;

                            string newLabel = lab + suffix;
                            _fiLabelInt.SetValue(opt, newLabel);

                            if (ENABLE_MODTAG_LOGS && debug && ShouldDebugLog("ModTagAppended:" + modName))
                                Log.Message($"[RightClick] ModTagAppended | label='{lab}' -> '{newLabel}'");
                        }
                    }
                }
                catch { }

                // FINAL FALLBACK: Nightmare + no research tool => ensure NO actionable research options remain.
                try
                {
                    if (rrActive && RRHelpers.Mode() == RRHelpers.RRMode.Nightmare && !RRHelpers.PawnHasResearchTool(pawn))
                    {
                        const string token1 = "prioritize research"; // covers "Prioritize research" prefix
                        const string token2 = "prioritize researching";
                        bool replacedAny = false;
                        for (int i = __result.Count - 1; i >= 0; i--)
                        {
                            var opt = __result[i]; if (opt == null) continue;
                            var lab = opt.Label; if (string.IsNullOrEmpty(lab)) continue;
                            string lower = lab.ToLowerInvariant();
                            if ((lower.StartsWith(token1) || lower.StartsWith(token2)) && lower.IndexOf("(will fetch", StringComparison.Ordinal) < 0)
                            {
                                if (opt.action != null)
                                {
                                    // Replace with disabled variant (tooltip explanation)
                                    var disabled = new FloatMenuOption(lab + " (needs research tool)", null)
                                    { iconThing = opt.iconThing, autoTakeable = false };
                                    disabled.tooltip = "This pawn can't start research in Nightmare without a research tool.";
                                    __result[i] = disabled; replacedAny = true;
                                }
                            }
                        }
                        if (replacedAny && Prefs.DevMode && s.debugLogging && ShouldDebugLog("NightmareResearchFallback"))
                            Log.Message("[RightClick] Nightmare fallback disabled vanilla research option(s) (no tool).");
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                Log.Warning("[SurvivalTools.RightClickRescue] Postfix exception: " + ex);
            }
        }
    }
}

namespace SurvivalTools.UI.RightClickRescue
{
    using RimWorld;
    using Verse;
    using System;
    using System.Collections.Generic;
    using SurvivalTools.Assign;
    using SurvivalTools.Compat;
    using SurvivalTools.Scoring;
    using SurvivalTools.Helpers;
    using SurvivalTools.Compat.ResearchReinvented;

    internal static partial class FloatMenu_PrioritizeWithRescue
    {
        private static readonly StatDef _researchStat = CompatAPI.GetResearchSpeedStat() ?? ST_StatDefOf.ResearchSpeed;
        private static ResearchScanner _researchScannerInstance; // reuse existing scanner logic

        private static void EnsureResearchScanner()
        {
            if (_researchScannerInstance == null)
            {
                try { _researchScannerInstance = new ResearchScanner(); RightClickRescueBuilder.PrewarmResearch(); } catch { }
            }
        }

        private static void TryAddRRResearchRescue(Pawn pawn, FloatMenuContext ctx, List<FloatMenuOption> options)
        {
            try
            {
                if (pawn == null || ctx == null || options == null) return;
                if (!RRHelpers.IsActive()) return;
                if (!pawn.RaceProps?.Humanlike ?? true) return;
                EnsureResearchScanner();

                // Identify clicked bench via ResearchScanner (reuses robust detection heuristics)
                // ResearchScanner is declared below in the same namespace; ensure initialization via its static method.
                ResearchScanner.EnsureInit();
                RightClickRescueBuilder.IRescueTargetScanner scanner = _researchScannerInstance;
                if (scanner == null) return;
                if (!scanner.CanHandle(ctx)) return; // nothing that looks like a bench
                if (!scanner.TryDescribeTarget(pawn, ctx, out var desc)) return; // no research project / bench invalid
                if (desc.WorkGiverDef == null || desc.JobDef == null) return;

                var mode = RRHelpers.Mode();
                bool hasTool = RRHelpers.PawnHasResearchTool(pawn);
                // Quick exit: has tool => Nightmare shows vanilla only, others may optionally show rescue but spec says no rescue needed when already equipped.
                if (hasTool) return;

                string basePrefix = "Prioritize researching"; // vanilla prefix basis
                string toolName; bool canUpgrade = AssignmentSearchPreview.CanUpgradePreview(pawn, _researchStat, out toolName);
                bool nightmare = (mode == RRHelpers.RRMode.Nightmare);

                if (nightmare)
                {
                    if (canUpgrade)
                    {
                        // Add ONLY rescue; remove vanilla now so dedup not required to catch scenario where basePrefix differs (bench name variant)
                        RemoveVanillaResearchOptions(options, basePrefix);
                        AddResearchRescueOption(pawn, desc, toolName, options, basePrefix);
                    }
                    else
                    {
                        // Remove any vanilla research options and add disabled explanatory line
                        RemoveVanillaResearchOptions(options, basePrefix);
                        AddDisabledNightmareResearchOption(pawn, desc, options, basePrefix);
                    }
                    return;
                }

                // Hardcore or Normal: only add rescue if upgrade exists, keep vanilla line
                if (canUpgrade)
                {
                    AddResearchRescueOption(pawn, desc, toolName, options, basePrefix);
                }
            }
            catch (Exception e)
            {
                if (Prefs.DevMode && SurvivalToolsMod.Settings?.debugLogging == true)
                    Log.Warning("[ST.RRRightClick] Failed to add research rescue: " + e);
            }
        }

        private static void ExecuteRRResearchRescue(Pawn pawn, Thing bench, IntVec3 cell, WorkGiverDef wg, JobDef jobDef, bool immediate = false)
        {
            var settings = SurvivalToolsMod.Settings; if (pawn == null || settings == null) return;
            var stat = _researchStat; if (stat == null) return;
            bool upgradeQueued = AssignmentSearch.TryUpgradeFor(
                pawn,
                stat,
                0.1f,          // Spec: use 10% min gain for research rescue
                24f,           // modest radius
                500,           // path cost budget
                Assign.AssignmentSearch.QueuePriority.Front,
                "RightClick.ResearchRescue");

            Job job = JobMaker.MakeJob(jobDef, bench);
            job.playerForced = true;
            bool prereqs = upgradeQueued;
            if (Prefs.DevMode && settings.debugLogging)
            {
                try { Log.Message($"[RightClick] Research rescue: job={job.def.defName} immediate={immediate} prereqs={prereqs} upgradeQueued={upgradeQueued}"); } catch { }
            }

            if (!immediate)
            {
                // Always enqueue at tail; do not interrupt current job
                try { pawn.jobs?.jobQueue?.EnqueueLast(job, JobTag.Misc); } catch { pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc); }
                return;
            }

            if (prereqs)
            {
                // Enqueue then interrupt to let acquisition start immediately
                try { pawn.jobs?.jobQueue?.EnqueueLast(job, JobTag.Misc); } catch { }
                try { pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, true); } catch { }
                return;
            }

            // Immediate & no prereqs: start now
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
        private static void AddResearchRescueOption(Pawn pawn, RightClickRescueBuilder.RescueTarget desc, string toolName, List<FloatMenuOption> options, string basePrefix)
        {
            string toolDisplay = toolName ?? (_researchStat?.label?.CapitalizeFirst() ?? "tool");
            string label;
            try { label = basePrefix + " (" + "ST_WillFetchTool".Translate(toolDisplay) + ")"; }
            catch { label = basePrefix + " (will fetch " + toolDisplay + ")"; }
            Thing bench = desc.IconThing; IntVec3 clickCell = desc.ClickCell;
            Action act = () =>
            {
                bool immediate = KeyBindingDefOf.QueueOrder.IsDown;
                ExecuteRRResearchRescue(pawn, bench, clickCell, desc.WorkGiverDef, desc.JobDef, immediate);
            };
            var opt = new FloatMenuOption(label, act) { iconThing = bench, autoTakeable = false };
            options.Add(FloatMenuUtility.DecoratePrioritizedTask(opt, pawn, new LocalTargetInfo(clickCell)));
        }

        private static void AddDisabledNightmareResearchOption(Pawn pawn, RightClickRescueBuilder.RescueTarget desc, List<FloatMenuOption> options, string basePrefix)
        {
            string label = basePrefix + " (needs research tool)"; // Fallback; consider translation key later
            var opt = new FloatMenuOption(label, null) { iconThing = desc.IconThing, autoTakeable = false }; // null action => disabled
            opt.tooltip = "This pawn can't start research in Nightmare without a research tool.";
            options.Add(opt);
        }

        private static void RemoveVanillaResearchOptions(List<FloatMenuOption> options, string basePrefix)
        {
            if (options == null) return;
            for (int i = options.Count - 1; i >= 0; i--)
            {
                var lab = options[i]?.Label; if (string.IsNullOrEmpty(lab)) continue;
                if (lab.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase) && lab.IndexOf("(will fetch", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    options.RemoveAt(i);
                }
            }
        }
    }
}
