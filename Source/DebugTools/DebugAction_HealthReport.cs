// RimWorld 1.6 / C# 7.3
// Source/DebugTools/DebugAction_HealthReport.cs
// Consolidated one-shot health report for Survival Tools systems (Phase 9+).
// Dev-mode only; writes a single file to Desktop without spamming logs.

using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;
using SurvivalTools.Assign;
using SurvivalTools.HarmonyStuff;
using SurvivalTools.Helpers;
using System.Reflection;

namespace SurvivalTools.DebugTools
{
    internal static class DebugAction_HealthReport
    {
        // Expected declaring types that should appear among prefixes/postfixes on hotspots.
        private static readonly string[] ST_ALLOWED = new[]
        {
            "SurvivalTools.Assign.PreWork_AutoEquip",
            "SurvivalTools.Gating.WorkGiver_Gates", // if gating prefix/postfix present
            "SurvivalTools.HarmonyStuff.ITab_Gear_ST",
            "SurvivalTools.HarmonyStuff.HarmonyPatches_CacheInvalidation", // optional (only if used)
            "SurvivalTools.Assign.PostAddHooks",
            "SurvivalTools.Assign.PostEquipHooks_AddEquipment",
            "SurvivalTools.Assign.PostEquipHooks_AddEquipment+PostEquipHooks_NotifyAdded"
        };
        [LudeonTK.DebugAction("ST", "Health Report (one file)", false, false)]
        private static void Generate()
        {
            try
            {
                if (!Prefs.DevMode) return; // Silent in non-dev

                var sb = new StringBuilder(8192);
                var settings = SurvivalToolsMod.Settings;
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                // 1) Header -------------------------------------------------
                sb.AppendLine("[SurvivalTools Health Report]");
                sb.AppendLine("Generated: " + now);
                sb.AppendLine();
                sb.AppendLine("-- Header / Settings --");
                if (settings != null)
                {
                    sb.AppendLine($"AssignmentsEnabled: {settings.enableAssignments}");
                    sb.AppendLine($"HardcoreMode: {settings.hardcoreMode}  ExtraHardcoreMode: {settings.extraHardcoreMode}");
                    sb.AppendLine($"CarryLimit (mode derived): {(settings.extraHardcoreMode ? 1 : settings.hardcoreMode ? 2 : 3)}");
                    sb.AppendLine($"AlertMinTicks: {settings.toolGateAlertMinTicks}");
                }
                else sb.AppendLine("Settings: <null>");
                sb.AppendLine();

                // 2) Harmony hotspots --------------------------------------
                sb.AppendLine("-- Harmony Hotspots --");
                // Hook mode summary (exact vs fallback) for primary job start interception
                try
                {
                    var exact = AccessTools.Method(typeof(Pawn_JobTracker), "StartJob", new[] {
                        typeof(Job), typeof(JobCondition), typeof(ThinkNode), typeof(bool), typeof(bool), typeof(ThinkTreeDef),
                        typeof(JobTag?), typeof(bool), typeof(bool), typeof(bool?), typeof(bool), typeof(bool), typeof(bool) });
                    var info = exact != null ? Harmony.GetPatchInfo(exact) : null;
                    bool hasExact = info != null && info.Prefixes.Any(p => p?.PatchMethod?.DeclaringType?.FullName == "SurvivalTools.Assign.PreWork_AutoEquip");
                    bool hasOrdered = false;
                    var ordered = AccessTools.Method(typeof(Pawn_JobTracker), "TryTakeOrderedJob", new[] { typeof(Job), typeof(JobTag?), typeof(bool) });
                    if (ordered != null)
                    {
                        var oi = Harmony.GetPatchInfo(ordered);
                        hasOrdered = oi != null && oi.Prefixes.Any(p => p?.PatchMethod?.DeclaringType?.FullName == "SurvivalTools.Assign.PreWork_AutoEquip");
                    }
                    sb.AppendLine($"Hook Mode: StartJob={(hasExact ? "Exact" : "<missing>")}  TryTakeOrderedJob={(hasOrdered ? "Present" : "<missing>")}");
                }
                catch (Exception hmex)
                {
                    sb.AppendLine("Hook Mode: <error> " + hmex.Message);
                }
                // RimWorld 1.6 primary StartJob (long signature) + short fallbacks + legacy TryStartJob forms
                DumpHarmonyFor(sb, typeof(Pawn_JobTracker), "StartJob", new[] {
                    typeof(Job), typeof(JobCondition), typeof(ThinkNode), typeof(bool), typeof(bool), typeof(ThinkTreeDef),
                    typeof(JobTag?), typeof(bool), typeof(bool), typeof(bool?), typeof(bool), typeof(bool), typeof(bool) });
                DumpHarmonyFor(sb, typeof(Pawn_JobTracker), "StartJob", new[] { typeof(Job), typeof(JobTag?), typeof(bool) });
                DumpHarmonyFor(sb, typeof(Pawn_JobTracker), "StartJob", new[] { typeof(Job), typeof(bool) });
                DumpHarmonyFor(sb, typeof(Pawn_JobTracker), "TryStartJob", new[] { typeof(Job), typeof(JobTag?), typeof(bool) });
                DumpHarmonyFor(sb, typeof(Pawn_JobTracker), "TryStartJob", new[] { typeof(Job), typeof(bool) });
                DumpHarmonyFor(sb, typeof(Pawn_JobTracker), "TryTakeOrderedJob", new[] { typeof(Job), typeof(JobTag?), typeof(bool) });
                DumpHarmonyFor(sb, typeof(Pawn_JobTracker), "TryTakeOrderedJob", new[] { typeof(Job), typeof(bool) });
                DumpHarmonyFor(sb, typeof(ITab_Pawn_Gear), "FillTab", Type.EmptyTypes);
                DumpHarmonyFor(sb, typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.AddEquipment), new[] { typeof(ThingWithComps) });
                DumpHarmonyFor(sb, typeof(Pawn_EquipmentTracker), "Notify_EquipmentAdded", new[] { typeof(ThingWithComps) });
                sb.AppendLine();

                // 3) Optional-stat validator summary ----------------------
                sb.AppendLine("-- Optional Stat Validator --");
                var optStats = new[] { ST_StatDefOf.MiningYieldDigging, ST_StatDefOf.ButcheryFleshEfficiency, ST_StatDefOf.MedicalSurgerySuccessChance }.Where(s => s != null).ToList();
                var flagged = DefDatabase<WorkGiverDef>.AllDefsListForReading
                    .Where(wg => wg != null && UsesOptionalAsRequired(wg, optStats))
                    .Select(wg => wg.defName).ToList();
                sb.AppendLine($"FlaggedWorkGivers: {flagged.Count}");
                if (flagged.Count > 0)
                {
                    foreach (var name in flagged.Take(10)) sb.AppendLine("  " + name);
                    if (flagged.Count > 10) sb.AppendLine("  ... (truncated)");
                }
                sb.AppendLine();

                // 4) Bound consumables -------------------------------------
                sb.AppendLine("-- Bound Consumables --");
                int active = ST_BoundConsumables.ActiveBindingCount;
                int prunedNow = ST_BoundConsumables.PruneDriftedOnce();
                sb.AppendLine($"ActiveBindings: {active} (prunedThisRun={prunedNow})");
                if (active > 0)
                {
                    var dump = new StringBuilder(4096);
                    ST_BoundConsumables.DebugAppendDump(dump);
                    // Parse lines to extract first 10 entries after header line
                    var lines = dump.ToString().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    int shown = 0;
                    for (int i = 0; i < lines.Length && shown < 11; i++)
                    {
                        var line = lines[i];
                        if (line.StartsWith("#"))
                        {
                            sb.AppendLine(line);
                            shown++;
                        }
                    }
                    if (active > shown) sb.AppendLine("  ... (additional entries omitted)");
                }
                sb.AppendLine();

                // 5) ScoreCache stats (if available) -----------------------
                sb.AppendLine("-- Scoring Cache --");
                TryAppendScoreCache(sb);
                sb.AppendLine();

                // 6) Wear service ------------------------------------------
                sb.AppendLine("-- Wear Service --");
                TryAppendWearStats(sb);
                sb.AppendLine();

                // 7) Delivery exemption sanity -----------------------------
                sb.AppendLine("-- Delivery WorkGiver Exemptions --");
                TryAppendDeliveryExemptions(sb);
                sb.AppendLine();

                // 8) Nightmare carry invariant -----------------------------
                try
                {
                    sb.AppendLine("-- Nightmare Carry Invariant --");
                    int vio = 0, listed = 0;
                    var sbV = new StringBuilder();
                    if (settings?.extraHardcoreMode == true)
                    {
                        foreach (var p in PawnsFinder.AllMaps_FreeColonistsSpawned)
                        {
                            if (p == null || p.Dead) continue;
                            int allowed = AssignmentSearch.GetEffectiveCarryLimit(p, settings);
                            int carried = CountRealTools(p);
                            int queued = CountQueuedDrops(p);
                            if (carried > allowed && queued == 0)
                            {
                                vio++; if (listed++ < 10) sbV.AppendLine($"[NightmareCarry][VIOLATION] pawn={p.LabelShort} carried={carried} allowed={allowed} queuedDrops=0");
                            }
                        }
                    }
                    sb.AppendLine($"Nightmare carry invariant: violations={vio}");
                    // Runtime rolling window metric placeholder (counter infra not yet implemented)
                    sb.AppendLine("Nightmare carry runtime: last300ticksViolations=<n/a>");
                    if (listed > 0) sb.Append(sbV);
                    sb.AppendLine();
                }
                catch (Exception nme)
                {
                    sb.AppendLine("Nightmare carry invariant: <error> " + nme.Message);
                }

                // Write out ------------------------------------------------
                var path = ST_FileIO.WriteUtf8Atomic($"ST_HealthReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt", sb.ToString());
                try { Messages.Message("Survival Tools: wrote " + path, MessageTypeDefOf.TaskCompletion, false); } catch { }
            }
            catch (Exception ex)
            {
                Log.Warning("[SurvivalTools] Health report failed: " + ex);
            }
        }

        private static void DumpHarmonyFor(StringBuilder sb, Type type, string methodName, Type[] sig)
        {
            var m = AccessTools.Method(type, methodName, sig);
            sb.AppendLine($"--- {type.Name}.{methodName}({string.Join(",", sig.Select(s => s?.Name ?? "?"))}) ---");
            if (m == null) { sb.AppendLine("  Method not found"); return; }
            var info = Harmony.GetPatchInfo(m);
            if (info == null) { sb.AppendLine("  No patches"); return; }
            void ListPatches(string label, IList<Patch> patches)
            {
                if (patches == null) return;
                for (int i = 0; i < patches.Count; i++)
                {
                    var p = patches[i];
                    var pm = p?.PatchMethod; var dt = pm?.DeclaringType;
                    sb.AppendLine($"  [{label}] owner={p?.owner} priority={p?.priority} {dt?.FullName}::{pm?.Name}");
                }
            }
            ListPatches("PRE", info.Prefixes);
            ListPatches("POST", info.Postfixes);
            ListPatches("TRANS", info.Transpilers);

            // Allowlist integrity summary
            WriteAllowlistStatus(sb, m, info.Prefixes, info.Postfixes);
        }

        private static void WriteAllowlistStatus(StringBuilder sb, MethodBase original, IList<Patch> prefixes, IList<Patch> postfixes)
        {
            try
            {
                sb.AppendLine("  Allowlist integrity:");
                foreach (var allow in ST_ALLOWED)
                {
                    bool present = (prefixes != null && prefixes.Any(p => p?.PatchMethod?.DeclaringType?.FullName == allow)) ||
                                   (postfixes != null && postfixes.Any(p => p?.PatchMethod?.DeclaringType?.FullName == allow));
                    sb.Append("    - ").Append(allow).Append(": ").AppendLine(present ? "OK" : "MISSING");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  Allowlist integrity: <error> " + ex.Message);
            }
        }

        private static bool UsesOptionalAsRequired(WorkGiverDef wg, System.Collections.Generic.List<StatDef> optStats)
        {
            // Check extension
            var ext = wg.GetModExtension<WorkGiverExtension>()?.requiredStats;
            if (ext != null && ext.Any(s => optStats.Contains(s))) return true;
            // Registry mapping
            var req = Compat.CompatAPI.GetRequiredStatsFor(wg);
            if (req != null && req.Any(s => optStats.Contains(s))) return true;
            return false;
        }

        // Placeholder: adapt if there is an internal scoring cache type later.
        private static void TryAppendScoreCache(StringBuilder sb)
        {
            // Attempt reflective lookup for a known scoring cache class if it exists (future-proof).
            try
            {
                // Native ScoreCache (current implementation)
                var scType = AccessTools.TypeByName("SurvivalTools.Helpers.ScoreCache");
                if (scType != null)
                {
                    var getStats = scType.GetMethod("GetCacheStats", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    if (getStats != null)
                    {
                        var tuple = getStats.Invoke(null, null);
                        if (tuple != null)
                        {
                            // tuple: (int entryCount, int hits, int misses, int resolverVersion)
                            var entryCount = (int)tuple.GetType().GetField("Item1").GetValue(tuple);
                            var hits = (int)tuple.GetType().GetField("Item2").GetValue(tuple);
                            var misses = (int)tuple.GetType().GetField("Item3").GetValue(tuple);
                            var resolverVer = (int)tuple.GetType().GetField("Item4").GetValue(tuple);
                            int lookups = hits + misses;
                            float hitPct = lookups > 0 ? (hits * 100f / lookups) : 0f;
                            sb.AppendLine($"Entries={entryCount} Lookups={lookups} Hits={hits} Hit%={hitPct:F1} ResolverVer={resolverVer}");
                            return;
                        }
                    }
                }
                sb.AppendLine("Cache: <not present>");
            }
            catch (Exception ex)
            {
                sb.AppendLine("Cache: <reflection error> " + ex.Message);
            }
        }

        private static int SafeGetInt(Type t, object inst, string field)
        {
            try { return (int)(t.GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(inst) ?? 0); } catch { return 0; }
        }
        private static string SafeGetString(Type t, object inst, string field)
        {
            try { return (string)(t.GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(inst) ?? string.Empty); } catch { return string.Empty; }
        }

        private static void TryAppendWearStats(StringBuilder sb)
        {
            try
            {
                var wearType = typeof(ST_WearService);
                var cadenceField = wearType.GetField("PulseIntervalTicks", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                var cadence = cadenceField?.GetValue(null) ?? "?";
                int now = Find.TickManager?.TicksGame ?? 0;
                int last = ST_WearService.GetGlobalLastPulseTickUnsafe();
                string since = last == 0 ? "never" : (now - last) + " ticks ago";
                sb.AppendLine("PulseIntervalTicks=" + cadence + ", lastPulse=" + since);

                var lastPulseListField = wearType.GetField("_recentPulses", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                var list = lastPulseListField?.GetValue(null) as System.Collections.IEnumerable;
                if (list != null)
                {
                    int shown = 0;
                    sb.Append("RecentPulses=");
                    foreach (var item in list)
                    {
                        if (shown++ >= 10) break;
                        sb.Append(item + ",");
                    }
                    sb.AppendLine();
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("Wear: <reflection error> " + ex.Message);
            }
        }

        private static void TryAppendDeliveryExemptions(StringBuilder sb)
        {
            try
            {
                var helperType = AccessTools.TypeByName("SurvivalTools.Helpers.StatGatingHelper");
                if (helperType == null) { sb.AppendLine("No StatGatingHelper"); return; }
                var field = helperType.GetField("PureDeliveryWorkGivers", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (field == null) { sb.AppendLine("No delivery exemption registry"); return; }
                var list = field.GetValue(null) as System.Collections.IEnumerable;
                if (list == null) { sb.AppendLine("DeliveryExemptions: <none>"); return; }
                sb.Append("DeliveryExemptions=");
                int count = 0;
                foreach (var o in list) { if (count++ > 0) sb.Append(", "); sb.Append(o); }
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                sb.AppendLine("Delivery: <reflection error> " + ex.Message);
            }
        }

        // Nightmare invariant helpers
        private static int CountRealTools(Pawn pawn)
        {
            if (pawn == null) return 0;
            int c = 0;
            var inv = pawn.inventory?.innerContainer; if (inv != null)
            {
                for (int i = 0; i < inv.Count; i++) { var t = inv[i]; if (SurvivalTools.Assign.NightmareCarryEnforcer.IsCarryLimitedTool(inv[i])) c++; }
            }
            var eq = pawn.equipment?.AllEquipmentListForReading; if (eq != null)
            {
                for (int i = 0; i < eq.Count; i++) { var t = eq[i]; if (SurvivalTools.Assign.NightmareCarryEnforcer.IsCarryLimitedTool(eq[i])) c++; }
            }
            return c;
        }
        private static int CountQueuedDrops(Pawn pawn)
        {
            int qd = 0; try
            {
                var jt = pawn?.jobs; if (jt == null) return 0;
                var cur = jt.curJob; if (IsDrop(cur)) qd++;
                var q = jt.jobQueue; if (q != null)
                {
                    for (int i = 0; i < q.Count; i++) { var j = q[i]?.job; if (IsDrop(j)) qd++; }
                }
            }
            catch { }
            return qd;
            bool IsDrop(Job job) => job != null && (job.def == JobDefOf.DropEquipment || job.def == ST_JobDefOf.DropSurvivalTool);
        }
    }
}
