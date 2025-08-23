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
            bool debug = SurvivalTools.Settings != null && SurvivalTools.Settings.debugLogging;

            try
            {
                if (pawn == null)
                {
                    //if (debug) DLog("Skip: pawn == null");
                    return;
                }

                if (flags.HasFlag(PawnRenderFlags.Portrait) || flags.HasFlag(PawnRenderFlags.Invisible))
                {
                    //if (debug) DLog(pawn, $"Skip: flags={FlagsToString(flags)} (Portrait/Invisible)");
                    return;
                }

                if (!pawn.Spawned || pawn.Dead || pawn.Downed)
                {
                    //if (debug) DLog(pawn, $"Skip: Spawned={pawn.Spawned}, Dead={pawn.Dead}, Downed={pawn.Downed}");
                    return;
                }

                if (pawn.Drafted)
                {
                    //if (debug) DLog(pawn, "Skip: pawn is drafted");
                    return;
                }

                var curJob = pawn.jobs?.curJob;
                var driver = pawn.jobs?.curDriver;
                if (curJob == null || driver == null)
                {
                    //if (debug) DLog(pawn, $"Skip: no curJob/driver (curJob={(curJob?.def?.defName ?? "null")}, driver={(driver?.GetType().Name ?? "null")})");
                    return;
                }

                if (!IsWorklike(curJob.def))
                {
                    //if (debug) DLog(pawn, $"Skip: job '{curJob.def.defName}' not worklike");
                    return;
                }

                if (facing == Rot4.North)
                {
                    // if (debug) DLog(pawn, $"Skip: facing North handled in body pass (job={curJob.def.defName})");
                    return;
                }

                // Equipment pass already supplies an equipment-layer Y; keep it.
                float toolAlt = drawPos.y;

                if (debug)
                {
                    //  DLog(pawn,
                    //    $"Draw tool | job={curJob.def.defName} facing={facing} flags={FlagsToString(flags)} " +
                    //  $"drawPos=({drawPos.x:F2},{drawPos.y:F2},{drawPos.z:F2}) toolAlt={toolAlt:F2}");
                }

                ActiveToolDrawer.DrawStaticTool(pawn, drawPos, facing, toolAlt);
            }
            catch (System.Exception e)
            {
                // Collapse repeats per pawn+method to avoid spam
                int key = Gen.HashCombineInt("ST_DrawExtras".GetHashCode(), pawn?.thingIDNumber ?? 0);
                Log.WarningOnce($"[SurvivalTools] DrawEquipmentAndApparelExtras postfix errored for {pawn?.LabelShort ?? "null"}: {e}", key);
            }
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

        // ---------- tiny debug helpers (gated by setting) ----------

        private static void DLog(Pawn pawn, string msg)
        {
            if (SurvivalTools.Settings != null && SurvivalTools.Settings.debugLogging) ;
            //Log.Message($"[SurvivalTools.DrawExtras] {pawn?.LabelShort ?? "null"}: {msg}");
        }

        private static void DLog(string msg)
        {
            if (SurvivalTools.Settings != null && SurvivalTools.Settings.debugLogging) ;
            //Log.Message($"[SurvivalTools.DrawExtras] {msg}");
        }

        private static string FlagsToString(PawnRenderFlags f)
        {
            // Let RimWorld’s enum provide the right names for this version
            return f.ToString();
        }
    }
}

