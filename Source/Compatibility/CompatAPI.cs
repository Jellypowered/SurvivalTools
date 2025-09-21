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
using SurvivalTools.Compat.SeparateTreeChopping;
using SurvivalTools.Compat.ResearchReinvented;
using SurvivalTools.Compat.CommonSense;
using SurvivalTools.Compat.SmarterConstruction;
using SurvivalTools.Compat.SmarterDeconstruction;
using SurvivalTools.Compat.TDEnhancementPack;
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

    // ---------- Separate Tree Chopping ----------
    internal sealed class SeparateTreeChoppingCompatibilityModule : ICompatibilityModule
    {
        public string ModName => "Separate Tree Chopping";
        public bool IsModActive => SeparateTreeChoppingHelpers.IsSeparateTreeChoppingActive();

        public void Initialize()
        {
            if (!IsModActive) return;

            // If both are handling tree felling, auto-prefer STC (safer UX).
            if (SeparateTreeChoppingHelpers.ShouldAutoDisableSTTreeFelling())
            {
#if DEBUG
                if (IsCompatLogging() && IsDebugLoggingEnabled)
                    Log.Message("[SurvivalTools Compat] Auto-resolving tree felling overlap for Separate Tree Chopping...");
#endif
                if (!SeparateTreeChoppingHelpers.ApplyRecommendedResolution())
                    Log.Warning("[SurvivalTools Compat] Failed to auto-resolve tree felling conflict.");
            }
        }

        public List<StatDef> GetCompatibilityStats() => new List<StatDef>(); // none added by STC

        public Dictionary<string, string> GetDebugInfo()
        {
            var map = new Dictionary<string, string> { ["Active"] = IsModActive.ToString() };
            if (!IsModActive) return map;

            map["Has Tree Conflict"] = SeparateTreeChoppingHelpers.HasTreeFellingConflict().ToString();
            map["Recommended Resolution"] = SeparateTreeChoppingHelpers.GetRecommendedResolution().ToString();

            var conflicts = SeparateTreeChoppingHelpers.CheckForConflicts();
            map["Conflicts"] = conflicts.Count > 0 ? string.Join("; ", conflicts) : "None";
            return map;
        }
    }

    // ---------- Research Reinvented ----------
    internal sealed class ResearchReinventedCompatibilityModule : ICompatibilityModule
    {
        public string ModName => "Research Reinvented";
        public bool IsModActive => RRHelpers.IsRRActive;

        public void Initialize() => RRHelpers.Initialize();

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
                RegisterModule(new SeparateTreeChoppingCompatibilityModule());
                // New compat modules
                // Register new compatibility modules explicitly so they are discoverable and type-checked at compile time.
                try { RegisterModule(new CommonSenseCompatibilityModule()); } catch { }
                try { RegisterModule(new SmarterConstructionCompatibilityModule()); } catch { }
                try { RegisterModule(new SmarterDeconstructionCompatibilityModule()); } catch { }
                try { RegisterModule(new TDEnhancementPackCompatibilityModule()); } catch { }

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

            // Initialize the SurvivalToolRegistry (Phase 1)
            SurvivalToolRegistry.Initialize();

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

#if DEBUG
        [DebugAction("SurvivalTools", "Dump compatibility modules status")]
        public static void DumpCompatibilityStatus()
        {
            Log.Message("[SurvivalTools Compat] === Compatibility Modules Status ===");
            foreach (var m in Modules)
            {
                Log.Message($"[SurvivalTools Compat] Module: {m.ModName}");
                Log.Message($"[SurvivalTools Compat]   Active: {m.IsModActive}");
                if (m.IsModActive)
                {
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
        private static readonly Dictionary<StatDef, List<string>> _statAliases = new Dictionary<StatDef, List<string>>();
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
            if (!_statAliases.ContainsKey(stat))
                _statAliases[stat] = new List<string>();
            if (!_statAliases[stat].Contains(alias))
                _statAliases[stat].Add(alias);
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
            return _statAliases.TryGetValue(stat, out var aliases) ? aliases : new List<string>();
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
        // Registry entry points (Phase 1)
        public static void RegisterWorkGiverRequirement(WorkGiverDef workGiver, StatDef stat) => SurvivalToolRegistry.RegisterWorkGiverRequirement(workGiver, stat);
        public static void RegisterWorkGiverRequirement(string workGiverDefName, string statDefName) => SurvivalToolRegistry.RegisterWorkGiverRequirement(workGiverDefName, statDefName);
        public static void RegisterJobRequirement(JobDef job, StatDef stat) => SurvivalToolRegistry.RegisterJobRequirement(job, stat);
        public static void RegisterJobRequirement(string jobDefName, string statDefName) => SurvivalToolRegistry.RegisterJobRequirement(jobDefName, statDefName);
        public static void RegisterStatAlias(StatDef stat, string alias) => SurvivalToolRegistry.RegisterStatAlias(stat, alias);
        public static void RegisterStatAlias(string statDefName, string alias) => SurvivalToolRegistry.RegisterStatAlias(statDefName, alias);

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
        public static bool IsSeparateTreeChoppingActive => SeparateTreeChoppingHelpers.IsSeparateTreeChoppingActive();
        public static bool HasTreeFellingConflict() => SeparateTreeChoppingHelpers.HasTreeFellingConflict();
        public static bool ApplyTreeFellingConflictResolution() => SeparateTreeChoppingHelpers.ApplyRecommendedResolution();
        public static List<string> GetSeparateTreeChoppingRecommendations() => SeparateTreeChoppingHelpers.GetUserRecommendations();

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

        [DebugAction("SurvivalTools", "Dump compatibility status")]
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
            var settings = SurvivalTools.Settings;
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
