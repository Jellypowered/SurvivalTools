using System.Linq;
using HarmonyLib;
using RimWorld;
using SurvivalTools;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SurvivalTools.HarmonyStuff
{
    // Draw in the same pass vanilla uses for guns/apparel extras.
    // We run VERY LOW priority so other mods (e.g. Yayo, Dark Ages) go first.
    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAndApparelExtras))]
    [HarmonyPriority(Priority.VeryLow)]
    public static class ShowWhileWorking_Draw_Postfix
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, Vector3 drawPos, Rot4 facing, PawnRenderFlags flags)
        {
            // Skip non-play renders
            if (flags.HasFlag(PawnRenderFlags.Portrait) || flags.HasFlag(PawnRenderFlags.Invisible)) return;
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed) return;

            // Only show while actually working
            if (pawn.Drafted) return;
            Job job = pawn.CurJob;
            if (job == null || pawn.jobs?.curDriver == null) return;

            // Ask logic which tool is in use - use the actual SurvivalTools system
            var tool = GetActiveToolForJob(pawn, job) as ThingWithComps;
            if (tool == null) return;

            // If another mod already causes this SAME item to be drawn as Primary, don't double-draw
            if (pawn.equipment?.Primary != null && pawn.equipment.Primary == tool) return;

            // Use vanilla equipment-distance factor (children hold things a bit closer)
            float distFactor = pawn.ageTracker?.CurLifeStage?.equipmentDrawDistanceFactor ?? 1f;

            // If pawn is "aiming" per vanilla rules, use the aiming helper; else use the carried helper
            var stanceBusy = pawn.stances?.curStance as Stance_Busy;
            bool canAim = !flags.HasFlag(PawnRenderFlags.NeverAimWeapon)
                          && stanceBusy != null && !stanceBusy.neverAimWeapon && stanceBusy.focusTarg.IsValid;

            if (canAim)
            {
                // Compute aim angle (mirror of vanilla logic shape)
                Vector3 target = stanceBusy.focusTarg.HasThing
                    ? stanceBusy.focusTarg.Thing.DrawPos
                    : stanceBusy.focusTarg.Cell.ToVector3Shifted();

                float aimAngle = 0f;
                if ((target - pawn.DrawPos).sqrMagnitude > 0.001f)
                    aimAngle = (target - pawn.DrawPos).AngleFlat();

                var verb = pawn.CurrentEffectiveVerb;
                if (verb != null && verb.AimAngleOverride.HasValue)
                    aimAngle = verb.AimAngleOverride.Value;

                // Vanilla pushes the held item forward along aim:
                drawPos += new Vector3(0f, 0f, 0.4f + tool.def.equippedDistanceOffset)
                           .RotatedBy(aimAngle) * distFactor;

                PawnRenderUtility.DrawEquipmentAiming(tool, drawPos, aimAngle);
            }
            else
            {
                // Static carry look, handled by vanilla helper (uses built-in EqLoc offsets & flip)
                PawnRenderUtility.DrawCarriedWeapon(tool, drawPos, facing, distFactor);
            }
        }

        /// <summary>
        /// Gets the best survival tool for the current job using the actual SurvivalTools logic.
        /// </summary>
        private static Thing GetActiveToolForJob(Pawn pawn, Job job)
        {
            if (!pawn.CanUseSurvivalTools()) return null;

            var requiredStats = SurvivalToolUtility.RelevantStatsFor(job.workGiverDef, job);
            if (requiredStats.NullOrEmpty()) return null;

            // Debug logging for tool drawing
            if (SurvivalToolUtility.IsDebugLoggingEnabled)
            {
                string logKey = $"Drawing_Tool_{pawn.ThingID}_{job.def.defName}";
                if (SurvivalToolUtility.ShouldLogWithCooldown(logKey))
                {
                    var bestTool = pawn.GetBestSurvivalTool(requiredStats);
                    Log.Message($"[SurvivalTools.Drawing] {pawn.LabelShort} doing {job.def.defName} (WG: {job.workGiverDef?.defName ?? "null"}) needs {string.Join(", ", requiredStats.Select(s => s.defName))} -> drawing {bestTool?.LabelShort ?? "no tool"}");
                }
            }

            // Find the best tool for the required stats
            return pawn.GetBestSurvivalTool(requiredStats);
        }
    }
}

