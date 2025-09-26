// RimWorld 1.6 / C# 7.3
// Source/Helpers/ST_Logging.cs
// The central logging utility for SurvivalTools.
// Provides debug logging toggles, deduplication, cooldowns,
// buffered aggregation, and specialized loggers for key systems. This should be used EVERYWHERE logging is needed.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools
{
    internal static class ST_Logging
    {
        #region Settings & flags

        private static bool? _debugLoggingCache;

        internal static bool IsDebugLoggingEnabled
        {
            get
            {
#if DEBUG
                if (_debugLoggingCache == null)
                {
                    try
                    {
                        _debugLoggingCache = SurvivalTools.Settings?.debugLogging ?? false;
                    }
                    catch
                    {
                        return false;
                    }
                }
                return _debugLoggingCache.Value;
#else
                return false;
#endif
            }
        }

        internal static bool IsCompatLogging()
        {
#if DEBUG
            return SurvivalTools.Settings?.compatLogging ?? false;
#else
            return false;
#endif
        }

        internal static void InvalidateDebugLoggingCache() => _debugLoggingCache = null;

        // Enable dedup only in DevMode and when at least one toggle is active
        private static bool DedupEnabled =>
            Prefs.DevMode && (IsDebugLoggingEnabled || IsCompatLogging());

        #endregion

        #region ToolGate deduped logging (Part B)

        // Compact key: "pawnId|jobDef|statDef"
        private struct LogBucket
        {
            public int lastTick;
            public int suppressedCount;
        }

        private static readonly Dictionary<string, LogBucket> _toolGateBuckets = new Dictionary<string, LogBucket>(512);
        private const int TOOLGATE_SUPPRESS_TICKS = 250; // suppress identical entries within this window
        private const int TOOLGATE_REPORT_TICKS = 2500; // periodic flush threshold for reporting

        /// <summary>
        /// Log a ToolGate event with deduplication. Emits the first event immediately (unless debug off), then
        /// suppresses repeated identical events for TOOLGATE_SUPPRESS_TICKS. When suppressed events exist and the
        /// cooldown expires, emits a summary line indicating how many events were suppressed.
        /// If DebugMode is enabled in settings, suppression is bypassed and all events are logged.
        /// </summary>
        internal static void LogToolGateEvent(Pawn pawn, JobDef jobDef, StatDef statDef, string reason)
        {
            // jobDef may be null (we'll include a placeholder in messages). pawn and statDef are required.
            if (pawn == null || statDef == null) return;

            // If debug logging is globally disabled, don't do any heavy work
            bool debugOn = IsDebugLoggingEnabled;
            if (!debugOn)
            {
                // Still allow a minimal non-debug message in release runs (rare)
                return;
            }

            // Developer override: if debug logging flag is true, bypass suppression
            if (SurvivalTools.Settings != null && SurvivalTools.Settings.debugLogging)
            {
                try { Log.Message($"[SurvivalTools.ToolGate] {pawn.LabelShort} denied {jobDef.defName} ({statDef.defName}) - {reason}"); } catch { }
                return;
            }

            int now = 0;
            try { now = Find.TickManager?.TicksGame ?? 0; } catch { }

            string jobName = jobDef?.defName ?? "<noJob>";
            string key = pawn.ThingID + "|" + jobName + "|" + (statDef.defName ?? "null");

            LogBucket bucket;
            if (!_toolGateBuckets.TryGetValue(key, out bucket))
            {
                // First occurrence: log immediately and record tick
                try { Log.Message($"[SurvivalTools.ToolGate] {pawn.LabelShort} denied {jobName} ({statDef.defName}) - {reason}"); } catch { }
                bucket.lastTick = now;
                bucket.suppressedCount = 0;
                _toolGateBuckets[key] = bucket;
                return;
            }

            // If within suppression window, increment counter and return
            if (now - bucket.lastTick < TOOLGATE_SUPPRESS_TICKS)
            {
                bucket.suppressedCount++;
                _toolGateBuckets[key] = bucket;
                return;
            }

            // Suppression window expired: if we suppressed events, emit a summary, then log the current event
            if (bucket.suppressedCount > 0)
            {
                try
                {
                    Log.Message($"[SurvivalTools.ToolGate] {pawn.LabelShort} denied {jobName} ({statDef.defName}) — suppressed {bucket.suppressedCount} similar events.");
                }
                catch { }
                bucket.suppressedCount = 0;
            }

            // Log the current event and update lastTick
            try { Log.Message($"[SurvivalTools.ToolGate] {pawn.LabelShort} denied {jobName} ({statDef.defName}) - {reason}"); } catch { }
            bucket.lastTick = now;
            _toolGateBuckets[key] = bucket;
        }

        /// <summary>
        /// Called periodically (e.g., in ST_LogRunner.GameComponentUpdate) to flush stale buckets
        /// and emit summary messages for suppressed counts that haven't been reported yet.
        /// </summary>
        internal static void TickToolGateBuckets()
        {
            if (!IsDebugLoggingEnabled) return;
            int now = 0;
            try { now = Find.TickManager?.TicksGame ?? 0; } catch { }

            var toReport = new List<string>();
            foreach (var kv in _toolGateBuckets)
            {
                var k = kv.Key;
                var b = kv.Value;
                if (b.suppressedCount > 0 && now - b.lastTick >= TOOLGATE_SUPPRESS_TICKS)
                {
                    toReport.Add(k);
                }
                else if (now - b.lastTick >= TOOLGATE_REPORT_TICKS)
                {
                    // Stale bucket: purge
                    toReport.Add(k);
                }
            }

            for (int i = 0; i < toReport.Count; i++)
            {
                var k = toReport[i];
                if (!_toolGateBuckets.TryGetValue(k, out var b)) continue;
                if (b.suppressedCount > 0)
                {
                    // Key format: pawnId|jobDef|statDef
                    var parts = k.Split('|');
                    string pawnId = parts.Length > 0 ? parts[0] : "?";
                    string job = parts.Length > 1 ? parts[1] : "?";
                    string stat = parts.Length > 2 ? parts[2] : "?";
                    try { Log.Message($"[SurvivalTools.ToolGate] {pawnId} denied {job} ({stat}) — suppressed {b.suppressedCount} similar events."); } catch { }
                }
                _toolGateBuckets.Remove(k);
            }
        }

        #endregion

        #region Diagnostics

        public static void LogStatPartSummary(Pawn pawn, StatDef stat, JobDef job, string toolLabel, float factor, string context = null)
        {
            if (!IsDebugLoggingEnabled) return;
            string pawnLabel = pawn?.LabelShort ?? "null";
            string statLabel = stat?.defName ?? "null";
            string jobLabel = job?.defName ?? "null";
            string key = $"StatPartSummary|{pawn?.ThingID ?? "null"}|{statLabel}|{jobLabel}";
            string msg = $"[SurvivalTools] StatPart: pawn={pawnLabel} job={jobLabel} stat={statLabel} tool={toolLabel} factor={factor:F2}{(context != null ? " " + context : "")}";
            LogDebug(msg, key, true);
        }

        public static void DumpStatDiag(Pawn pawn, StatDef stat, string jobContext = null, bool includeBestTool = true)
        {
            if (!IsDebugLoggingEnabled || pawn == null || stat == null)
                return;

            string pawnId = pawn.ThingID ?? "null";
            string statName = stat.defName ?? "null";
            string jobLabel = jobContext ?? (pawn.CurJob?.def?.defName ?? "null");

            string key = $"DumpStatDiag|{pawnId}|{statName}|{jobLabel}";
            if (!ShouldLogWithCooldown(key))
                return;

            try
            {
                float raw = pawn.GetStatValue(stat, applyPostProcess: false);
                float post = pawn.GetStatValue(stat, applyPostProcess: true);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[SurvivalTools.StatDiag] pawn={pawn.LabelShort} job={jobLabel} stat={statName}");
                sb.AppendLine($"  base={stat.defaultBaseValue:F3}, raw={raw:F3}, post={post:F3}");

                if (stat.parts != null && stat.parts.Count > 0)
                {
                    sb.AppendLine($"  parts ({stat.parts.Count}):");
                    for (int i = 0; i < stat.parts.Count; i++)
                        sb.AppendLine($"   [{i}] {stat.parts[i].GetType().FullName}");
                }

                if (includeBestTool)
                {
                    var bestTool = pawn.GetBestSurvivalTool(stat);
                    if (bestTool != null)
                    {
                        float factor = bestTool.WorkStatFactors?
                            .FirstOrDefault(m => m.stat == stat)?.value ?? 0f;
                        sb.AppendLine($"  bestTool={bestTool.LabelCapNoCount} factor={factor}");
                    }
                    else
                    {
                        sb.AppendLine("  bestTool=(none)");
                    }
                }

                LogDebug(sb.ToString(), key, respectCooldown: true);
            }
            catch (Exception ex)
            {
                LogWarning($"[SurvivalTools.StatDiag] Exception while dumping {statName} for {pawn.LabelShort}: {ex}");
            }
        }

        #endregion

        #region Cooldown & per-pawn-per-job gates

        private static readonly Dictionary<string, int> _lastLoggedTick = new Dictionary<string, int>();
        private const int LOG_COOLDOWN_TICKS = 2500;

        internal static bool ShouldLog(string logKey, bool respectCooldown = true)
        {
            if (!IsDebugLoggingEnabled) return false;
            if (!respectCooldown || string.IsNullOrEmpty(logKey)) return true;

            int now = 0;
            try
            {
                if (Find.TickManager != null)
                    now = Find.TickManager.TicksGame;
            }
            catch { return false; }

            int last;
            if (_lastLoggedTick.TryGetValue(logKey, out last) && now - last < LOG_COOLDOWN_TICKS)
                return false;

            _lastLoggedTick[logKey] = now;
            return true;
        }

        internal static bool ShouldLogWithCooldown(string logKey) => ShouldLog(logKey, true);

        private static readonly Dictionary<Pawn, HashSet<JobDef>> _loggedJobsPerPawn =
            new Dictionary<Pawn, HashSet<JobDef>>();

        internal static bool ShouldLogJobForPawn(Pawn pawn, JobDef jobDef)
        {
            if (pawn == null || jobDef == null) return false;
            CleanupJobLoggingCache(pawn);

            HashSet<JobDef> set;
            if (!_loggedJobsPerPawn.TryGetValue(pawn, out set))
            {
                set = new HashSet<JobDef>();
                _loggedJobsPerPawn[pawn] = set;
            }

            if (set.Contains(jobDef)) return false;
            set.Add(jobDef);
            return true;
        }

        private static void CleanupJobLoggingCache(Pawn pawn)
        {
            if (pawn?.CurJob?.def == null) return;

            HashSet<JobDef> set;
            if (_loggedJobsPerPawn.TryGetValue(pawn, out set) && !set.Contains(pawn.CurJob.def))
                set.Clear();
        }

        #endregion

        #region Buffer & aggregation

        private enum LogLevel { Message, Warning, Error }

        private sealed class BufferedEntry
        {
            public string text;
            public int count;
            public float createdAtMs;
            public float dueAtMs;
            public LogLevel level;
        }

        private static readonly object _bufLock = new object();
        private static readonly Dictionary<string, BufferedEntry> _buffer =
            new Dictionary<string, BufferedEntry>(256);

        private const float BUFFER_MS = 1000f;
        private const float MAX_HOLD_MS = 2000f;
        private const int MAX_ENTRIES = 512;

        private static float NowMs => Time.realtimeSinceStartup * 1000f;

        private static void EnqueueBuffered(string formatted, LogLevel level)
        {
            if (string.IsNullOrEmpty(formatted))
                return;

            if (!DedupEnabled)
            {
                Emit(formatted, level);
                return;
            }

            var now = NowMs;
            lock (_bufLock)
            {
                BufferedEntry entry;
                if (_buffer.TryGetValue(formatted, out entry))
                {
                    entry.count++;
                    var cap = entry.createdAtMs + MAX_HOLD_MS;
                    var next = now + BUFFER_MS;
                    entry.dueAtMs = next < cap ? next : cap;
                }
                else
                {
                    if (_buffer.Count >= MAX_ENTRIES)
                        FlushOldest_NoLock();

                    _buffer[formatted] = new BufferedEntry
                    {
                        text = formatted,
                        count = 1,
                        createdAtMs = now,
                        dueAtMs = now + BUFFER_MS,
                        level = level
                    };
                }
            }
        }

        private static void FlushOldest_NoLock()
        {
            if (_buffer.Count == 0) return;

            string oldestKey = null;
            float oldestCreated = float.MaxValue;

            foreach (var kv in _buffer)
            {
                var e = kv.Value;
                if (e.createdAtMs < oldestCreated)
                {
                    oldestCreated = e.createdAtMs;
                    oldestKey = kv.Key;
                }
            }

            if (oldestKey != null)
            {
                var e = _buffer[oldestKey];
                var line = e.count > 1 ? $"{e.text} (Logged {e.count}x)" : e.text;
                _buffer.Remove(oldestKey);
                Emit(line, e.level);
            }
        }

        internal static void TickBuffered()
        {
            if (!DedupEnabled) return;

            var now = NowMs;
            List<string> ready = null;

            lock (_bufLock)
            {
                foreach (var kv in _buffer)
                {
                    var e = kv.Value;
                    if (now >= e.dueAtMs || now >= e.createdAtMs + MAX_HOLD_MS)
                    {
                        if (ready == null) ready = new List<string>(8);
                        ready.Add(kv.Key);
                    }
                }

                if (ready != null)
                {
                    for (int i = 0; i < ready.Count; i++)
                    {
                        var key = ready[i];
                        var e = _buffer[key];
                        var line = e.count > 1 ? $"{e.text} (Logged {e.count}x)" : e.text;
                        _buffer.Remove(key);
                        Emit(line, e.level);
                    }
                }
            }
        }

        private static void Emit(string text, LogLevel level)
        {
            try
            {
                switch (level)
                {
                    case LogLevel.Warning: Log.Warning(text); break;
                    case LogLevel.Error: Log.Error(text); break;
                    default: Log.Message(text); break;
                }
            }
            catch { }
        }

        #endregion

        #region Public logging API

        internal static void LogDebug(string message, string logKey = null, bool respectCooldown = true)
        {
            if (!IsDebugLoggingEnabled) return;
            if (logKey != null && !ShouldLog(logKey, respectCooldown)) return;
            EnqueueBuffered(message, LogLevel.Message);
        }

        /// <summary>
        /// Emit a compact summary of a pawn's job queue for diagnostics.
        /// Uses a cooldown keyed by pawn and tag to avoid spam. Max 20 entries.
        /// </summary>
        internal static void LogJobQueueSummary(Pawn pawn, string tag)
        {
            if (!IsDebugLoggingEnabled) return;
            try
            {
                var jq = pawn?.jobs?.jobQueue;
                string key = $"ST.JobQueueSummary|{pawn?.ThingID}|{tag}";
                if (!ShouldLog(key, respectCooldown: true)) return;

                if (jq == null)
                {
                    LogDebug($"[SurvivalTools.JobQueue][{tag}] jobQueue=null", key);
                    return;
                }
                int count = jq.Count;
                if (count == 0)
                {
                    LogDebug($"[SurvivalTools.JobQueue][{tag}] jobQueue count=0", key);
                    return;
                }
                int maxEntries = 20;
                int shown = Math.Min(count, maxEntries);
                var sb = new System.Text.StringBuilder(64 + shown * 24);
                sb.Append($"[SurvivalTools.JobQueue][{tag}] jobQueue count={count} :: ");
                for (int i = 0; i < shown; i++)
                {
                    var item = jq[i];
                    var def = item?.job?.def?.defName ?? "(null)";
                    var j = item?.job;
                    string target = "(n/a)";
                    if (j != null)
                    {
                        if (j.targetA.HasThing)
                        {
                            var t = j.targetA.Thing;
                            target = $"{t.LabelShort}#{t.thingIDNumber}";
                        }
                        else if (j.targetA.Cell.IsValid)
                        {
                            target = j.targetA.Cell.ToString();
                        }
                    }
                    if (i > 0) sb.Append(" | ");
                    sb.Append('[').Append(i).Append("] ").Append(def).Append(" -> ").Append(target);
                }
                if (count > shown) sb.Append($" | … +{count - shown} more");
                LogDebug(sb.ToString(), key);
            }
            catch (Exception ex)
            {
                LogWarning($"[SurvivalTools.JobQueue] Failed to log summary: {ex}");
            }
        }

        internal static void LogCompat(string message, string logKey = null, bool respectCooldown = true)
        {
            if (!IsDebugLoggingEnabled || !IsCompatLogging()) return;
            if (logKey != null && !ShouldLog(logKey, respectCooldown)) return;
            EnqueueBuffered(message, LogLevel.Message);
        }

        internal static void LogCompatMessage(string message, string logKey = null, bool respectCooldown = true)
        {
            if (!IsDebugLoggingEnabled || !IsCompatLogging()) return;
            if (logKey != null && !ShouldLog(logKey, respectCooldown)) return;
            EnqueueBuffered($"[SurvivalTools Compat] {message}", LogLevel.Message);
        }

        internal static void LogInfo(string message) => Emit(message, LogLevel.Message);
        internal static void LogWarning(string message) => Emit(message, LogLevel.Warning);
        internal static void LogError(string message) => Emit(message, LogLevel.Error);

        /// <summary>
        /// Raw debug logger for special-case systems (ToolResolver). Bypasses buffering,
        /// deduplication and cooldown but remains gated behind the debug-settings flag.
        /// Use only where callers must always see unthrottled debug output when DevMode+debug
        /// logging is enabled (e.g. automatic tool resolution at startup).
        /// </summary>
        internal static void LogRawDebug(string message)
        {
            if (!IsDebugLoggingEnabled) return;
            Emit(message, LogLevel.Message);
        }

        /// <summary>
        /// Decision-level logging: deduped and cooldown-aware. Intended for scoring/validation
        /// and patch decision explanations.
        /// </summary>
        internal static void LogDecision(string key, string message)
        {
            if (!IsDebugLoggingEnabled) return;
            if (string.IsNullOrEmpty(key))
            {
                EnqueueBuffered(message, LogLevel.Message);
                return;
            }
            if (!ShouldLog(key, respectCooldown: true)) return;
            EnqueueBuffered(message, LogLevel.Message);
        }

        /// <summary>
        /// Summarized debug message for AI flows (single-line summary per pawn/job).
        /// Respects cooldown keyed by pawn+job to avoid per-tool spam.
        /// </summary>
        internal static void LogDebugSummary(Pawn pawn, JobDef job, Thing chosenTool)
        {
            if (!IsDebugLoggingEnabled) return;
            string pawnId = pawn?.ThingID ?? "null";
            string jobName = job?.defName ?? "<noJob>";
            string toolLabel = chosenTool?.LabelShort ?? "(none)";
            string key = $"Summary|{pawnId}|{jobName}";
            string msg = $"[SurvivalTools] Summary: pawn={pawn?.LabelShort ?? "null"} job={jobName} tool={toolLabel}";
            LogDebug(msg, key, respectCooldown: true);
        }

        /// <summary>
        /// Summarized stat debug for evaluation chains. Emits one line per pawn+stat key.
        /// </summary>
        internal static void LogStatDebug(Pawn pawn, StatDef stat, float factor)
        {
            if (!IsDebugLoggingEnabled) return;
            if (pawn == null || stat == null) return;
            string key = $"StatDebug|{pawn.ThingID}|{stat.defName}";
            string msg = $"[SurvivalTools.StatDebug] pawn={pawn.LabelShort} stat={stat.defName} factor={factor:F3}";
            LogDebug(msg, key, respectCooldown: true);
        }

        /// <summary>
        /// Emits an info-level message but only once per supplied key (cooldown semantics).
        /// Useful for alerts and UI change logs that should not spam every tick.
        /// </summary>
        internal static void LogInfoOnce(string message, string key)
        {
            if (!IsDebugLoggingEnabled) return;
            if (string.IsNullOrEmpty(key))
            {
                Emit(message, LogLevel.Message);
                return;
            }
            if (!ShouldLog(key, respectCooldown: true)) return;
            Emit(message, LogLevel.Message);
        }

        internal static void LogCompatWarning(string message) =>
            Emit($"[SurvivalTools Compat] {message}", LogLevel.Warning);

        internal static void LogCompatError(string message) =>
            Emit($"[SurvivalTools Compat] {message}", LogLevel.Error);

        #endregion

        #region Optional: extension audit

        internal static void ExtensionLogger()
        {
            if (!IsDebugLoggingEnabled && !IsCompatLogging())
                return;

            try
            {
                var all = DefDatabase<ThingDef>.AllDefsListForReading;
                int count = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].GetModExtension<SurvivalToolProperties>() != null)
                        count++;
                }

                Emit($"[SurvivalTools] Found {count} ThingDefs with SurvivalToolProperties extension applied.", LogLevel.Message);

                for (int i = 0; i < all.Count; i++)
                {
                    var def = all[i];
                    var ext = def.GetModExtension<SurvivalToolProperties>();
                    if (ext == null) continue;

                    Emit($"[SurvivalTools] {def.defName} has SurvivalToolProperties.", LogLevel.Message);

                    if (ext.baseWorkStatFactors != null && ext.baseWorkStatFactors.Count > 0)
                    {
                        for (int j = 0; j < ext.baseWorkStatFactors.Count; j++)
                        {
                            var m = ext.baseWorkStatFactors[j];
                            if (m?.stat != null)
                                Emit($"    - {m.stat.defName}: {m.value.ToStringPercent()}", LogLevel.Message);
                        }
                    }
                    else
                    {
                        Emit("    (no baseWorkStatFactors defined)", LogLevel.Message);
                    }

                    if (ext.toolWearFactor > 0f)
                        Emit($"    - toolWearFactor: {ext.toolWearFactor}", LogLevel.Message);

                    if (ext.defaultSurvivalToolAssignmentTags != null && ext.defaultSurvivalToolAssignmentTags.Count > 0)
                    {
                        var tags = string.Join(", ", ext.defaultSurvivalToolAssignmentTags);
                        Emit($"    - defaultSurvivalToolAssignmentTags: {tags}", LogLevel.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Emit($"[SurvivalTools] ExtensionLogger failed: {e}", LogLevel.Warning);
            }
        }

        #endregion
    }

    public sealed class ST_LogRunner : GameComponent
    {
        public ST_LogRunner(Game game) : base() { }

        public override void GameComponentUpdate()
        {
            ST_Logging.TickBuffered();
            ST_Logging.TickToolGateBuckets();
        }
    }
}
