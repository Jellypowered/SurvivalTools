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

        public static bool TryGetOrBind(Pawn pawn, StatDef stat, out Thing boundUnit)
        {
            boundUnit = null;
            if (pawn == null || stat == null || pawn.inventory?.innerContainer == null) return false;
            var key = new Key { pawnId = pawn.thingIDNumber, statIndex = stat.index };

            if (_map.TryGetValue(key, out var info))
            {
                if (info.boundRef != null && info.boundRef.TryGetTarget(out var existing) && existing != null && !existing.Destroyed)
                {
                    if (pawn.inventory.innerContainer.Contains(existing))
                    {
                        boundUnit = existing; return true;
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
    }
}
