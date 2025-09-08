// RimWorld 1.6 / C# 7.3
// Source/Compatibility/PrimitiveToolsCompat.cs
//
// Primitive Tools compatibility module for SurvivalTools
// Robust integration with VBY's Primitive Tools
// TODO: Expand Primitive Tools compatibility once PT is fully updated for RimWorld 1.6.
// For now: detection + minimal integration only (safe no-op otherwise).

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SurvivalTools.Compat.PrimitiveTools
{
    public static class PrimitiveToolsCompat
    {
        private static bool? _isActive;
        private static HashSet<ThingDef> _primitiveToolDefs;
        private static readonly object _lock = new object();

        // Known/fallback defNames (kept from your original)
        private static readonly string[] KnownPrimitiveToolDefNames =
        {
            "VBY_BoneShovel",
            "VBY_FlintAxe",
            "VBY_FlintHandAxe",
            "VBY_BoneSaw",
            "VBY_FlintSickle",
            "VBY_HammerStone",
            "VBY_StoneHammer",
            "VBY_StoneMallet"
        };

        // Package ID fingerprints (normalized)
        private static readonly string[] PackageIdHints =
        {
            "vby.primitivetools",        // canonical-ish
            "primitivetools",            // generic substring
            "virusbombay.primitivetools" // safety alias if mod renames
        };

        #region Cache control

        public static void ResetCaches()
        {
            lock (_lock)
            {
                _isActive = null;
                _primitiveToolDefs = null;
            }
        }

        #endregion

        #region Core Detection

        public static bool IsPrimitiveToolsActive()
        {
            if (_isActive.HasValue) return _isActive.Value;

            lock (_lock)
            {
                if (_isActive.HasValue) return _isActive.Value;

                try
                {
                    bool foundMod = false;

                    // 1) Identifier-based probe (fast path)
                    try
                    {
                        // Prefer ModLister API when available; fall back to ModsConfig enumeration
                        // Guarded in try/catch for cross-version safety.
                        var mods = ModsConfig.ActiveModsInLoadOrder;
                        foreach (var m in mods)
                        {
                            if (m == null) continue;

                            var pid = (m.PackageId ?? string.Empty).ToLowerInvariant();
                            var name = (m.Name ?? string.Empty).ToLowerInvariant();

                            if (PackageIdHints.Any(h => pid.Contains(h)) ||
                                name.Contains("primitive tools"))
                            {
                                foundMod = true;
                                break;
                            }
                        }
                    }
                    catch { /* swallow */ }

                    // 2) Content verification: any ThingDef from the suspected pack?
                    bool contentMatches = false;
                    if (foundMod)
                    {
                        try
                        {
                            var anyFromPack = DefDatabase<ThingDef>.AllDefsListForReading.Any(d =>
                            {
                                var pkg = d?.modContentPack?.PackageId?.ToLowerInvariant() ?? "";
                                return PackageIdHints.Any(h => pkg.Contains(h)) ||
                                       (d.defName?.StartsWith("VBY_", StringComparison.OrdinalIgnoreCase) ?? false);
                            });

                            contentMatches = anyFromPack;
                        }
                        catch { contentMatches = false; }
                    }

                    // 3) Final verification: at least one known def exists (super cheap and stable)
                    bool hasKnownDef = KnownPrimitiveToolDefNames.Any(n => DefDatabase<ThingDef>.GetNamedSilentFail(n) != null);

                    _isActive = (foundMod && (contentMatches || hasKnownDef));

                    if (ST_Logging.IsCompatLogging() && ST_Logging.IsDebugLoggingEnabled)
                        ST_Logging.LogCompatMessage($"[PT-Compat] Active={_isActive} (foundMod={foundMod}, content={contentMatches}, knownDef={hasKnownDef})");

                    return _isActive.Value;
                }
                catch (Exception ex)
                {
                    Log.Error($"[SurvivalTools PT-Compat] Exception during detection: {ex}");
                    _isActive = false;
                    return false;
                }
            }
        }

        #endregion

        #region Tool Discovery

        public static List<ThingDef> GetPrimitiveToolDefs()
        {
            if (_primitiveToolDefs != null) return _primitiveToolDefs.ToList();

            lock (_lock)
            {
                if (_primitiveToolDefs != null) return _primitiveToolDefs.ToList();

                _primitiveToolDefs = new HashSet<ThingDef>();

                if (!IsPrimitiveToolsActive())
                    return _primitiveToolDefs.ToList();

                try
                {
                    // A) Dynamic discovery by content pack + tool identity
                    var all = DefDatabase<ThingDef>.AllDefsListForReading;
                    for (int i = 0; i < all.Count; i++)
                    {
                        var def = all[i];
                        if (def == null) continue;

                        var pkg = def.modContentPack?.PackageId?.ToLowerInvariant() ?? "";
                        bool fromPT = PackageIdHints.Any(h => pkg.Contains(h)) ||
                                      (def.defName?.StartsWith("VBY_", StringComparison.OrdinalIgnoreCase) ?? false);
                        if (!fromPT) continue;

                        // Treat as a PT survival tool if:
                        //  - Has SurvivalToolProperties extension, OR
                        //  - thingClass is SurvivalTool (or subclass).
                        bool looksLikeTool =
                            def.GetModExtension<SurvivalToolProperties>() != null ||
                            (def.thingClass != null && typeof(SurvivalTool).IsAssignableFrom(def.thingClass));

                        if (looksLikeTool)
                            _primitiveToolDefs.Add(def);
                    }

                    // B) Fallback: include explicitly-known defs
                    foreach (var name in KnownPrimitiveToolDefNames)
                    {
                        var d = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                        if (d != null) _primitiveToolDefs.Add(d);
                    }

                    if (ST_Logging.IsCompatLogging() && ST_Logging.IsDebugLoggingEnabled)
                        ST_Logging.LogCompatMessage($"[PT-Compat] Discovered {_primitiveToolDefs.Count} primitive tool defs");

                    return _primitiveToolDefs.ToList();
                }
                catch (Exception ex)
                {
                    Log.Error($"[SurvivalTools PT-Compat] Exception while gathering defs: {ex}");
                    return _primitiveToolDefs.ToList();
                }
            }
        }

        public static bool IsPrimitiveTool(ThingDef def)
        {
            if (!IsPrimitiveToolsActive() || def == null) return false;

            var set = _primitiveToolDefs;
            if (set == null)
            {
                // Lazy hydrate outside lock (safe enough), then re-check under lock if still null.
                var _ = GetPrimitiveToolDefs();
                set = _primitiveToolDefs;
            }

            if (set != null && set.Contains(def)) return true;

            // One-shot heuristic if cache missed (no allocation)
            try
            {
                bool nameHit = def.defName?.StartsWith("VBY_", StringComparison.OrdinalIgnoreCase) == true;
                bool hasExt = def.GetModExtension<SurvivalToolProperties>() != null;
                bool clsOk = def.thingClass != null && typeof(SurvivalTool).IsAssignableFrom(def.thingClass);

                return nameHit && (hasExt || clsOk);
            }
            catch { return false; }
        }

        public static bool IsPrimitiveTool(Thing thing) => thing?.def != null && IsPrimitiveTool(thing.def);

        #endregion

        #region Pawn Tool Analysis

        public static bool PawnHasPrimitiveTools(Pawn pawn)
        {
            if (!IsPrimitiveToolsActive() || pawn == null) return false;

            try
            {
                // Equipped
                var eq = pawn.equipment?.Primary;
                if (eq != null && IsPrimitiveTool(eq)) return true;

                // Inventory
                var inv = pawn.inventory?.innerContainer;
                if (inv != null)
                {
                    for (int i = 0; i < inv.Count; i++)
                        if (IsPrimitiveTool(inv[i])) return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools PT-Compat] Exception checking pawn tools: {ex}");
            }

            return false;
        }

        public static List<Thing> GetPawnPrimitiveTools(Pawn pawn)
        {
            var result = new List<Thing>();
            if (!IsPrimitiveToolsActive() || pawn == null) return result;

            try
            {
                var eq = pawn.equipment?.Primary;
                if (eq != null && IsPrimitiveTool(eq)) result.Add(eq);

                var inv = pawn.inventory?.innerContainer;
                if (inv != null)
                {
                    for (int i = 0; i < inv.Count; i++)
                    {
                        var t = inv[i];
                        if (IsPrimitiveTool(t)) result.Add(t);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools PT-Compat] Exception collecting pawn tools: {ex}");
            }

            return result;
        }

        #endregion

        #region Tool Assignment Integration

        public static bool ShouldOptimizeForPrimitiveTools()
        {
            return IsPrimitiveToolsActive() && SurvivalTools.Settings?.toolOptimization == true;
        }

        /// <summary>
        /// Get stat defs that Primitive Tools contribute to.
        /// Returns only valid StatDefs (null-safe).
        /// </summary>
        public static List<StatDef> GetPrimitiveToolStats()
        {
            var stats = new List<StatDef>();

            if (!IsPrimitiveToolsActive())
                return stats;

            void AddSafe(StatDef def)
            {
                if (def != null) stats.Add(def);
            }

            // Custom ST defs
            AddSafe(ST_StatDefOf.DiggingSpeed);
            AddSafe(ST_StatDefOf.MiningYieldDigging);
            AddSafe(ST_StatDefOf.PlantHarvestingSpeed);
            AddSafe(ST_StatDefOf.TreeFellingSpeed);
            AddSafe(ST_StatDefOf.MedicalOperationSpeed);
            AddSafe(ST_StatDefOf.MedicalSurgerySuccessChance);

            // Vanilla defs
            AddSafe(StatDefOf.ConstructionSpeed);
            AddSafe(StatDefOf.ConstructSuccessChance);

            return stats;
        }



        #endregion

        #region Conflict Resolution

        public static List<string> CheckForConflicts()
        {
            var conflicts = new List<string>();
            if (!IsPrimitiveToolsActive()) return conflicts;

            try
            {
                var defs = GetPrimitiveToolDefs();
                for (int i = 0; i < defs.Count; i++)
                {
                    var d = defs[i];
                    if (d == null) continue;

                    var hasExt = d.GetModExtension<SurvivalToolProperties>() != null;
                    var isSTClass = d.thingClass != null && typeof(SurvivalTool).IsAssignableFrom(d.thingClass);

                    if (!hasExt && !isSTClass)
                        conflicts.Add($"{d.defName} missing SurvivalToolProperties and not a SurvivalTool thingClass");

                    if (hasExt)
                    {
                        var ext = d.GetModExtension<SurvivalToolProperties>();
                        if (ext.baseWorkStatFactors == null || ext.baseWorkStatFactors.Count == 0)
                            conflicts.Add($"{d.defName} has SurvivalToolProperties but no baseWorkStatFactors");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools PT-Compat] Exception in conflict check: {ex}");
                conflicts.Add($"Exception during conflict check: {ex.Message}");
            }

            return conflicts;
        }

        #endregion
    }
}
