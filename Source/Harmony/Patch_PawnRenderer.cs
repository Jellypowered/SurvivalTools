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
            // Skip portraits / invisible
            if (parms.Portrait || parms.flags.HasFlag(PawnRenderFlags.Invisible)) return;

            var pawn = parms.pawn;
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed) return;

            // Only when actually working (not drafted/combat/idle)
            if (pawn.Drafted) return;
            if (pawn.jobs?.curJob == null || pawn.jobs.curDriver == null) return;
            if (!IsWorklike(pawn.CurJobDef)) return;

            // Only handle North here (we want it behind the body). E/S/W happen in equipment pass.
            if (parms.facing != Rot4.North) return;

            // World position from the renderer matrix
            Vector3 rootLoc = parms.matrix.MultiplyPoint3x4(Vector3.zero);

            // Push slightly below the body so the body draws over it
            float baseAlt = AltitudeLayer.Pawn.AltitudeFor();
            float toolAlt = baseAlt - Altitudes.AltInc * 0.25f;

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
