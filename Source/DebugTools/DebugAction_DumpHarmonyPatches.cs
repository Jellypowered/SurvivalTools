using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using LudeonTK;

namespace SurvivalTools.DebugTools
{
    internal static class DebugAction_DumpHarmonyPatches
    {
        [LudeonTK.DebugAction("ST", "Dump Harmony on hotspots", false, false)]
        public static void DumpHarmonyPatches()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== SurvivalTools Harmony Patch Hotspot Analysis ===");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            // Define hotspot methods that are common collision points
            var hotspots = new[]
            {
                // Job system hotspots
                new { Type = typeof(Pawn_JobTracker), Method = "TryTakeOrderedJob", Params = new[] { typeof(Job), typeof(JobTag?), typeof(bool) } },
                new { Type = typeof(Pawn_JobTracker), Method = "TryTakeOrderedJob", Params = new[] { typeof(Job), typeof(bool) } },
                new { Type = typeof(Pawn_JobTracker), Method = "TryStartJob", Params = new[] { typeof(Job), typeof(JobTag?), typeof(bool) } },
                new { Type = typeof(Pawn_JobTracker), Method = "TryStartJob", Params = new[] { typeof(Job), typeof(bool) } },
                
                // UI hotspots
                new { Type = typeof(ITab_Pawn_Gear), Method = "FillTab", Params = Type.EmptyTypes },
                
                // Tool/equipment hotspots
                new { Type = typeof(Pawn_EquipmentTracker), Method = "TryDropEquipment", Params = new[] { typeof(ThingWithComps), typeof(ThingWithComps).MakeByRefType(), typeof(IntVec3), typeof(bool) } },
                new { Type = typeof(Thing), Method = "Destroy", Params = new[] { typeof(DestroyMode) } },
                new { Type = typeof(ThingMaker), Method = "MakeThing", Params = new[] { typeof(ThingDef), typeof(ThingDef) } },
                
                // Work system hotspots  
                new { Type = typeof(WorkGiver_Scanner), Method = "HasJobOnThing", Params = new[] { typeof(Pawn), typeof(Thing), typeof(bool) } },
                new { Type = typeof(WorkGiver_PlantsCut), Method = "HasJobOnThing", Params = new[] { typeof(Pawn), typeof(Thing), typeof(bool) } },
            };

            var ourAssembly = typeof(DebugAction_DumpHarmonyPatches).Assembly;

            foreach (var hotspot in hotspots)
            {
                sb.AppendLine($"=== {hotspot.Type.Name}.{hotspot.Method} ===");

                var method = AccessTools.Method(hotspot.Type, hotspot.Method, hotspot.Params);
                if (method == null)
                {
                    sb.AppendLine("  Method not found");
                    sb.AppendLine();
                    continue;
                }

                var info = Harmony.GetPatchInfo(method);
                if (info == null)
                {
                    sb.AppendLine("  No patches found");
                    sb.AppendLine();
                    continue;
                }

                sb.AppendLine($"  Total patches: {info.Prefixes.Count} prefixes, {info.Postfixes.Count} postfixes, {info.Transpilers.Count} transpilers");
                sb.AppendLine();

                // Analyze all patches
                var allPatches = info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers);
                foreach (var patch in allPatches)
                {
                    var m = patch.PatchMethod;
                    var dt = m?.DeclaringType;
                    if (dt == null) continue;

                    var patchType = info.Prefixes.Contains(patch) ? "PREFIX" :
                                  info.Postfixes.Contains(patch) ? "POSTFIX" : "TRANSPILER";

                    bool isOurs = dt.Assembly == ourAssembly;
                    var marker = isOurs ? "[ST]" : "[EXT]";

                    sb.AppendLine($"  {marker} {patchType}: {dt.FullName}.{m.Name}");
                    sb.AppendLine($"       Owner: {patch.owner}");
                    sb.AppendLine($"       Assembly: {dt.Assembly.GetName().Name}");
                    if (isOurs && dt.Namespace != null && dt.Namespace.StartsWith("SurvivalTools", StringComparison.Ordinal))
                    {
                        sb.AppendLine($"       ST Type: {dt.Name}");
                    }
                    sb.AppendLine();
                }
            }

            // Write to desktop
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var fileName = $"SurvivalTools_HarmonyDump_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var fullPath = Path.Combine(desktopPath, fileName);

            try
            {
                File.WriteAllText(fullPath, sb.ToString());
                Log.Message($"[SurvivalTools.DebugTools] Harmony patch dump written to: {fullPath}");
                Messages.Message($"Harmony patch dump written to Desktop: {fileName}", MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools.DebugTools] Failed to write harmony dump: {ex}");
                Messages.Message($"Failed to write harmony dump: {ex.Message}", MessageTypeDefOf.RejectInput, false);
            }
        }
    }
}