// RimWorld 1.6 / C# 7.3
// Source/Helpers/ST_WearService.cs
// Phase 8: Central pulsed wear service for real + virtual survival tools.
// - No per-thing comps; single static service invoked from StatPart hot path.
// - Zero allocations in hot path (struct keys, pooled dictionaries, no LINQ).
// - Tracks last pulse tick and fractional remainder per (pawn, tool, stat) trio.
// - Applies durability loss every >= 60 ticks while work stat actively transformed.
// - Virtual tools: drains HP from underlying textile stack (cloth/wool/etc.).

using System.Collections.Generic;
using RimWorld;
using Verse;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Helpers
{
    internal static class ST_WearService
    {
        private struct WearKey
        {
            public int pawnId; // pawn.thingIDNumber
            public int toolId; // underlying thing (virtual -> source stack)
            public int statIndex; // stat.index (stable)
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = pawnId;
                    h = (h * 397) ^ toolId;
                    h = (h * 397) ^ statIndex;
                    return h;
                }
            }
            public override bool Equals(object obj)
            {
                if (!(obj is WearKey wk)) return false;
                return pawnId == wk.pawnId && toolId == wk.toolId && statIndex == wk.statIndex;
            }
        }

        private struct WearState
        {
            public int lastTick;
            public float remainder; // carry fractional HP until >=1 consumed across pulses
        }

        // Supplemental throttle map for legacy adapter calls that don't identify a specific tool (e.g., mining ResetTicks hooks).
        // Keyed by (pawnId, statIndex) to guarantee at most one accepted pulse per interval regardless of caller path.
        private struct StatThrottleKey
        {
            public int pawnId;
            public int statIndex;
            public override int GetHashCode()
            {
                unchecked { return (pawnId * 397) ^ statIndex; }
            }
            public override bool Equals(object obj)
            {
                if (!(obj is StatThrottleKey o)) return false; return o.pawnId == pawnId && o.statIndex == statIndex;
            }
        }
        private static readonly Dictionary<StatThrottleKey, int> _statThrottle = new Dictionary<StatThrottleKey, int>(256);

        // Internal state dictionary. Expect small cardinality (active workers only)
        private static readonly Dictionary<WearKey, WearState> _states = new Dictionary<WearKey, WearState>(256);

        private const int PulseInterval = 60; // once per in‑game second
        private const float BaseHpPerPulse = 0.1f; // before multipliers
        private static int _lastGlobalPulseTick = 0; // last successful HP application (any tool)

        // Per-stat additional multiplier (only cleaning different right now)
        private static float StatMultiplier(StatDef stat)
        {
            if (stat == null) return 1f;
            if (stat == ST_StatDefOf.CleaningSpeed) return 1.5f;
            return 1f;
        }

        /// <summary>
        /// Attempt a wear pulse for this pawn/tool/stat trio. Only call when a positive tool factor was applied.
        /// </summary>
        internal static void TryPulseWear(Pawn pawn, SurvivalTool toolWrapper, StatDef stat)
        {
            if (pawn == null || toolWrapper == null || stat == null) return;
            // Skip if degradation disabled
            var settings = SurvivalToolsMod.Settings;
            if (settings == null || settings.toolDegradationFactor <= 0f) return;
            Thing actualThing = null;
            if (toolWrapper is VirtualTool)
            {
                // Bind or obtain per-(pawn,stat) single unit so we don't delete whole stacks
                if (!ST_BoundConsumables.TryGetOrBind(pawn, stat, out actualThing))
                    actualThing = GetUnderlyingThing(toolWrapper); // fallback
                else
                {
                    // Guard: if the bound unit is no longer in this pawn's inventory (merged, hauled, dropped), unbind and retry next pulse.
                    var owner = actualThing.ParentHolder as ThingOwner;
                    if (owner != pawn.inventory?.innerContainer)
                    {
                        ST_BoundConsumables.UnbindByThingId(actualThing.thingIDNumber);
                        return; // rebind next pulse
                    }
                }
            }
            else
            {
                actualThing = GetUnderlyingThing(toolWrapper);
            }
            if (actualThing == null || actualThing.DestroyedOrNull()) return;
            if (!actualThing.def.useHitPoints || actualThing.MaxHitPoints <= 0) return;

            // Wood / apparel / weapons should never reach here for virtual tools due to eligibility filters

            int now = Find.TickManager?.TicksGame ?? 0;

            var key = new WearKey
            {
                pawnId = pawn.thingIDNumber,
                toolId = actualThing.thingIDNumber,
                statIndex = stat.index
            };

            WearState state;
            if (_states.TryGetValue(key, out state))
            {
                if (now - state.lastTick < PulseInterval)
                    return; // too soon
            }
            else
            {
                state.lastTick = now - PulseInterval; // allow immediate pulse first time
                state.remainder = 0f;
            }

            state.lastTick = now;

            // Compute delta HP with remainder carry, ensuring at least 1 per 10 pulses (by fractional accumulation)
            float mult = settings.toolDegradationFactor * StatMultiplier(stat);
            float raw = BaseHpPerPulse * mult; // e.g. 0.1 * factors
            raw += state.remainder;
            int whole = (int)raw; // truncate
            state.remainder = raw - whole;
            if (whole <= 0)
            {
                // Force progress by accumulating remainder; only apply when reaches >=1; guarantee 1 every ~10 pulses via BaseHpPerPulse 0.1
                _states[key] = state;
                return;
            }

            ApplyHpLoss(pawn, actualThing, whole, stat);

            // Phase 12: Discharge powered tools during work
            // DISABLED: Battery system turned off
            /*
            var powerComp = actualThing.TryGetComp<CompPowerTool>();
            if (powerComp != null)
            {
                powerComp.NotifyWorkTick(stat);
            }
            */

            // Record global pulse tick (successful application only)
            _lastGlobalPulseTick = now;

            _states[key] = state;
        }

        /// <summary>
        /// Adapter-friendly throttled pulse: ensures only one pulse per (pawn, stat) per interval even if multiple tool lookups fire.
        /// Used by legacy TryDegradeTool hook to unify with StatPart pulses.
        /// </summary>
        internal static void TryPulseWearThrottled(Pawn pawn, SurvivalTool toolWrapper, StatDef stat)
        {
            if (pawn == null || toolWrapper == null || stat == null) return;
            var settings = SurvivalToolsMod.Settings;
            if (settings == null || settings.toolDegradationFactor <= 0f) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            var sKey = new StatThrottleKey { pawnId = pawn.thingIDNumber, statIndex = stat.index };
            if (_statThrottle.TryGetValue(sKey, out int last) && now - last < PulseInterval)
                return; // already pulsed via some path
            _statThrottle[sKey] = now;
            TryPulseWear(pawn, toolWrapper, stat); // delegate (will still do per-tool gating)
        }

        private static Thing GetUnderlyingThing(SurvivalTool toolWrapper)
        {
            if (toolWrapper is VirtualTool vt)
                return vt.SourceThing; // degrade stack item
            return toolWrapper; // real tool instance
        }

        private static void ApplyHpLoss(Pawn pawn, Thing thing, int amount, StatDef stat)
        {
            if (thing == null || thing.DestroyedOrNull()) return;
            int before = thing.HitPoints;
            int after = before - amount;
            if (after > 0)
            {
                thing.HitPoints = after;
                if (IsDebugLoggingEnabled && stat == ST_StatDefOf.DiggingSpeed && ShouldLogWithCooldown($"WearPulse_Dig_{pawn.thingIDNumber}_{thing.thingIDNumber}"))
                {
                    LogDebug($"[SurvivalTools.Wear] DiggingSpeed pulse {pawn.LabelShort} -{amount} HP ({after}/{thing.MaxHitPoints}) on {thing.LabelNoCount}", $"WearPulse_Dig_{pawn.thingIDNumber}_{thing.thingIDNumber}");
                }
                return;
            }

            // Destroy bound consumable (or tool) directly; bound registry will be cleared.
            try { thing.Destroy(DestroyMode.Vanish); } catch { }
            ST_BoundConsumables.UnbindByThingId(thing.thingIDNumber);

            // Invalidate scoring so next selection re-evaluates
            ScoreCache.NotifyToolChanged(thing);

            if (IsDebugLoggingEnabled)
            {
                LogDebug($"[SurvivalTools.Wear] {pawn.LabelShort} consumed {thing.LabelCapNoCount}", $"WearPulse_{pawn.thingIDNumber}_{thing.thingIDNumber}");
            }
        }

        /// <summary>Clear all tracked wear state (e.g., on game load reset if desired)</summary>
        internal static void Clear()
        {
            _states.Clear();
            _statThrottle.Clear();
        }

        /// <summary>Last tick any wear HP was actually applied (0 if never).</summary>
        public static int GetGlobalLastPulseTickUnsafe() => _lastGlobalPulseTick;
    }
}
