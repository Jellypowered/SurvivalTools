using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(PawnRenderer), "RenderPawnInternal")]
    public static class PawnRenderer_RenderPawnInternal_Prefix
    {
        [HarmonyPrefix]
        public static void Prefix(PawnRenderer __instance, PawnDrawParms parms)
        {
            bool debug = SurvivalTools.Settings != null && SurvivalTools.Settings.debugLogging;

            // Skip portraits / invisible
            if (parms.Portrait || parms.flags.HasFlag(PawnRenderFlags.Invisible))
            {
                if (debug)
                    Log.Message($"[SurvivalTools] Skipping draw for {parms.pawn?.LabelShort ?? "null"} — Portrait or Invisible.");
                return;
            }

            var pawn = parms.pawn;
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed)
            {
                if (debug)
                    Log.Message($"[SurvivalTools] Skipping draw — Pawn is null, not spawned, dead, or downed.");
                return;
            }

            // Only when actually working (not drafted/combat/idle)
            if (pawn.Drafted)
            {
                if (debug)
                    Log.Message($"[SurvivalTools] Skipping draw for {pawn.LabelShort} — Drafted.");
                return;
            }
            if (pawn.jobs?.curJob == null || pawn.jobs.curDriver == null)
            {
                if (debug)
                    Log.Message($"[SurvivalTools] Skipping draw for {pawn.LabelShort} — No current job/driver.");
                return;
            }
            if (!IsWorklike(pawn.CurJobDef))
            {
                if (debug)
                    Log.Message($"[SurvivalTools] Skipping draw for {pawn.LabelShort} — Job '{pawn.CurJobDef.defName}' not worklike.");
                return;
            }

            // Only handle North here (we want it behind the body). E/S/W happen in equipment pass.
            if (parms.facing != Rot4.North)
            {
                if (debug)
                    Log.Message($"[SurvivalTools] Skipping draw for {pawn.LabelShort} — Facing {parms.facing}, handled elsewhere.");
                return;
            }

            // World position from the renderer matrix
            Vector3 rootLoc = parms.matrix.MultiplyPoint3x4(Vector3.zero);

            // Push slightly below the body so the body draws over it
            float baseAlt = AltitudeLayer.Pawn.AltitudeFor();
            float toolAlt = baseAlt - Altitudes.AltInc * 0.25f;

            if (debug)
            {
                Log.Message($"[SurvivalTools] Drawing tool for {pawn.LabelShort} — Facing {parms.facing}, " +
                            $"Job: {pawn.CurJobDef.defName}, RootLoc: {rootLoc}, ToolAlt: {toolAlt}, Flags: {parms.flags}");
            }

            ActiveToolDrawer.DrawStaticTool(pawn, rootLoc, parms.facing, toolAlt);
        }

        private static bool IsWorklike(JobDef def)
        {
            if (def == null) return false;
            var s = def.defName.ToLowerInvariant();
            return s.Contains("mine") || s.Contains("deepdrill") ||
                   s.Contains("construct") || s.Contains("frame") || s.Contains("repair") || s.Contains("smooth") ||
                   s.Contains("buildroof") || s.Contains("removeroof") || s.Contains("install") ||
                   s.Contains("uninstall") || s.Contains("deconstruct") || s.Contains("build") ||
                   s.Contains("plant") || s.Contains("sow") || s.Contains("harvest") || s.Contains("cut");
        }
    }
}

