// RimWorld 1.6 / C# 7.3
// Source/Helpers/ToolGateMoteHelper.cs

// TODO: Remove this file and all references to it if motes are not desired in the mod.
//       (Settings.showDenialMotes = false is sufficient to disable all mote spawning.)
//
// Provides unified, rate-limited spawning of denial / slowdown motes for tool-gated work.
// Edge considerations:
//  - Per pawn+stat cooldown (default 3s) to avoid spam when WorkGiver queries repeat.
//  - Skips if settings.showDenialMotes == false.
//  - Avoids duplicating existing Cleaning/WorkSpeedGlobal penalty motes (caller should skip those or pass skipCleaningAndGlobal=true).
//  - Builds text using translation keys + stat category description for player clarity.
//  - Normal mode slowdown: show slowed factor; hardcore block: show blocked message.
//  - Combined message (Needs + Slowed) available for future hybrid cases.

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools
{
    public static class ToolGateMoteHelper
    {
        private struct Key : IEquatable<Key>
        {
            public int PawnId; public int StatId;
            public Key(int pawnId, int statId) { PawnId = pawnId; StatId = statId; }
            public bool Equals(Key other) => PawnId == other.PawnId && StatId == other.StatId;
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => (PawnId * 397) ^ StatId;
        }

        private static readonly Dictionary<Key, float> _lastShownTick = new Dictionary<Key, float>(128);
        private const int CooldownTicks = 1800; // 30 seconds at 60 TPS

        private static bool CanShow(Pawn pawn, StatDef stat)
        {
            if (pawn?.Map == null) return false;
            var settings = SurvivalTools.Settings;
            if (settings == null || !settings.showDenialMotes) return false;
            if (stat == null) return false;

            var key = new Key(pawn.thingIDNumber, stat.index);
            float curTick = Find.TickManager.TicksGame;
            if (_lastShownTick.TryGetValue(key, out float last) && (curTick - last) < CooldownTicks)
                return false;
            _lastShownTick[key] = curTick;
            return true;
        }

        private static string GetCategoryLabel(StatDef stat)
        {
            try
            {
                return SurvivalToolUtility.GetStatCategoryDescription(stat) ?? stat?.label ?? stat?.defName ?? "?";
            }
            catch { return stat?.label ?? stat?.defName ?? "?"; }
        }

        public static void TryShowBlockedMote(Pawn pawn, StatDef stat)
        {
            if (!CanShow(pawn, stat)) return;
            try
            {
                string cat = GetCategoryLabel(stat);
                string text = "SurvivalTools_Mote_Blocked".Translate(cat);
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, text, 3.5f);
                if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"Mote_Blocked_{pawn.ThingID}_{stat.defName}"))
                    LogDebug($"[SurvivalTools.Mote] Blocked mote shown for {pawn.LabelShort} ({cat})", $"Mote_Blocked_{pawn.ThingID}_{stat.defName}");
            }
            catch { }
        }

        public static void TryShowSlowedMote(Pawn pawn, StatDef stat, float factor)
        {
            if (!CanShow(pawn, stat)) return;
            try
            {
                string text = "SurvivalTools_Mote_Slowed".Translate(factor.ToString("F2"));
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, text, 3.5f);
                if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"Mote_Slowed_{pawn.ThingID}_{stat.defName}"))
                    LogDebug($"[SurvivalTools.Mote] Slowed mote shown for {pawn.LabelShort} ({stat.defName}) x{factor:F2}", $"Mote_Slowed_{pawn.ThingID}_{stat.defName}");
            }
            catch { }
        }

        public static void TryShowNeedsAndSlowedMote(Pawn pawn, StatDef stat, float factor)
        {
            if (!CanShow(pawn, stat)) return;
            try
            {
                string cat = GetCategoryLabel(stat);
                string text = "SurvivalTools_Mote_NeedsAndSlowed".Translate(cat, factor.ToString("F2"));
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, text, 3.5f);
                if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"Mote_NeedsSlow_{pawn.ThingID}_{stat.defName}"))
                    LogDebug($"[SurvivalTools.Mote] Needs+Slowed mote shown for {pawn.LabelShort} ({cat}) x{factor:F2}", $"Mote_NeedsSlow_{pawn.ThingID}_{stat.defName}");
            }
            catch { }
        }
    }
}
