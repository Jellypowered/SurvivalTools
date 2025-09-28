// RimWorld 1.6 / C# 7.3
// Source/DebugTools/DebugAction_ConsolidationAndConsumables.cs
// Phase 9: Consolidation verification + Phase 8 bound consumables dump.
using System;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;
using LudeonTK;
using SurvivalTools.Assign; // For PreWork_AutoEquip
using SurvivalTools.HarmonyStuff; // For ITab_Gear_ST

namespace SurvivalTools.DebugTools
{
    internal static class DebugAction_ConsolidationAndConsumables
    {
        // Avoid direct typeof() references so we don't trip the namespace vs Mod class name collision (SurvivalTools class conflicts with root namespace token).
        private const string AllowPreWorkTypeFullName = "SurvivalTools.Assign.PreWork_AutoEquip";
        private const string AllowGearTabTypeFullName = "SurvivalTools.HarmonyStuff.ITab_Gear_ST";
        [LudeonTK.DebugAction("ST", "Dump bound consumables → Desktop", false, false)]
        private static void DumpBoundConsumables()
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("[SurvivalTools] Bound Consumables Dump");
            sb.AppendLine("===================================");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            try
            {
                Helpers.ST_BoundConsumables.DebugAppendDump(sb);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error dumping registry: {ex}");
            }

            var path = ST_FileIO.WriteUtf8Atomic($"ST_BoundConsumables_{DateTime.Now:yyyyMMdd_HHmmss}.txt", sb.ToString());
            try { Messages.Message("Survival Tools: wrote " + path, MessageTypeDefOf.TaskCompletion); } catch { }
        }

        [LudeonTK.DebugAction("ST", "Verify consolidation (patch allowlist)", false, false)]
        private static void VerifyConsolidation()
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("[SurvivalTools] Consolidation Verification");
            sb.AppendLine("========================================");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            try
            {
                // Hotspot introspection similar to existing patch dump but with allowlist emphasis.
                var hotspots = new[]
                {
                    // RimWorld 1.6 exact StartJob signature (13 params). Keep first for prominence.
                    new { Type = typeof(Pawn_JobTracker), Method = "StartJob", Params = new[] { typeof(Job), typeof(JobCondition), typeof(ThinkNode), typeof(bool), typeof(bool), typeof(ThinkTreeDef), typeof(JobTag?), typeof(bool), typeof(bool), typeof(bool), typeof(bool?), typeof(bool), typeof(bool?) } },
                    new { Type = typeof(Pawn_JobTracker), Method = "TryStartJob", Params = new[] { typeof(Job), typeof(JobTag?), typeof(bool) } },
                    new { Type = typeof(Pawn_JobTracker), Method = "TryStartJob", Params = new[] { typeof(Job), typeof(bool) } },
                    new { Type = typeof(Pawn_JobTracker), Method = "TryTakeOrderedJob", Params = new[] { typeof(Job), typeof(JobTag?), typeof(bool) } },
                    new { Type = typeof(Pawn_JobTracker), Method = "TryTakeOrderedJob", Params = new[] { typeof(Job), typeof(bool) } },
                    new { Type = typeof(ITab_Pawn_Gear), Method = "FillTab", Params = Type.EmptyTypes },
                };

                var ourAsm = typeof(DebugAction_ConsolidationAndConsumables).Assembly;

                foreach (var h in hotspots)
                {
                    sb.AppendLine($"--- {h.Type.Name}.{h.Method} ---");
                    var method = HarmonyLib.AccessTools.Method(h.Type, h.Method, h.Params);
                    if (method == null)
                    {
                        sb.AppendLine("  Method not found (version mismatch?)");
                        sb.AppendLine();
                        continue;
                    }
                    var info = Harmony.GetPatchInfo(method);
                    if (info == null)
                    {
                        sb.AppendLine("  No patches (clean)");
                        sb.AppendLine();
                        continue;
                    }
                    sb.AppendLine($"  Prefixes={info.Prefixes.Count} Postfixes={info.Postfixes.Count} Transpilers={info.Transpilers.Count}");

                    var all = info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers);
                    foreach (var patch in all)
                    {
                        var m = patch.PatchMethod;
                        var dt = m?.DeclaringType;
                        if (dt == null) continue;
                        bool isOurs = dt.Assembly == ourAsm && (dt.Namespace?.StartsWith("SurvivalTools") == true);
                        string kind = info.Prefixes.Contains(patch) ? "PREFIX" : info.Postfixes.Contains(patch) ? "POSTFIX" : "TRANS";

                        // These types are not nested inside SurvivalTools, so compare by full name to avoid occasional assembly load edge cases.
                        bool allowlisted = dt.FullName == AllowPreWorkTypeFullName || dt.FullName == AllowGearTabTypeFullName;
                        string marker = isOurs ? (allowlisted ? "[ST-ALLOW]" : "[ST-LEGACY?]") : "[EXT]";
                        sb.AppendLine($"  {marker} {kind} {dt.FullName}.{m.Name} (owner={patch.owner})");
                    }
                    sb.AppendLine();
                }

                // Summary expectations
                sb.AppendLine("Expectations:");
                sb.AppendLine("  * Only PreWork_AutoEquip & ITab_Gear_ST should remain on job/gear hotspots.");
                sb.AppendLine("  * No legacy OptimizeSurvivalTools / AutoToolPickup bodies should be active (stubbed).");
                sb.AppendLine($"  * Active bound consumable bindings: {Helpers.ST_BoundConsumables.ActiveBindingCount}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Verification failed: {ex}");
            }

            var path = ST_FileIO.WriteUtf8Atomic($"ST_ConsolidationVerify_{DateTime.Now:yyyyMMdd_HHmmss}.txt", sb.ToString());
            try { Messages.Message("Survival Tools: wrote " + path, MessageTypeDefOf.TaskCompletion); } catch { }
        }

        [LudeonTK.DebugAction("ST", "Run optional stat gating validator", false, false)]
        private static void RunOptionalStatValidator()
        {
            try
            {
                // Reuse the internal method via reflection to avoid code dup (kept internal in StaticConstructorClass)
                var method = typeof(StaticConstructorClass).GetMethod("ValidateDemotedOptionalStatsNotHardGated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                method?.Invoke(null, null);
                Messages.Message("Survival Tools: Optional stat validator executed (see log if debug enabled)", MessageTypeDefOf.TaskCompletion, false);
            }
            catch (Exception ex)
            {
                Log.Warning("[SurvivalTools] Manual optional stat validator failed: " + ex);
            }
        }
    }
}
