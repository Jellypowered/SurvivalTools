// RimWorld 1.6 / C# 7.3
// Source/Debug/DebugAction_DumpHarmonyForMods.cs
// Phase 10 Tree Stack: Harmony patch forensics for Primitive Tools / Tree Chopping Speed Stat / Primitive Core / Separate Tree Chopping.

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using LudeonTK;
using Verse;

namespace SurvivalTools.Debugging
{
#if DEBUG
    internal static class DebugAction_DumpHarmonyForMods
    {
        // Name needles (loose, case-insensitive)
        private static readonly string[] Needles =
        {
            "Primitive Tools",
            "Tree Chopping Speed",
            "Primitive Core",
            "Separate Tree Chopping"
        };

        [DebugAction("SurvivalTools", "Dump Harmony (Tree Stack Mods)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Dump()
        {
            try
            {
                var lines = new List<string>
                {
                    $"[ST] Harmony Dump @ {DateTime.Now:HH:mm:ss}",
                    "Targets: Primitive Tools / Tree Chopping Speed Stat / Primitive Core / Separate Tree Chopping",
                    string.Empty
                };

                var running = LoadedModManager.RunningModsListForReading;
                var hints = new List<string>();
                foreach (var m in running)
                {
                    if (Needles.Any(n => (m.Name?.IndexOf(n, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0))
                    {
                        lines.Add($"Mod: {m.Name} | Pkg: {m.PackageId} | Asm: {string.Join(", ", m.assemblies.loadedAssemblies.Select(a => a.GetName().Name))}");
                        hints.Add(m.PackageId);
                        hints.Add(m.Name);
                        hints.AddRange(m.assemblies.loadedAssemblies.Select(a => a.GetName().Name));
                    }
                }
                hints = hints.Where(h => !string.IsNullOrEmpty(h)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                lines.Add(string.Empty);

                var all = Harmony.GetAllPatchedMethods().ToList();
                lines.Add($"Patched methods total: {all.Count}");
                foreach (var method in all)
                {
                    var info = Harmony.GetPatchInfo(method);
                    if (info == null) continue;
                    bool owned = false;

                    void EmitOwned(System.Collections.ObjectModel.ReadOnlyCollection<HarmonyLib.Patch> p, string kind)
                    {
                        if (p == null) return;
                        foreach (var patch in p)
                        {
                            if (hints.Any(h => patch.owner?.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                if (!owned)
                                {
                                    lines.Add($"== {method.DeclaringType?.FullName}::{method.Name} ==");
                                    owned = true;
                                }
                                lines.Add($"  [{kind}] owner={patch.owner} prio={patch.priority} after={string.Join(",", patch.after)} before={string.Join(",", patch.before)}");
                            }
                        }
                    }

                    EmitOwned(info.Prefixes, "prefix");
                    EmitOwned(info.Postfixes, "postfix");
                    EmitOwned(info.Transpilers, "transpiler");
                    EmitOwned(info.Finalizers, "finalizer");
                }

                ST_FileIO.WriteUtf8Atomic("ST_Harmony_TreeStack.txt", string.Join("\n", lines));
                Log.Message("[ST] Wrote ST_Harmony_TreeStack.txt");
            }
            catch (Exception e)
            {
                Log.Error("[ST] Harmony dump failed: " + e);
            }
        }
    }
#endif
}
