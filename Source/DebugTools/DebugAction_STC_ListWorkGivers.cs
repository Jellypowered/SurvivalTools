// RimWorld 1.6 / C# 7.3
// Source/DebugTools/DebugAction_STC_ListWorkGivers.cs
// Dumps all WorkGiverDefs to log + file with tree suspicion column.

using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using RimWorld;
using Verse;
using LudeonTK;

namespace SurvivalTools.DebugTools
{
    internal static class DebugAction_STC_ListWorkGivers
    {
        [DebugAction("Survival Tools", "Dump WorkGivers (full)", allowedGameStates = AllowedGameStates.Playing)]
        private static void Dump()
        {
            if (!Prefs.DevMode) return;
            try
            {
                var all = DefDatabase<WorkGiverDef>.AllDefsListForReading;
                var list = new List<WorkGiverDef>(all);
                list.Sort((a, b) => string.Compare(SafePkg(a), SafePkg(b), StringComparison.OrdinalIgnoreCase));
                var sb = new StringBuilder(8192);
                int scanners = 0, treeLike = 0;
                foreach (var wg in list)
                {
                    if (wg == null) continue;
                    var workerField = typeof(WorkGiverDef).GetField("workerClass", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    var wc = workerField?.GetValue(wg) as Type;
                    bool isScanner = wc != null && typeof(WorkGiver_Scanner).IsAssignableFrom(wc);
                    if (isScanner) scanners++;
                    string dn = wg.defName ?? string.Empty;
                    string full = wc?.FullName ?? "null";
                    bool tree = (dn.IndexOf("chop", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                            (dn.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                            (dn.IndexOf("harvesttree", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                            (dn.IndexOf("plantscut", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                            (full.IndexOf("Chop", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                            (full.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                            (full.IndexOf("PlantsCut", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (tree) treeLike++;
                    sb.Append("def=").Append(dn)
                        .Append(", type=").Append(full)
                        .Append(", workType=").Append(wg.workType?.defName ?? "null")
                        .Append(", mod=").Append(SafePkg(wg))
                        .Append(", scanner=").Append(isScanner ? "Yes" : "No")
                        .Append(", tree=").Append(tree ? "Yes" : "No").AppendLine();
                }
                Log.Message($"[STC.Debug] WorkGivers (count={list.Count}, scanners={scanners}, treeLike={treeLike})");
                string content = sb.ToString();
                try
                {
                    string file = $"SurvivalTools_WorkGivers_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                    var path = ST_FileIO.WriteUtf8Atomic(file, content);
                    Log.Message("[STC.Debug] Wrote WG dump: " + path);
                }
                catch (Exception ioex) { Log.Warning("[STC.Debug] Write file failed: " + ioex.Message); }
            }
            catch (Exception ex)
            {
                Log.Warning("[STC.Debug] WG dump failed: " + ex.Message);
            }
        }

        private static string SafePkg(WorkGiverDef wg)
        {
            try { return (wg?.modContentPack?.PackageId ?? "null").ToLowerInvariant(); } catch { return "null"; }
        }
    }
}