// RimWorld 1.6 / C# 7.3
// Source/Compatibility/CompatAPI.cs
// Keep! Needed for compatibility integrations, registry, and public API.
//
// SurvivalTools Generic Compatibility API + Registry (single file)
//
// - Public, mod-agnostic CompatAPI surface
// - Internal registry with pluggable modules (RR, Primitive Tools, Separate Tree Chopping)
// - Lightweight caching, null-safety, debug-only logging chatter

using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;
using SurvivalTools.Compat.PrimitiveTools;
// (Legacy SeparateTreeChopping namespace removed; Phase 10 helper lives under SurvivalTools.Compatibility.SeparateTreeChopping)
using SurvivalTools.Compat.ResearchReinvented;
// CommonSense: legacy module removed; new Phase 10 helper lives under SurvivalTools.Compatibility.CommonSense
using SurvivalTools.Compatibility.CommonSense;
// SmarterConstruction refactored (Phase 10) to direct helper init (not module-based)
using SurvivalTools.Compatibility.SmarterConstruction;
// (Phase 10) TD Enhancement Pack direct helper init
using SurvivalTools.Compatibility.TDEnhancementPack;
using SurvivalTools.Compatibility.TreeStack; // Tree stack integration
using SurvivalTools.Helpers;

using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Compat
{
    // Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs

    // ---------- Module Interface ----------
    public interface ICompatibilityModule
    {
        string ModName { get; }
        bool IsModActive { get; }
        void Initialize();
        List<StatDef> GetCompatibilityStats();
        Dictionary<string, string> GetDebugInfo();
    }

    // ---------- Primitive Tools ----------
    internal sealed class PrimitiveToolsCompatibilityModule : ICompatibilityModule
    {
        public string ModName => "Primitive Tools";
        public bool IsModActive => PrimitiveToolsHelpers.IsPrimitiveToolsActive();

        public void Initialize()
        {
#if DEBUG
            if (IsModActive && IsCompatLogging() && IsDebugLoggingEnabled)
                Log.Message($"[SurvivalTools Compat] Primitive Tools detected ({PrimitiveToolsHelpers.GetPrimitiveToolDefs().Count} defs).");
#endif
        }

        public List<StatDef> GetCompatibilityStats() => new List<StatDef>(); // none added by PT

        public Dictionary<string, string> GetDebugInfo()
        {
            var info = new Dictionary<string, string>
            {
                ["Active"] = IsModActive.ToString(),
                ["Tool Count"] = PrimitiveToolsHelpers.GetPrimitiveToolDefs().Count.ToString(),
                ["Optimize?"] = PrimitiveToolsHelpers.ShouldOptimizeForPrimitiveTools().ToString()
            };

            if (IsModActive)
            {
                var conflicts = PrimitiveToolsHelpers.CheckForConflicts();
                info["Conflicts"] = conflicts.Count > 0 ? string.Join("; ", conflicts) : "None";
            }
            return info;
        }
    }


    // ---------- Research Reinvented ----------
    internal sealed class ResearchReinventedCompatibilityModule : ICompatibilityModule
    {
        public string ModName => "Research Reinvented";
        public bool IsModActive => RRHelpers.IsRRActive;

        public void Initialize() { /* RR patches now initialized via shared harmony in registry init */ }

        public List<StatDef> GetCompatibilityStats() => CompatAPI.GetAllResearchStats();

        public Dictionary<string, string> GetDebugInfo() => RRHelpers.GetReflectionStatus();
    }

    // ---------- Registry ----------
    [StaticConstructorOnStartup]
    internal static class CompatibilityRegistry
    {
        private static readonly List<ICompatibilityModule> Modules = new List<ICompatibilityModule>();
        private static bool _initialized;

        static CompatibilityRegistry() => Initialize();

        public static void Initialize()
        {
            if (_initialized) return;

#if DEBUG
            if (IsCompatLogging() && IsDebugLoggingEnabled)
                Log.Message("[SurvivalTools Compat] Initializing module registry…");
#endif
            try
            {
                RegisterModule(new ResearchReinventedCompatibilityModule());
                RegisterModule(new PrimitiveToolsCompatibilityModule());
                // (Legacy) Separate Tree Chopping module removed – now handled by Phase 10 helper (STCHelpers)
                // New compat modules
                // Register new compatibility modules explicitly so they are discoverable and type-checked at compile time.
                // SmarterConstruction now uses direct helper initialization (not ICompatibilityModule)
                // TD Enhancement Pack migrated to Phase 10 helper (no module registration needed)

                foreach (var m in Modules.Where(m => m.IsModActive))
                {
                    try
                    {
#if DEBUG
                        if (IsCompatLogging() && IsDebugLoggingEnabled)
                            Log.Message($"[SurvivalTools Compat] Init {m.ModName}…");
#endif
                        m.Initialize();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[SurvivalTools Compat] Failed to init {m.ModName}: {e}");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"[SurvivalTools Compat] Registry init failed: {e}");
            }
            _initialized = true;

            // Phase 10: direct-initialized compatibility helpers (non-module style)
            try
            {
                var primaryHarmony = new HarmonyLib.Harmony("Jelly.SurvivalToolsReborn");
                SurvivalTools.HarmonyStuff.HarmonyPatches.Init(primaryHarmony);
                SurvivalTools.HarmonyStuff.WorkGiver_Gates.Init(primaryHarmony);
                SurvivalTools.HarmonyStuff.Patch_ToolInvalidation.Init(primaryHarmony);

                // Phase 10 helpers now use shared harmony where needed
                SurvivalTools.Compatibility.SmarterConstruction.SmarterConstructionHelpers.Initialize();
                SurvivalTools.Compatibility.SmarterConstruction.SmarterConstructionPatches.Initialize(primaryHarmony);
                SurvivalTools.Compatibility.SmarterDeconstruction.SmarterDeconstructionHelpers.Initialize();
                SurvivalTools.Compatibility.SmarterDeconstruction.SmarterDeconstructionPatches.Initialize(primaryHarmony);
                SurvivalTools.Compatibility.CommonSense.CommonSenseHelpers.Initialize();
                SurvivalTools.Compatibility.TDEnhancementPack.TDEnhancementPackHelpers.Initialize();
                SurvivalTools.Compatibility.TDEnhancementPack.TDEnhancementPackPatches.Initialize(primaryHarmony);
                SurvivalTools.Compatibility.SeparateTreeChopping.STCHelpers.Initialize();
                SurvivalTools.Compat.ResearchReinvented.RRHelpers.Initialize(primaryHarmony);
                SurvivalTools.Compat.ResearchReinvented.RRPatches.Init(primaryHarmony);
                // Tree Stack (aliases first, then WG mappings). Arbiter auto-runs via static ctor.
                TreeStatAliases.Initialize();
                TreeWorkGiverMappings.Initialize();
                // Auto-register active authority tree WorkGivers for right-click rescue + TreeFellingSpeed mapping.
                try
                {
                    var authority = TreeSystemArbiter.Authority;
                    foreach (var wg in DefDatabase<WorkGiverDef>.AllDefsListForReading)
                    {
                        if (wg == null) continue;
                        // Only consider plant cutting related work type (broad) first.
                        bool plantCutType = wg.workType == WorkTypeDefOf.PlantCutting;
                        // Authority-specific heuristics by defName/label.
                        string dn = wg.defName ?? string.Empty;
                        string lbl = wg.label ?? string.Empty;
                        bool matchesAuthority = false;
                        switch (authority)
                        {
                            case TreeAuthority.SeparateTreeChopping:
                                matchesAuthority = (dn.IndexOf("ChopTrees", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                                   (lbl.IndexOf("chop", StringComparison.OrdinalIgnoreCase) >= 0);
                                break;
                            case TreeAuthority.PrimitiveTools_TCSS:
                                matchesAuthority = (dn.IndexOf("ChopTrees", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                                   (dn.IndexOf("ChopWood", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                                   (lbl.IndexOf("chop", StringComparison.OrdinalIgnoreCase) >= 0);
                                break;
                            default: // Vanilla / internal
                                matchesAuthority = plantCutType || dn.IndexOf("PlantsCut", StringComparison.OrdinalIgnoreCase) >= 0;
                                break;
                        }
                        if (!matchesAuthority) continue;
                        // Map to TreeFellingSpeed and register for rescue.
                        try
                        {
                            // Obtain worker class via reflection (public field not guaranteed)
                            // Resolve workerClass via reflection (cannot access CompatAPI private field here)
                            var workerField = typeof(WorkGiverDef).GetField("workerClass", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                            var wt = workerField?.GetValue(wg) as Type;
                            if (wt == null) continue; // skip if no worker class
                            CompatAPI.MapWGsToStat_ByDerivationOrAlias(ST_StatDefOf.TreeFellingSpeed, new[] { wt }, null);
                            CompatAPI.RegisterRightClickEligibleWGSubclass(wt);
#if DEBUG
                            if (IsCompatLogging() && IsDebugLoggingEnabled)
                                Log.Message($"[SurvivalTools Compat] Tree WG registered for right-click: {wg.defName} => TreeFellingSpeed");
#endif
                        }
                        catch (Exception inner)
                        {
                            Log.Warning($"[SurvivalTools Compat] Failed tree WG auto-registration for {wg.defName}: {inner.Message}");
                        }
                    }
                }
                catch (Exception autoRegEx)
                {
                    Log.Warning("[SurvivalTools Compat] Tree WG auto-registration phase failed: " + autoRegEx.Message);
                }
                // Core (vanilla) right-click eligibility registrations (non-tree). Tree workers handled above.
                SurvivalTools.Compatibility.RightClick.RightClickEligibilityBootstrap.Initialize();
                // Diagnostic: tree authority summary (safe guarded)
                try
                {
                    Log.Message($"[SurvivalTools Compat] TreeAuthority: {TreeSystemArbiter.Authority} (STC={TreeSystemArbiter.STC_Active}, PT={TreeSystemArbiter.PT_Active}, TCSS={TreeSystemArbiter.TCSS_Active}, PC={TreeSystemArbiter.PrimitiveCore_Active})");
                }
                catch (Exception e)
                {
                    Log.Message("[SurvivalTools Compat] TreeAuthority line failed: " + e.Message);
                }
            }
            catch (Exception e)
            {
                Log.Error("[SurvivalTools Compat] Phase 10 direct helper init failed: " + e);
            }

            // Initialize the ToolStatResolver (Phase 2)
            Helpers.ToolStatResolver.Initialize();
        }

        public static void RegisterModule(ICompatibilityModule module)
        {
            if (module == null) return;
            if (Modules.Any(m => string.Equals(m.ModName, module.ModName, StringComparison.OrdinalIgnoreCase)))
            {
#if DEBUG
                if (IsCompatLogging() && IsDebugLoggingEnabled)
                    Log.Message($"[SurvivalTools Compat] {module.ModName} already registered, skipping.");
#endif
                return;
            }
            Modules.Add(module);
        }


        public static IEnumerable<ICompatibilityModule> GetActiveModules() =>
            Modules.Where(m => m.IsModActive);

        public static ICompatibilityModule GetModule(string modName) =>
            Modules.FirstOrDefault(m => m.ModName.Equals(modName, StringComparison.OrdinalIgnoreCase));

        public static List<StatDef> GetAllCompatibilityStats()
        {
            var all = new List<StatDef>();
            foreach (var m in GetActiveModules())
            {
                try { all.AddRange(m.GetCompatibilityStats()); }
                catch (Exception e) { Log.Warning($"[SurvivalTools Compat] Stats from {m.ModName} failed: {e.Message}"); }
            }
            return all.Where(s => s != null).Distinct().ToList();
        }

        // (moved) Tree Stack alias dictionary now lives within CompatAPI proper.

#if DEBUG
        [DebugAction("Survival Tools", "Dump compatibility modules status")]
        public static void DumpCompatibilityStatus()
        {
            Log.Message("[SurvivalTools Compat] === Compatibility Modules Status ===");
            foreach (var m in Modules)
            {
                Log.Message($"[SurvivalTools Compat] Module: {m.ModName}");
                Log.Message($"[SurvivalTools Compat]   Active: {m.IsModActive}");
                if (m.IsModActive)
                {

                    // Tree Stack summary
                    try
                    {
                        Log.Message($"[SurvivalTools Compat] TreeAuthority: {TreeSystemArbiter.Authority} (STC={TreeSystemArbiter.STC_Active}, PT={TreeSystemArbiter.PT_Active}, TCSS={TreeSystemArbiter.TCSS_Active}, PC={TreeSystemArbiter.PrimitiveCore_Active})");
                    }
                    catch (Exception e)
                    {
                        Log.Message("[SurvivalTools Compat] TreeAuthority line failed: " + e.Message);
                    }
                    try
                    {
                        var info = m.GetDebugInfo();
                        foreach (var kv in info) Log.Message($"[SurvivalTools Compat]   {kv.Key}: {kv.Value}");
                        var stats = m.GetCompatibilityStats();
                        var statNames = (stats ?? new List<StatDef>()).Select(s => s != null ? s.defName : "null").ToArray();
                        Log.Message($"[SurvivalTools Compat]   Stats: {string.Join(", ", statNames)}");
                    }
                    catch (Exception e) { Log.Message($"[SurvivalTools Compat]   Error: {e.Message}"); }
                }
                Log.Message(string.Empty);
            }
            Log.Message("[SurvivalTools Compat] === End Compatibility Status ===");
        }
#endif
    }

    // ---------- Registry (Phase 1) ----------
    internal static class SurvivalToolRegistry
    {
        // O(1) lookups for WorkGiver/Job requirements
        private static readonly Dictionary<WorkGiverDef, List<StatDef>> _workGiverRequirements = new Dictionary<WorkGiverDef, List<StatDef>>();
        private static readonly Dictionary<JobDef, List<StatDef>> _jobRequirements = new Dictionary<JobDef, List<StatDef>>();
        private static readonly Dictionary<StatDef, List<string>> _stringStatAliases = new Dictionary<StatDef, List<string>>();
        private static readonly Dictionary<string, string> _toolQuirks = new Dictionary<string, string>();

        // Callback support
        private static readonly List<Action> _afterDefsLoadedCallbacks = new List<Action>();
        private static bool _initialized = false;

        public static void RegisterWorkGiverRequirement(WorkGiverDef workGiver, StatDef stat)
        {
            if (workGiver == null || stat == null) return;
            if (!_workGiverRequirements.ContainsKey(workGiver))
                _workGiverRequirements[workGiver] = new List<StatDef>();
            if (!_workGiverRequirements[workGiver].Contains(stat))
                _workGiverRequirements[workGiver].Add(stat);
        }

        public static void RegisterJobRequirement(JobDef job, StatDef stat)
        {
            if (job == null || stat == null) return;
            if (!_jobRequirements.ContainsKey(job))
                _jobRequirements[job] = new List<StatDef>();
            if (!_jobRequirements[job].Contains(stat))
                _jobRequirements[job].Add(stat);
        }

        public static void RegisterStatAlias(StatDef stat, string alias)
        {
            if (stat == null || string.IsNullOrEmpty(alias)) return;
            if (!_stringStatAliases.ContainsKey(stat))
                _stringStatAliases[stat] = new List<string>();
            if (!_stringStatAliases[stat].Contains(alias))
                _stringStatAliases[stat].Add(alias);
        }

        public static void RegisterToolQuirk(string toolDefName, string quirkDescription)
        {
            if (string.IsNullOrEmpty(toolDefName) || string.IsNullOrEmpty(quirkDescription)) return;
            _toolQuirks[toolDefName] = quirkDescription;
        }

        public static void OnAfterDefsLoaded(Action callback)
        {
            if (callback == null) return;
            if (_initialized)
            {
                // Already initialized, run immediately
                try { callback(); } catch (Exception e) { Log.Error($"[SurvivalTools Registry] Callback failed: {e}"); }
            }
            else
            {
                _afterDefsLoadedCallbacks.Add(callback);
            }
        }

        // O(1) lookups
        public static List<StatDef> GetWorkGiverRequirements(WorkGiverDef workGiver)
        {
            return _workGiverRequirements.TryGetValue(workGiver, out var stats) ? stats : new List<StatDef>();
        }

        public static List<StatDef> GetJobRequirements(JobDef job)
        {
            return _jobRequirements.TryGetValue(job, out var stats) ? stats : new List<StatDef>();
        }

        public static List<string> GetStatAliases(StatDef stat)
        {
            return _stringStatAliases.TryGetValue(stat, out var aliases) ? aliases : new List<string>();
        }

        public static string GetToolQuirk(string toolDefName)
        {
            return _toolQuirks.TryGetValue(toolDefName, out var quirk) ? quirk : null;
        }

        // Array versions for JobGate (LINQ-free)
        public static StatDef[] GetRequiredStatsFor(WorkGiverDef wg)
        {
            if (wg == null) return new StatDef[0];
            var list = GetWorkGiverRequirements(wg);
            return list.Count > 0 ? list.ToArray() : new StatDef[0];
        }

        public static StatDef[] GetRequiredStatsFor(JobDef job)
        {
            if (job == null) return new StatDef[0];
            var list = GetJobRequirements(job);
            return list.Count > 0 ? list.ToArray() : new StatDef[0];
        }

        public static bool IsModActive(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return false;
            return ModsConfig.ActiveModsInLoadOrder.Any(m =>
                m.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        }

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            // Run all deferred callbacks
            foreach (var callback in _afterDefsLoadedCallbacks)
            {
                try { callback(); } catch (Exception e) { Log.Error($"[SurvivalTools Registry] Callback failed: {e}"); }
            }
        }

        // No-op overloads for existing call sites (Phase 1 compatibility)
        public static void RegisterWorkGiverRequirement(string workGiverDefName, string statDefName)
        {
            var workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            var stat = DefDatabase<StatDef>.GetNamedSilentFail(statDefName);
            RegisterWorkGiverRequirement(workGiver, stat);
        }

        public static void RegisterJobRequirement(string jobDefName, string statDefName)
        {
            var job = DefDatabase<JobDef>.GetNamedSilentFail(jobDefName);
            var stat = DefDatabase<StatDef>.GetNamedSilentFail(statDefName);
            RegisterJobRequirement(job, stat);
        }

        public static void RegisterStatAlias(string statDefName, string alias)
        {
            var stat = DefDatabase<StatDef>.GetNamedSilentFail(statDefName);
            RegisterStatAlias(stat, alias);
        }
    }

    // ---------- Public API ----------
    public static class CompatAPI
    {
        private static readonly System.Reflection.FieldInfo _wgWorkerField = typeof(WorkGiverDef).GetField("workerClass", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Tree Stack StatDef->StatDef alias support (local to CompatAPI)
        private static readonly Dictionary<StatDef, HashSet<StatDef>> _treeStackStatAliases = new Dictionary<StatDef, HashSet<StatDef>>();
        public static IEnumerable<StatDef> ResolveStatAliases(StatDef stat)
        {
            if (stat == null) yield break;
            yield return stat;
            if (_treeStackStatAliases.TryGetValue(stat, out var set))
            {
                foreach (var s in set) if (s != null) yield return s;
            }
        }

        // --- Phase 10 Helpers: Bulk WG mapping / exemptions / right-click eligibility (refactored) ---
        public static void MapWGsToStat_ByDerivationOrAlias(StatDef toolStat, IEnumerable<Type> workerBasesOrExact, IEnumerable<string> defNameAliases)
        {
            try
            {
                if (toolStat == null) return;
                var baseTypes = workerBasesOrExact?.Where(t => t != null).ToList();
                var aliasSet = defNameAliases != null ? new HashSet<string>(defNameAliases.Where(a => !string.IsNullOrEmpty(a)), StringComparer.OrdinalIgnoreCase) : null;
                bool HasBaseTypes = baseTypes != null && baseTypes.Count > 0;
                bool HasAliases = aliasSet != null && aliasSet.Count > 0;
                if (!HasBaseTypes && !HasAliases) return;
                foreach (var wg in DefDatabase<WorkGiverDef>.AllDefsListForReading)
                {
                    if (wg == null) continue;
                    bool match = false;
                    if (HasAliases && aliasSet.Contains(wg.defName)) match = true;
                    if (!match && HasBaseTypes)
                    {
                        var wt = _wgWorkerField?.GetValue(wg) as Type;
                        if (wt != null)
                        {
                            foreach (var bt in baseTypes)
                            {
                                if (bt.IsAssignableFrom(wt)) { match = true; break; }
                            }
                        }
                    }
                    if (match) SurvivalToolRegistry.RegisterWorkGiverRequirement(wg, toolStat);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[SurvivalTools][CompatAPI] MapWGsToStat_ByDerivationOrAlias failed: {ex.Message}");
            }
        }

        public static void ExemptPureDelivery_ByDerivationOrAlias(IEnumerable<Type> workerBasesOrExact, IEnumerable<string> defNameAliases)
        {
            try
            {
                var baseTypes = workerBasesOrExact?.Where(t => t != null).ToList();
                var aliasSet = defNameAliases != null ? new HashSet<string>(defNameAliases.Where(a => !string.IsNullOrEmpty(a)), StringComparer.OrdinalIgnoreCase) : null;
                bool HasBaseTypes = baseTypes != null && baseTypes.Count > 0;
                bool HasAliases = aliasSet != null && aliasSet.Count > 0;
                if (!HasBaseTypes && !HasAliases) return;
                foreach (var wg in DefDatabase<WorkGiverDef>.AllDefsListForReading)
                {
                    if (wg == null) continue;
                    bool match = false;
                    if (HasAliases && aliasSet.Contains(wg.defName)) match = true;
                    if (!match && HasBaseTypes)
                    {
                        var wt = _wgWorkerField?.GetValue(wg) as Type;
                        if (wt != null)
                        {
                            foreach (var bt in baseTypes)
                            {
                                if (bt.IsAssignableFrom(wt)) { match = true; break; }
                            }
                        }
                    }
                    if (match) { try { Gating.JobGate.MarkPureDelivery(wg); } catch { } }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[SurvivalTools][CompatAPI] ExemptPureDelivery_ByDerivationOrAlias failed: {ex.Message}");
            }
        }

        public static void RegisterRightClickEligibleWGSubclass(Type workerSubclass)
        {
            try
            {
                UI.RightClickRescue.ST_RightClickRescueProvider.RegisterWorkerSubclass(workerSubclass);
            }
            catch (Exception ex)
            {
                Log.Warning($"[SurvivalTools][CompatAPI] RegisterRightClickEligibleWGSubclass failed: {ex.Message}");
            }
        }
        // Registry entry points (Phase 1)
        public static void RegisterWorkGiverRequirement(WorkGiverDef workGiver, StatDef stat) => SurvivalToolRegistry.RegisterWorkGiverRequirement(workGiver, stat);
        public static void RegisterWorkGiverRequirement(string workGiverDefName, string statDefName) => SurvivalToolRegistry.RegisterWorkGiverRequirement(workGiverDefName, statDefName);
        public static void RegisterJobRequirement(JobDef job, StatDef stat) => SurvivalToolRegistry.RegisterJobRequirement(job, stat);
        public static void RegisterJobRequirement(string jobDefName, string statDefName) => SurvivalToolRegistry.RegisterJobRequirement(jobDefName, statDefName);
        // Legacy string alias system (Phase 1). Renamed to avoid overload ambiguity.
        public static void RegisterStatStringAlias(StatDef stat, string alias) => SurvivalToolRegistry.RegisterStatAlias(stat, alias);
        public static void RegisterStatStringAlias(string statDefName, string alias) => SurvivalToolRegistry.RegisterStatAlias(statDefName, alias);
        // Tree Stack aliasing (StatDef->StatDef). Stored in _treeStackStatAliases.
        public static void RegisterStatAlias(StatDef from, StatDef to)
        {
            try
            {
                if (from == null || to == null || from == to) return;
                if (!_treeStackStatAliases.TryGetValue(from, out var set))
                {
                    set = new HashSet<StatDef>();
                    _treeStackStatAliases[from] = set;
                }
                set.Add(to);
            }
            catch (Exception e)
            {
#if DEBUG
                Log.Warning("[SurvivalTools][CompatAPI] RegisterStatAlias failed: " + e.Message);
#endif
            }
        }

        /// <summary>
        /// Register a tool quirk by defName match (legacy overload).
        /// Forwarder to new quirk system.
        /// </summary>
        [System.Obsolete("Legacy overload. Use predicate-based RegisterToolQuirk for better control.", false)]
        public static void RegisterToolQuirk(string toolDefName, string quirkDescription)
        {
            // Forward to new system with exact defName match
            RegisterToolQuirk(
                predicate: toolDef => string.Equals(toolDef.defName, toolDefName, StringComparison.OrdinalIgnoreCase),
                action: applier => applier.AddDevNote(quirkDescription)
            );
        }

        public static void OnAfterDefsLoaded(Action callback) => SurvivalToolRegistry.OnAfterDefsLoaded(callback);
        public static bool IsModActive(string packageId) => SurvivalToolRegistry.IsModActive(packageId);

        // RR initialization guard wrapper (prevents accidental double init noise)
        private static bool _rrInit;
        public static void InitResearchReinventedIfNeeded(HarmonyLib.Harmony h = null)
        {
            if (_rrInit) return;
            _rrInit = true;
            try
            {
                SurvivalTools.Compat.ResearchReinvented.RRHelpers.Initialize(h ?? new HarmonyLib.Harmony("jelly.survivaltools.rrbootstrap"));
                ST_Logging.DevOnce("Compat.RR.Init", "[Compat] RRHelpers.Initialize() completed.");
            }
            catch (Exception ex)
            {
                Log.Warning("[SurvivalTools Compat] RR init wrapper failed: " + ex.Message);
            }
        }

        // — Research Reinvented (kept for backward compatibility with your call sites) —
        public static bool IsResearchReinventedActive => RRHelpers.IsRRActive;

        // Cached stat defs to avoid recursion/expensive lookups
        private static StatDef _cachedResearchSpeed;
        private static StatDef _cachedFieldResearchSpeed;

        public static StatDef GetResearchSpeedStat()
        {
            return _cachedResearchSpeed
                   ?? (_cachedResearchSpeed = ST_StatDefOf.ResearchSpeed
                       ?? DefDatabase<StatDef>.GetNamedSilentFail("ResearchSpeed"));
        }

        public static StatDef GetFieldResearchSpeedStat()
        {
            return _cachedFieldResearchSpeed
                   ?? (_cachedFieldResearchSpeed = DefDatabase<StatDef>.GetNamedSilentFail("FieldResearchSpeedMultiplier")
                       ?? DefDatabase<StatDef>.GetNamedSilentFail("RR_FieldResearchSpeed")
                       ?? DefDatabase<StatDef>.GetNamedSilentFail("FieldResearchSpeed"));
        }

        /// <summary>
        /// True if pawn has a survival tool (real or virtual) that provides research speed.
        /// Works for both vanilla Research and RR’s Field Research.
        /// </summary>
        public static bool PawnHasResearchTools(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                return false;

            var researchStat = GetResearchSpeedStat();
            if (researchStat != null && pawn.HasSurvivalToolFor(researchStat))
                return true;

            var fieldStat = GetFieldResearchSpeedStat();
            if (fieldStat != null && pawn.HasSurvivalToolFor(fieldStat))
                return true;

            return false;
        }

        public static bool IsRRWorkGiver(WorkGiverDef wg) => RRHelpers.IsRRWorkGiver(wg);
        public static bool IsFieldResearchWorkGiver(WorkGiverDef wg) => RRHelpers.IsFieldResearchWorkGiver(wg);

        // — Primitive Tools —
        public static bool IsPrimitiveToolsActive => PrimitiveToolsHelpers.IsPrimitiveToolsActive();
        public static bool PawnHasPrimitiveTools(Pawn pawn) => PrimitiveToolsHelpers.PawnHasPrimitiveTools(pawn);
        public static List<Thing> GetPawnPrimitiveTools(Pawn pawn) => PrimitiveToolsHelpers.GetPawnPrimitiveTools(pawn);
        public static bool ShouldOptimizeForPrimitiveTools() => PrimitiveToolsHelpers.ShouldOptimizeForPrimitiveTools();

        // — Separate Tree Chopping —
        public static bool IsSeparateTreeChoppingActive => Compatibility.SeparateTreeChopping.SeparateTreeChoppingConflict.IsSeparateTreeChoppingActive();
        public static bool HasTreeFellingConflict() => Compatibility.SeparateTreeChopping.SeparateTreeChoppingConflict.HasTreeFellingConflict();
        public static bool ApplyTreeFellingConflictResolution() => Compatibility.SeparateTreeChopping.SeparateTreeChoppingConflict.ApplyRecommendedResolution();
        public static List<string> GetSeparateTreeChoppingRecommendations() => Compatibility.SeparateTreeChopping.SeparateTreeChoppingConflict.GetUserRecommendations();

        public static string GetTreeAuthoritySummary() => $"{TreeSystemArbiter.Authority} (STC={TreeSystemArbiter.STC_Active}, PT={TreeSystemArbiter.PT_Active}, TCSS={TreeSystemArbiter.TCSS_Active}, PC={TreeSystemArbiter.PrimitiveCore_Active})";

        // — Generic —
        public static List<StatDef> GetAllCompatibilityStats() => CompatibilityRegistry.GetAllCompatibilityStats();
        public static List<StatDef> GetAllResearchStats()
        {
            var list = new List<StatDef>();
            var r = GetResearchSpeedStat();
            var f = GetFieldResearchSpeedStat();
            if (r != null) list.Add(r);
            if (f != null && !list.Contains(f)) list.Add(f);
            return list;
        }

        // Diagnostics: produce lines summarizing mappings and right-click eligibility + pure delivery overrides
        public static List<string> DumpCompatibilityStatusLines()
        {
            var lines = new List<string>();
            try
            {
                lines.Add("-- Compatibility (Survival Tools) --");
                lines.Add("Mapped WorkGivers -> ToolStats:");
                // Enumerate WG->stat mappings (internal data via reflection fallback if needed)
                try
                {
                    var regType = typeof(SurvivalTools.Compat.SurvivalToolRegistry);
                    var field = regType.GetField("_workGiverRequirements", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    var dict = field?.GetValue(null) as System.Collections.IDictionary;
                    if (dict != null)
                    {
                        foreach (System.Collections.DictionaryEntry de in dict)
                        {
                            var wg = de.Key as WorkGiverDef;
                            var list = de.Value as List<StatDef>;
                            if (wg == null || list == null) continue;
                            lines.Add($"  {wg.defName} => {string.Join(",", list.Select(s => s?.defName))}");
                        }
                    }
                    else lines.Add("  <no mappings>");
                }
                catch (Exception em) { lines.Add("  <error enumerating mappings> " + em.Message); }

                try
                {
                    var names = UI.RightClickRescue.ST_RightClickRescueProvider.GetRegisteredSubclassNames().ToList();
                    lines.Add("RightClickEligibleWorkerSubclasses: " + (names.Count == 0 ? "<none>" : string.Join(", ", names)));
                }
                catch (Exception er) { lines.Add("RightClickEligibleWorkerSubclasses: <error> " + er.Message); }

                // Tree authority / scanner policy summary (Phase 10 Tree Stack)
                try
                {
                    lines.Add($"TreeAuthority: {TreeSystemArbiter.Authority} (STC={TreeSystemArbiter.STC_Active}, PT={TreeSystemArbiter.PT_Active}, TCSS={TreeSystemArbiter.TCSS_Active}, PC={TreeSystemArbiter.PrimitiveCore_Active})");
                    switch (TreeSystemArbiter.Authority)
                    {
                        case TreeAuthority.SeparateTreeChopping:
                            lines.Add("Tree scanners registered: STC only (TreesChop, PlantsCut)");
                            lines.Add("Tree scanners suppressed: PrimitiveTools, Vanilla");
                            break;
                        case TreeAuthority.PrimitiveTools_TCSS:
                            lines.Add("Tree scanners registered: PrimitiveTools (+Vanilla fallback)");
                            lines.Add("Tree scanners suppressed: STC");
                            break;
                        default:
                            lines.Add("Tree scanners registered: Vanilla");
                            lines.Add("Tree scanners suppressed: STC, PrimitiveTools");
                            break;
                    }
                    // Compact single-line summary for downstream parsers / tests
                    string reg, sup;
                    switch (TreeSystemArbiter.Authority)
                    {
                        case TreeAuthority.SeparateTreeChopping: reg = "STC"; sup = "PT,Vanilla"; break;
                        case TreeAuthority.PrimitiveTools_TCSS: reg = "PT,Vanilla"; sup = "STC"; break;
                        default: reg = "Vanilla"; sup = "STC,PT"; break;
                    }
                    lines.Add($"TreeAuthoritySummary authority={TreeSystemArbiter.Authority} registered={reg} suppressed={sup}");
                }
                catch (Exception taEx)
                {
                    lines.Add("TreeAuthority summary: <error> " + taEx.Message);
                }

                try
                {
                    int pureCount = Gating.JobGate.GetExplicitPureDeliveryWorkGivers().Count();
                    lines.Add("PureDeliveryExplicit: " + pureCount);
                }
                catch (Exception ep) { lines.Add("PureDeliveryExplicit: <error> " + ep.Message); }
            }
            catch (Exception ex)
            {
                lines.Add("[CompatAPI] Error building compatibility status lines: " + ex.Message);
            }
            return lines;
        }

        [DebugAction("Survival Tools", "Dump compatibility status")]
        public static void DumpCompatibilityStatus() => CompatibilityRegistry.DumpCompatibilityStatus();

        // Hot reload helper if you ever need to re-init on game load changes.
        public static void ReinitializeRegistryForDebug()
        {
#if DEBUG
            Log.Message("[SurvivalTools Compat] ReinitializeRegistryForDebug() called (dev only).");
#endif
        }

        // Array getters for JobGate (Phase 5)
        public static StatDef[] GetRequiredStatsFor(WorkGiverDef wg) => SurvivalToolRegistry.GetRequiredStatsFor(wg);
        public static StatDef[] GetRequiredStatsFor(JobDef job) => SurvivalToolRegistry.GetRequiredStatsFor(job);

        // Forwarders for compatibility (Phase 1 - add during refactor)
        /// <summary>
        /// Forwarder during refactor. Do not extend.
        /// </summary>
        [System.Obsolete("Forwarder during refactor. Do not extend.", false)]
        public static List<StatDef> GetStatsForWorkGiver(WorkGiverDef workGiver)
        {
            // Forward to existing StatGatingHelper during Phase 1
            return StatGatingHelper.GetStatsForWorkGiver(workGiver);
        }

        /// <summary>
        /// Forwarder during refactor. Do not extend.
        /// </summary>
        [System.Obsolete("Forwarder during refactor. Do not extend.", false)]
        public static bool ShouldBlockJobForStat(StatDef stat, Pawn pawn = null)
        {
            // Forward to existing StatGatingHelper during Phase 1
            var settings = SurvivalToolsMod.Settings;
            return settings != null && StatGatingHelper.ShouldBlockJobForStat(stat, settings, pawn);
        }

        // Phase 2 forwarders for stat resolution
        /// <summary>
        /// Forwarder during refactor. Do not extend.
        /// </summary>
        [System.Obsolete("Forwarder during refactor. Do not extend.", false)]
        public static float GetToolStatFactor(ThingDef toolDef, ThingDef stuffDef, StatDef stat)
        {
            // Forward to new centralized resolver (Phase 2)
            return Helpers.ToolStatResolver.GetToolStatFactor(toolDef, stuffDef, stat);
        }

        /// <summary>
        /// Forwarder during refactor. Do not extend.
        /// </summary>
        [System.Obsolete("Forwarder during refactor. Do not extend.", false)]
        public static ToolStatResolver.ToolStatInfo GetToolStatInfo(ThingDef toolDef, ThingDef stuffDef, StatDef stat)
        {
            // Forward to new centralized resolver (Phase 2)
            return Helpers.ToolStatResolver.GetToolStatInfo(toolDef, stuffDef, stat);
        }

        /// <summary>
        /// Register a tool quirk with predicate and action.
        /// Applied during stat resolution after inference but before clamping.
        /// Quirks are processed in registration order.
        /// </summary>
        /// <param name="predicate">Test if this quirk applies to a tool def</param>
        /// <param name="action">Apply quirk modifications</param>
        public static void RegisterToolQuirk(Func<ThingDef, bool> predicate, Action<ToolQuirkApplier> action)
        {
            if (predicate == null || action == null) return;
            Helpers.ToolStatResolver.RegisterQuirk(predicate, action);
        }
    }
}

// -------------------------------------------------------------------------
// TEMPLATE for new compatibility modules
// Copy, rename, and fill in details when adding new mod integrations.
// -------------------------------------------------------------------------
/*
public class SomeOtherModCompatibilityModule : ICompatibilityModule
{
    public string ModName => "Some Other Mod";

    public bool IsModActive
    {
        get
        {
            // Example detection (by packageId or def presence)
            return ModsConfig.ActiveModsInLoadOrder.Any(m =>
                m.PackageId.Contains("author.somemod") ||
                m.Name.Contains("Some Other Mod"));
        }
    }

    public void Initialize()
    {
        if (IsModActive)
        {
            if (ST_Logging.IsDebugLoggingEnabled && ST_Logging.IsCompatLogging())
                ST_Logging.LogInfo("[SurvivalTools Compat] SomeOtherMod detected and initialized.");
            // Perform setup or conflict resolution here
        }
    }

    public List<StatDef> GetCompatibilityStats()
    {
        // Return any StatDefs this mod introduces or uses
        return new List<StatDef>
        {
            // Example: DefDatabase<StatDef>.GetNamedSilentFail("SomeStat")
        };
    }

    public Dictionary<string, string> GetDebugInfo()
    {
        var info = new Dictionary<string, string>
        {
            ["Active"] = IsModActive.ToString(),
            ["Notes"] = "Fill in useful debug information here"
        };
        return info;
    }
}
*/
