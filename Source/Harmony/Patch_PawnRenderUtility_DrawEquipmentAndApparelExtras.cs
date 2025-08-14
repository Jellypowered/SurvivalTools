using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAndApparelExtras))]
    public static class PawnRenderUtility_DrawEquipmentAndApparelExtras_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, Vector3 drawPos, Rot4 facing, PawnRenderFlags flags)
        {
            // Skip portraits / invisible
            if (flags.HasFlag(PawnRenderFlags.Portrait) || flags.HasFlag(PawnRenderFlags.Invisible)) return;

            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed) return;

            // Only when actually working (not drafted/combat/idle)
            if (pawn.Drafted) return;
            if (pawn.jobs?.curJob == null || pawn.jobs.curDriver == null) return;
            if (!IsWorklike(pawn.CurJobDef)) return;

            // North is rendered in the body pass (prefix below) so skip here.
            if (facing == Rot4.North) return;

            // Equipment pass already supplies an equipment-layer Y; keep it.
            float toolAlt = drawPos.y;

            ActiveToolDrawer.DrawStaticTool(pawn, drawPos, facing, toolAlt);
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
