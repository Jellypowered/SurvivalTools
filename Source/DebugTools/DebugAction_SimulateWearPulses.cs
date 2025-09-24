// RimWorld 1.6 / C# 7.3
// Source/DebugTools/DebugAction_SimulateWearPulses.cs
// Phase 8: Dev tool to simulate 30 seconds of wear pulses for selected pawn.
// Appears only in DevMode. Writes summary to log + optional desktop file (if permission).

using System.Text;
using RimWorld;
using Verse;
using SurvivalTools.Helpers;
using LudeonTK;

namespace SurvivalTools.DebugTools
{
    [StaticConstructorOnStartup]
    internal static class DebugAction_SimulateWearPulses
    {
        // Exposed debug action (requires Verse.DebugAction attribute). If attribute missing (older API), method still callable via reflection.
#if !NO_DEBUG
        [DebugAction(category: "SurvivalTools", name: "Simulate wear pulses (selected pawn, 30s)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
#endif
        public static void SimulateWear()
        {
            if (!Prefs.DevMode) return;
            var sel = Find.Selector.SingleSelectedThing as Pawn;
            if (sel == null)
            {
                Log.Message("[SurvivalTools] Select a pawn first.");
                return;
            }

            // Target duration: 30 real pulses (30s in-game) => 30 * 60 ticks window
            int pulses = 30;
            int ticksPerPulse = 60;
            int startTick = Find.TickManager.TicksGame;

            var sb = new StringBuilder();
            sb.AppendLine($"[SurvivalTools] Simulated wear pulses for {sel.LabelShort} ({pulses} pulses)");

            // Collect active stats via work settings / known survival tool stats (simplified: just loop core stats)
            var stats = new StatDef[] { ST_StatDefOf.CleaningSpeed, ST_StatDefOf.DiggingSpeed, ST_StatDefOf.TreeFellingSpeed, StatDefOf.ConstructionSpeed };

            for (int i = 0; i < pulses; i++)
            {
                foreach (var stat in stats)
                {
                    if (stat == null) continue;
                    var tool = Scoring.ToolScoring.GetBestTool(sel, stat, out float score);
                    if (tool is SurvivalTool st && score > 0.01f)
                    {
                        int pre = GetHP(st);
                        ST_WearService.TryPulseWear(sel, st, stat);
                        int post = GetHP(st);
                        if (pre != post)
                        {
                            sb.AppendLine($" tick={Find.TickManager.TicksGame} stat={stat.defName} tool={tool.LabelCapNoCount} {pre}->{post}");
                        }
                    }
                }
                // Advance ticks manually (fast-forward). We don't want to actually wait; just fudge lastTick logic.
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + ticksPerPulse);
            }

            Log.Message(sb.ToString());
        }

        private static int GetHP(SurvivalTool t)
        {
            Thing underlying = t is VirtualTool vt ? vt.SourceThing : t;
            return underlying?.HitPoints ?? 0;
        }
    }
}
