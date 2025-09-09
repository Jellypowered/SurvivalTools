// RimWorld 1.6 / C# 7.3
// Source/Compatibility/CompatAPI.cs
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

using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Compat
{
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
    }

    // ---------- Public API ----------
    public static class CompatAPI
    {
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
