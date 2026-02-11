// RimWorld 1.6 / C# 7.3
// Source/Game/GameComponent_DroppedToolTracker.cs
//
// Tracks tools that have been manually dropped for repair or disassembly.
// Prevents auto-equip systems from picking them up until the player's intent is fulfilled.

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Game
{
    /// <summary>
    /// Tracks tools dropped by player intent (repair/disassembly) to prevent auto-equip interference.
    /// Tools are tracked until repaired or destroyed, preventing memory leaks.
    /// </summary>
    public class GameComponent_DroppedToolTracker : GameComponent
    {
        private enum DropIntent
        {
            Repair,
            Disassembly
        }

        private struct DropRecord
        {
            public int thingID;           // Track by thingIDNumber for fast lookup
            public DropIntent intent;
            public int droppedTick;
            public string defName;        // For debug logging only
        }

        // Active drop records
        private Dictionary<int, DropRecord> _droppedTools = new Dictionary<int, DropRecord>();

        // Cleanup tracking - tools that have been destroyed
        private HashSet<int> _destroyedThisSession = new HashSet<int>();

        // Performance: track next cleanup tick
        private int _nextCleanupTick = 0;
        private const int CLEANUP_INTERVAL = 600; // Every 10 seconds
        private const int STALE_THRESHOLD = 60000; // 1000 seconds - very old records

        public GameComponent_DroppedToolTracker(Verse.Game game) : base()
        {
        }

        /// <summary>
        /// Mark a tool as dropped for repair. It will be blocked from auto-equip until repaired.
        /// </summary>
        public static void MarkDroppedForRepair(Thing tool)
        {
            var comp = Current.Game?.GetComponent<GameComponent_DroppedToolTracker>();
            if (comp == null || tool == null) return;

            comp._droppedTools[tool.thingIDNumber] = new DropRecord
            {
                thingID = tool.thingIDNumber,
                intent = DropIntent.Repair,
                droppedTick = Find.TickManager?.TicksGame ?? 0,
                defName = tool.def?.defName ?? "unknown"
            };

            if (IsDebugLoggingEnabled)
                LogDebug($"[DroppedTools] Marked {tool.LabelShort} for repair (ID={tool.thingIDNumber})", $"DropTrack_Repair_{tool.thingIDNumber}");
        }

        /// <summary>
        /// Mark a tool as dropped for disassembly. It will be blocked from auto-equip until destroyed.
        /// </summary>
        public static void MarkDroppedForDisassembly(Thing tool)
        {
            var comp = Current.Game?.GetComponent<GameComponent_DroppedToolTracker>();
            if (comp == null || tool == null) return;

            comp._droppedTools[tool.thingIDNumber] = new DropRecord
            {
                thingID = tool.thingIDNumber,
                intent = DropIntent.Disassembly,
                droppedTick = Find.TickManager?.TicksGame ?? 0,
                defName = tool.def?.defName ?? "unknown"
            };

            if (IsDebugLoggingEnabled)
                LogDebug($"[DroppedTools] Marked {tool.LabelShort} for disassembly (ID={tool.thingIDNumber})", $"DropTrack_Disasm_{tool.thingIDNumber}");
        }

        /// <summary>
        /// Check if a tool is currently blocked from auto-equip due to player drop intent.
        /// </summary>
        public static bool IsBlockedFromAutoEquip(Thing tool)
        {
            if (tool == null) return false;

            var comp = Current.Game?.GetComponent<GameComponent_DroppedToolTracker>();
            if (comp == null) return false;

            // Quick check: is it tracked?
            if (!comp._droppedTools.TryGetValue(tool.thingIDNumber, out var record))
                return false;

            // Check if the tool has been repaired (for repair intent)
            if (record.intent == DropIntent.Repair)
            {
                // If tool is at max HP, it's been repaired - untrack it
                if (tool.HitPoints >= tool.MaxHitPoints)
                {
                    comp._droppedTools.Remove(tool.thingIDNumber);
                    if (IsDebugLoggingEnabled)
                        LogDebug($"[DroppedTools] {tool.LabelShort} repaired, allowing auto-equip", $"DropTrack_Repaired_{tool.thingIDNumber}");
                    return false;
                }
            }

            // Tool is still being tracked - block auto-equip
            return true;
        }

        /// <summary>
        /// Explicitly untrack a tool (called externally if needed).
        /// </summary>
        public static void Untrack(Thing tool)
        {
            if (tool == null) return;

            var comp = Current.Game?.GetComponent<GameComponent_DroppedToolTracker>();
            if (comp == null) return;

            if (comp._droppedTools.Remove(tool.thingIDNumber) && IsDebugLoggingEnabled)
                LogDebug($"[DroppedTools] Untracked {tool.LabelShort}", $"DropTrack_Untrack_{tool.thingIDNumber}");
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            // Periodic cleanup
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now >= _nextCleanupTick)
            {
                _nextCleanupTick = now + CLEANUP_INTERVAL;
                PerformCleanup(now);
            }
        }

        private void PerformCleanup(int currentTick)
        {
            try
            {
                if (_droppedTools.Count == 0 && _destroyedThisSession.Count == 0)
                    return;

                var toRemove = new List<int>();

                // Check each tracked tool
                foreach (var kvp in _droppedTools)
                {
                    int thingID = kvp.Key;
                    var record = kvp.Value;

                    // If marked as destroyed this session, remove immediately
                    if (_destroyedThisSession.Contains(thingID))
                    {
                        toRemove.Add(thingID);
                        continue;
                    }

                    // Check if the thing still exists in the world
                    bool exists = ThingExists(thingID);
                    if (!exists)
                    {
                        toRemove.Add(thingID);
                        _destroyedThisSession.Add(thingID); // Remember we saw it destroyed
                        continue;
                    }

                    // Remove very stale records (safety net against bugs)
                    int age = currentTick - record.droppedTick;
                    if (age > STALE_THRESHOLD)
                    {
                        toRemove.Add(thingID);
                        if (IsDebugLoggingEnabled)
                            LogDebug($"[DroppedTools] Removing stale record for {record.defName} (age={age} ticks)", $"DropTrack_Stale_{thingID}");
                    }
                }

                // Remove flagged records
                foreach (int id in toRemove)
                {
                    _droppedTools.Remove(id);
                }

                // Cleanup destroyed session tracking (keep last 1000 entries max)
                if (_destroyedThisSession.Count > 1000)
                {
                    _destroyedThisSession.Clear();
                }

                if (toRemove.Count > 0 && IsDebugLoggingEnabled)
                    LogDebug($"[DroppedTools] Cleaned up {toRemove.Count} records. Active: {_droppedTools.Count}", "DropTrack_Cleanup");
            }
            catch (Exception ex)
            {
                LogError($"[DroppedTools] Cleanup exception: {ex}");
            }
        }

        /// <summary>
        /// Check if a thing with the given ID exists in any map.
        /// </summary>
        private bool ThingExists(int thingID)
        {
            try
            {
                if (Find.Maps == null) return false;

                foreach (var map in Find.Maps)
                {
                    if (map?.listerThings == null) continue;

                    // Quick check: if the thing is in any group, it exists
                    var allThings = map.listerThings.AllThings;
                    if (allThings != null)
                    {
                        for (int i = 0; i < allThings.Count; i++)
                        {
                            if (allThings[i]?.thingIDNumber == thingID)
                                return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();

            // Save/load the drop records
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Convert to lists for serialization
                var ids = _droppedTools.Keys.ToList();
                var intents = _droppedTools.Values.Select(r => (int)r.intent).ToList();
                var ticks = _droppedTools.Values.Select(r => r.droppedTick).ToList();
                var names = _droppedTools.Values.Select(r => r.defName).ToList();

                Scribe_Collections.Look(ref ids, "droppedToolIDs", LookMode.Value);
                Scribe_Collections.Look(ref intents, "droppedToolIntents", LookMode.Value);
                Scribe_Collections.Look(ref ticks, "droppedToolTicks", LookMode.Value);
                Scribe_Collections.Look(ref names, "droppedToolNames", LookMode.Value);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                List<int> ids = null;
                List<int> intents = null;
                List<int> ticks = null;
                List<string> names = null;

                Scribe_Collections.Look(ref ids, "droppedToolIDs", LookMode.Value);
                Scribe_Collections.Look(ref intents, "droppedToolIntents", LookMode.Value);
                Scribe_Collections.Look(ref ticks, "droppedToolTicks", LookMode.Value);
                Scribe_Collections.Look(ref names, "droppedToolNames", LookMode.Value);

                if (ids != null && intents != null && ticks != null && names != null &&
                    ids.Count == intents.Count && ids.Count == ticks.Count && ids.Count == names.Count)
                {
                    _droppedTools = new Dictionary<int, DropRecord>();
                    for (int i = 0; i < ids.Count; i++)
                    {
                        _droppedTools[ids[i]] = new DropRecord
                        {
                            thingID = ids[i],
                            intent = (DropIntent)intents[i],
                            droppedTick = ticks[i],
                            defName = names[i]
                        };
                    }
                }
            }

            // Don't save _destroyedThisSession - it's only needed during the current game session
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                _destroyedThisSession = new HashSet<int>();
            }
        }

        /// <summary>
        /// Get stats for debugging/display.
        /// </summary>
        public static string GetDebugInfo()
        {
            var comp = Current.Game?.GetComponent<GameComponent_DroppedToolTracker>();
            if (comp == null) return "Tracker not initialized";

            int repairCount = comp._droppedTools.Values.Count(r => r.intent == DropIntent.Repair);
            int disasmCount = comp._droppedTools.Values.Count(r => r.intent == DropIntent.Disassembly);

            return $"Tracked tools: {comp._droppedTools.Count} (repair: {repairCount}, disassembly: {disasmCount})";
        }
    }
}
