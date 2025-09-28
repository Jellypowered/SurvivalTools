// RimWorld 1.6 / C# 7.3
// Source/Helpers/ST_BoundConsumables.cs
// Phase 8 hotfix: Registry for per-(pawn,stat) bound consumable textile units used by virtual tools.
// Prevents deleting entire textile stacks when virtual cleaning tools wear out.

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SurvivalTools.Helpers
{
    internal static class ST_BoundConsumables
    {
        private struct Key : IEquatable<Key>
        {
            public int pawnId;
            public int statIndex;
            public bool Equals(Key other) => pawnId == other.pawnId && statIndex == other.statIndex;
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => (pawnId * 397) ^ statIndex;
        }

        private class BoundInfo
        {
            public int boundThingId;
            public int parentStackId;
            public System.WeakReference<Thing> boundRef;
            public System.WeakReference<Thing> parentRef;
        }

        private static readonly Dictionary<Key, BoundInfo> _map = new Dictionary<Key, BoundInfo>(128);

        // Debug/inspection helper: returns current entry count (cheap, no enumeration)
        public static int ActiveBindingCount => _map.Count;

        public static bool TryGetOrBind(Pawn pawn, StatDef stat, out Thing boundUnit)
        {
            boundUnit = null;
            if (pawn == null || stat == null || pawn.inventory?.innerContainer == null) return false;
            var key = new Key { pawnId = pawn.thingIDNumber, statIndex = stat.index };

            if (_map.TryGetValue(key, out var info))
            {
                if (info.boundRef != null && info.boundRef.TryGetTarget(out var existing) && existing != null && !existing.Destroyed)
                {
                    // Ownership drift: if the bound unit left the pawn's inventory (hauled, merged, dropped), unbind so we can rebind cleanly.
                    if (pawn.inventory.innerContainer.Contains(existing))
                    {
                        boundUnit = existing; return true;
                    }
                    else
                    {
                        _map.Remove(key); // stale due to drift
                    }
                }
                _map.Remove(key); // stale
            }

            // Find first eligible textile stack
            Thing stack = null;
            var container = pawn.inventory.innerContainer;
            for (int i = 0; i < container.Count; i++)
            {
                var t = container[i];
                if (t == null) continue;
                if (!EligibleTextile(t.def)) continue;
                stack = t; break;
            }
            if (stack == null) return false;

            Thing bound = stack;
            int parentId = stack.thingIDNumber;
            if (stack.stackCount > 1)
            {
                bound = stack.SplitOff(1);
                if (!container.Contains(bound)) // defensive
                {
                    if (!container.TryAdd(bound))
                    {
                        try { bound.Destroy(DestroyMode.Vanish); } catch { }
                        bound = stack; // fallback to whole stack (rare)
                    }
                }
            }

            _map[key] = new BoundInfo
            {
                boundThingId = bound.thingIDNumber,
                parentStackId = parentId,
                boundRef = new System.WeakReference<Thing>(bound),
                parentRef = new System.WeakReference<Thing>(stack)
            };
            boundUnit = bound; return true;
        }

        public static bool ShouldHide(Pawn pawn, Thing thing)
        {
            if (pawn == null || thing == null) return false;
            int id = thing.thingIDNumber;
            foreach (var kv in _map)
            {
                if (kv.Key.pawnId != pawn.thingIDNumber) continue;
                var bi = kv.Value;
                // Hide only when we split off a separate bound unit (parent != bound). If we fell back to using a single-item stack
                // (parent == bound), we must NOT hide it or the player would see nothing.
                if (bi.parentStackId == id && bi.parentStackId != bi.boundThingId)
                    return true;
            }
            return false;
        }

        public static void UnbindByThingId(int thingId)
        {
            if (_map.Count == 0) return;
            List<Key> remove = null;
            foreach (var kv in _map)
            {
                if (kv.Value.boundThingId == thingId)
                {
                    if (remove == null) remove = new List<Key>(2);
                    remove.Add(kv.Key);
                }
            }
            if (remove != null)
            {
                for (int i = 0; i < remove.Count; i++) _map.Remove(remove[i]);
            }
        }

        // INTERNAL DEBUG: Append a human-readable dump of the registry to a StringBuilder.
        // Not performance critical (dev action only). Safe if called while map is running.
        internal static void DebugAppendDump(System.Text.StringBuilder sb)
        {
            if (sb == null) return;
            sb.AppendLine($"Active bound consumable entries: {_map.Count}");
            if (_map.Count == 0) return;

            // Build quick lookup for pawns by thingIDNumber for label resolution.
            var pawnLookup = new Dictionary<int, Pawn>(128);
            try
            {
                foreach (var map in Find.Maps)
                {
                    var pawns = map.mapPawns?.AllPawnsSpawned;
                    if (pawns == null) continue;
                    for (int i = 0; i < pawns.Count; i++)
                    {
                        var p = pawns[i];
                        if (p != null) pawnLookup[p.thingIDNumber] = p;
                    }
                }
                // World pawns (in caravans, etc.)
                foreach (var p in Find.WorldPawns.AllPawnsAliveOrDead)
                {
                    if (p != null && !pawnLookup.ContainsKey(p.thingIDNumber)) pawnLookup[p.thingIDNumber] = p;
                }
            }
            catch { }

            int idx = 0;
            foreach (var kv in _map)
            {
                idx++;
                var key = kv.Key;
                var info = kv.Value;
                Thing bound = null; info.boundRef?.TryGetTarget(out bound);
                Thing parent = null; info.parentRef?.TryGetTarget(out parent);

                string pawnLabel = pawnLookup.TryGetValue(key.pawnId, out var pawn) ? pawn.LabelShortCap : $"pawnId={key.pawnId}";
                // Resolve stat by index (linear scan is fine for debug scale)
                StatDef stat = null;
                try { stat = DefDatabase<StatDef>.AllDefsListForReading.FirstOrDefault(s => s.index == key.statIndex); } catch { }

                string statName = stat?.defName ?? $"statIndex={key.statIndex}";
                string boundLabel = bound != null ? bound.LabelCap : "<null>";
                string parentLabel = parent != null ? parent.LabelCap : "<null>";
                bool hideParent = parent != null && ShouldHide(pawn, parent);

                sb.AppendLine($"#{idx}: {pawnLabel} | {statName} -> bound:{boundLabel} (id {info.boundThingId}) parent:{parentLabel} (id {info.parentStackId}) hideParent={hideParent} validBound={!(bound == null || bound.Destroyed)}");
            }
        }

        private static bool EligibleTextile(ThingDef def)
        {
            if (def == null) return false;
            if (def.IsApparel || def.IsWeapon) return false;
            if (!def.IsStuff) return false;
            if (!def.stuffProps?.categories.NullOrEmpty() == true) return false;
            bool textile = false;
            var cats = def.stuffProps.categories;
            for (int i = 0; i < cats.Count; i++)
            {
                var c = cats[i];
                if (c?.defName != null && (c.defName.Contains("Fabric") || c.defName.Contains("Textile"))) { textile = true; break; }
            }
            if (!textile) return false;
            var dn = def.defName.ToLowerInvariant();
            if (dn == "woodlog" || dn.Contains("wood")) return false;
            var ext = def.GetModExtension<SurvivalToolProperties>();
            if (ext?.baseWorkStatFactors == null) return false;
            for (int i = 0; i < ext.baseWorkStatFactors.Count; i++)
            {
                var sm = ext.baseWorkStatFactors[i];
                if (sm?.stat == ST_StatDefOf.CleaningSpeed) return true;
            }
            return false;
        }

        /// <summary>
        /// Lightweight maintenance: remove any entries whose bound thing is no longer in its pawn's inventory.
        /// Can be called on a slow tick if future drift scenarios emerge beyond TryGetOrBind usage.
        /// </summary>
        internal static void PruneDrifted()
        {
            if (_map.Count == 0) return;
            List<Key> remove = null;
            foreach (var kv in _map)
            {
                var k = kv.Key; var bi = kv.Value;
                Thing bound = null; bi.boundRef?.TryGetTarget(out bound);
                if (bound == null || bound.Destroyed)
                {
                    if (remove == null) remove = new List<Key>(4);
                    remove.Add(k); continue;
                }
                // Resolve pawn by id (fast scan across maps/world optional; only when needed)
                Pawn pawn = null;
                try
                {
                    foreach (var map in Find.Maps)
                    {
                        var pList = map.mapPawns?.AllPawnsSpawned; if (pList == null) continue;
                        for (int i = 0; i < pList.Count; i++) if (pList[i]?.thingIDNumber == k.pawnId) { pawn = pList[i]; break; }
                        if (pawn != null) break;
                    }
                    if (pawn == null)
                    {
                        foreach (var p in Find.WorldPawns.AllPawnsAliveOrDead) { if (p?.thingIDNumber == k.pawnId) { pawn = p; break; } }
                    }
                }
                catch { }

                if (pawn == null || pawn.inventory?.innerContainer == null || !pawn.inventory.innerContainer.Contains(bound))
                {
                    if (remove == null) remove = new List<Key>(4);
                    remove.Add(k);
                }
            }
            if (remove != null) for (int i = 0; i < remove.Count; i++) _map.Remove(remove[i]);
        }

        /// <summary>
        /// One-shot prune used by health report: returns number of pruned bindings.
        /// Lightweight; does not allocate unless removals occur.
        /// </summary>
        public static int PruneDriftedOnce()
        {
            if (_map.Count == 0) return 0;
            List<Key> remove = null;
            foreach (var kv in _map)
            {
                var k = kv.Key; var bi = kv.Value;
                Thing bound = null; bi.boundRef?.TryGetTarget(out bound);
                if (bound == null || bound.Destroyed)
                {
                    if (remove == null) remove = new List<Key>(4);
                    remove.Add(k); continue;
                }
                Pawn pawn = null;
                try
                {
                    foreach (var map in Find.Maps)
                    {
                        var pList = map.mapPawns?.AllPawnsSpawned; if (pList == null) continue;
                        for (int i = 0; i < pList.Count; i++) if (pList[i]?.thingIDNumber == k.pawnId) { pawn = pList[i]; break; }
                        if (pawn != null) break;
                    }
                    if (pawn == null)
                    {
                        foreach (var p in Find.WorldPawns.AllPawnsAliveOrDead) { if (p?.thingIDNumber == k.pawnId) { pawn = p; break; } }
                    }
                }
                catch { }
                if (pawn == null || pawn.inventory?.innerContainer == null || !pawn.inventory.innerContainer.Contains(bound))
                {
                    if (remove == null) remove = new List<Key>(4);
                    remove.Add(k);
                }
            }
            int pruned = 0;
            if (remove != null)
            {
                pruned = remove.Count;
                for (int i = 0; i < remove.Count; i++) _map.Remove(remove[i]);
            }
            return pruned;
        }

        /// <summary>
        /// Lightweight predicate used by NightmareCarryEnforcer to exclude bound single textile units from real tool counting.
        /// Returns true if the provided thing is currently registered as a bound consumable unit.
        /// O(n) over small registry (<< 128 expected). No allocations.
        /// </summary>
        internal static bool IsBoundUnit(Thing thing)
        {
            if (thing == null || _map.Count == 0) return false;
            int id = thing.thingIDNumber;
            foreach (var kv in _map)
            {
                if (kv.Value.boundThingId == id) return true;
            }
            return false;
        }
    }
}
