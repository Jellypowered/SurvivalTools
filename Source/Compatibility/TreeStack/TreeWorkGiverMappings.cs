// RimWorld 1.6 / C# 7.3
// Source/Compatibility/TreeStack/TreeWorkGiverMappings.cs
// Maps all discovered tree-chop style WorkGivers to TreeFellingSpeed & registers right-click eligibility.

using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;
using RimWorld;

namespace SurvivalTools.Compatibility.TreeStack
{
    internal static class TreeWorkGiverMappings
    {
        private static readonly List<Type> _registered = new List<Type>();
        private static readonly List<Type> _suppressed = new List<Type>();

        internal static IEnumerable<Type> RegisteredTreeWorkers() => _registered;
        internal static IEnumerable<Type> SuppressedTreeWorkers() => _suppressed;

        // Safe wrappers for health report (avoid exceptions if not initialized yet)
        internal static IEnumerable<Type> RegisteredTreeWorkersSafe()
        {
            try { return _registered.ToArray(); } catch { return Array.Empty<Type>(); }
        }
        internal static IEnumerable<Type> SuppressedTreeWorkersSafe()
        {
            try { return _suppressed.ToArray(); } catch { return Array.Empty<Type>(); }
        }
        internal static string TreeAuthorityLabelSafe()
        {
            try { return TreeSystemArbiter.Authority.ToString(); } catch { return "<error>"; }
        }

        internal static void Initialize()
        {
            try
            {
                // Discover potential worker classes (null-safe)
                var wgChopVanilla = AccessTools.TypeByName("RimWorld.WorkGiver_TreeChop")
                                      ?? AccessTools.TypeByName("RimWorld.WorkGiver_PlantsCut")
                                      ?? AccessTools.TypeByName("RimWorld.WorkGiver_PlantsCut_Designated");
                var wgChopSTC = AccessTools.TypeByName("SeparateTreeChopping.WorkGiver_ChopTrees");
                var wgChopPT = AccessTools.TypeByName("PrimitiveTools.WorkGiver_ChopTrees"); // heuristic / may be null

                var aliases = new[] { "ChopWood", "ChopTrees", "CutTrees", "FellTrees", "FellTree", "Lumber" };

                var auth = TreeSystemArbiter.Authority;

                void MapAndRegister(Type t)
                {
                    if (t == null) return;
                    Compat.CompatAPI.MapWGsToStat_ByDerivationOrAlias(ST_StatDefOf.TreeFellingSpeed, new[] { t }, aliases);
                    Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(t);
                    if (!_registered.Contains(t)) _registered.Add(t);
                }
                void Suppress(Type t)
                {
                    if (t == null) return; if (!_suppressed.Contains(t)) _suppressed.Add(t);
                }

                switch (auth)
                {
                    case TreeAuthority.SeparateTreeChopping:
                        // STC authoritative: refined discovery heuristics.
                        int found = 0;
                        var workerField = typeof(WorkGiverDef).GetField("workerClass", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        foreach (var wg in DefDatabase<WorkGiverDef>.AllDefsListForReading)
                        {
                            if (wg == null) continue;
                            var t = workerField?.GetValue(wg) as Type; if (t == null) continue;
                            string dn = wg.defName ?? string.Empty;
                            string full = t.FullName ?? string.Empty;
                            string pkg = string.Empty;
                            try { var mcp = wg.modContentPack; if (mcp != null) pkg = (mcp.PackageId ?? string.Empty).ToLowerInvariant(); } catch { }
                            bool modMatch = pkg.IndexOf("separatetreechopping", StringComparison.OrdinalIgnoreCase) >= 0;
                            bool nameMatch = (dn.IndexOf("chop", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                             (dn.IndexOf("harvesttree", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                             (dn.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                             (dn.IndexOf("plantscut", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                             (full.IndexOf("Chop", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                             (full.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                             (full.IndexOf("PlantsCut", StringComparison.OrdinalIgnoreCase) >= 0);
                            bool candidate = modMatch || (wg.workType == WorkTypeDefOf.PlantCutting && nameMatch);
                            if (!candidate) continue;
                            MapAndRegister(t);
                            found++;
                        }
                        Suppress(wgChopVanilla);
                        Suppress(wgChopPT);
                        if (found == 0)
                        {
                            // Fallback to vanilla ONLY if no STC-specific workers discovered.
                            MapAndRegister(wgChopVanilla);
                        }
                        // Explicitly map STC authority core defs for stats + right-click eligibility (idempotent if already added).
                        try
                        {
                            var wgTreesChop = DefDatabase<WorkGiverDef>.GetNamedSilentFail("TreesChop");
                            var wgPlantsCut = DefDatabase<WorkGiverDef>.GetNamedSilentFail("PlantsCut");
                            if (wgTreesChop != null)
                            {
                                var wt = workerField?.GetValue(wgTreesChop) as Type;
                                if (wt != null)
                                {
                                    Compat.CompatAPI.MapWGsToStat_ByDerivationOrAlias(ST_StatDefOf.TreeFellingSpeed, new[] { wt }, new[] { "TreesChop" });
                                    Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(wt);
                                    if (!_registered.Contains(wt)) _registered.Add(wt);
                                }
                            }
                            if (wgPlantsCut != null)
                            {
                                var wt2 = workerField?.GetValue(wgPlantsCut) as Type;
                                if (wt2 != null)
                                {
                                    // PlantsCut should map to PlantHarvestingSpeed if available; fallback to TreeFellingSpeed only if stat missing.
                                    var harvestStat = ST_StatDefOf.PlantHarvestingSpeed ?? ST_StatDefOf.TreeFellingSpeed;
                                    Compat.CompatAPI.MapWGsToStat_ByDerivationOrAlias(harvestStat, new[] { wt2 }, new[] { "PlantsCut" });
                                    Compat.CompatAPI.RegisterRightClickEligibleWGSubclass(wt2);
                                    if (!_registered.Contains(wt2)) _registered.Add(wt2);
                                }
                            }
                        }
                        catch (Exception mapEx)
                        {
                            Log.Warning("[ST][TreeStack] Explicit STC WG mapping failed: " + mapEx.Message);
                        }
                        break;
                    case TreeAuthority.PrimitiveTools_TCSS:
                        // Primitive Tools + TCSS authoritative: prefer PT, include vanilla fallback
                        MapAndRegister(wgChopPT);
                        MapAndRegister(wgChopVanilla);
                        Suppress(wgChopSTC);
                        break;
                    default: // Internal / vanilla
                        MapAndRegister(wgChopVanilla);
                        Suppress(wgChopSTC);
                        Suppress(wgChopPT);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[ST][TreeStack] Mapping init error: " + ex.Message);
            }
        }
    }
}
