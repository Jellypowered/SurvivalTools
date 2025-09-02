// RimWorld 1.6 / C# 7.3
// Patch_PawnRenderer.cs
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SurvivalTools.HarmonyStuff
{
    // Draw in the same pass vanilla uses for guns/apparel extras.
    // Run last so our visual overlay remains visible over other mods' draws.
    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAndApparelExtras))]
    [HarmonyPriority(Priority.Last)]
    public static class ShowWhileWorking_Draw_Postfix
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, Vector3 drawPos, Rot4 facing, PawnRenderFlags flags)
        {
            // Skip non-play renders / invisible / invalid pawns
            if ((flags & (PawnRenderFlags.Portrait | PawnRenderFlags.Invisible)) != 0) return;
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed) return;

            // Only show while actually working (not drafted, has job + driver)
            if (pawn.Drafted) return;
            var job = pawn.CurJob;
            if (job == null || pawn.jobs?.curDriver == null) return;

            // Ask our logic which tool is in use
            var (toolThing, requiredStats) = GetActiveToolForJob(pawn, job);
            if (toolThing == null || toolThing.Destroyed) return;

            // If another mod already causes this SAME physical item to be drawn as Primary, don't double-draw.
            // For virtual wrappers compare by def so a physical cloth held as primary will also block us.
            var primary = pawn.equipment?.Primary;
            if (primary != null)
            {
                if (ReferenceEquals(primary, toolThing)) return;
                if (toolThing is VirtualSurvivalTool vt && primary.def == vt.SourceDef) return;
            }

            // Use vanilla equipment-distance factor (children hold things a bit closer)
            float distFactor = pawn.ageTracker?.CurLifeStage?.equipmentDrawDistanceFactor ?? 1f;

            // If pawn is "aiming" per vanilla rules, use the aiming helper; else use the carried helper
            var stanceBusy = pawn.stances?.curStance as Stance_Busy;
            bool canAim = stanceBusy != null
                          && !stanceBusy.neverAimWeapon
                          && !flags.HasFlag(PawnRenderFlags.NeverAimWeapon)
                          && stanceBusy.focusTarg.IsValid;

            // Precompute tint for virtual tools (stat-based)
            Color virtualTint = GetTintForVirtualTool(toolThing, requiredStats);

            if (canAim)
            {
                // Compute aim angle (mirror vanilla shape)
                Vector3 target = stanceBusy.focusTarg.HasThing
                    ? (stanceBusy.focusTarg.Thing?.DrawPos ?? pawn.DrawPos)
                    : stanceBusy.focusTarg.Cell.ToVector3Shifted();

                float aimAngle = 0f;
                Vector3 delta = target - pawn.DrawPos;
                if (delta.sqrMagnitude > 0.001f)
                    aimAngle = delta.AngleFlat();

                var verb = pawn.CurrentEffectiveVerb;
                if (verb != null && verb.AimAngleOverride.HasValue)
                    aimAngle = verb.AimAngleOverride.Value;

                // Adjust base position
                float eqOffset = toolThing.def?.equippedDistanceOffset ?? 0f;
                drawPos += new Vector3(0f, 0f, 0.4f + eqOffset).RotatedBy(aimAngle) * distFactor;

                if (toolThing is VirtualSurvivalTool)
                {
                    var mat = toolThing.Graphic?.MatSingle;
                    if (mat == null) return;

                    // Guard: mainTexture might be null or not Texture2D
                    var tex = mat.mainTexture as Texture2D;
                    var tinted = tex != null
                        ? MaterialPool.MatFrom(tex, ShaderDatabase.Transparent, virtualTint)
                        : mat; // fallback to original if no texture

                    const float scale = 0.75f;
                    Graphics.DrawMesh(
                        MeshPool.plane10,
                        Matrix4x4.TRS(drawPos, Quaternion.AngleAxis(aimAngle, Vector3.up), new Vector3(scale, 1f, scale)),
                        tinted,
                        0
                    );
                }
                else if (toolThing is ThingWithComps twc)
                {
                    // real SurvivalTool / ThingWithComps - use vanilla aiming helper
                    PawnRenderUtility.DrawEquipmentAiming(twc, drawPos, aimAngle);
                }
            }
            else
            {
                if (toolThing is VirtualSurvivalTool)
                {
                    var mat = toolThing.Graphic?.MatSingle;
                    if (mat == null) return;

                    var tex = mat.mainTexture as Texture2D;
                    var tinted = tex != null
                        ? MaterialPool.MatFrom(tex, ShaderDatabase.Transparent, virtualTint)
                        : mat;

                    const float scale = 0.75f;
                    Graphics.DrawMesh(
                        MeshPool.plane10,
                        Matrix4x4.TRS(drawPos, Quaternion.identity, new Vector3(scale, 1f, scale)),
                        tinted,
                        0
                    );
                }
                else if (toolThing is ThingWithComps twc)
                {
                    // real tool - let vanilla draw it as carried weapon
                    PawnRenderUtility.DrawCarriedWeapon(twc, drawPos, facing, distFactor);
                }
            }
        }

        private static Color GetTintForVirtualTool(Thing tool, List<StatDef> requiredStats)
        {
            // Default: light gray, semi-transparent
            var defaultTint = new Color(1f, 1f, 1f, 0.6f);
            if (tool == null || requiredStats == null || requiredStats.Count == 0) return defaultTint;

            // quick membership checks (no LINQ allocations in hot path)
            for (int i = 0; i < requiredStats.Count; i++)
            {
                var s = requiredStats[i];
                if (s == ST_StatDefOf.MedicalOperationSpeed || s == ST_StatDefOf.MedicalSurgerySuccessChance)
                    return new Color(0.4f, 1f, 0.4f, 0.75f);   // green
                if (s == ST_StatDefOf.CleaningSpeed)
                    return new Color(0.4f, 0.7f, 1f, 0.75f);   // blue
                if (s == ST_StatDefOf.ResearchSpeed)
                    return new Color(0.7f, 0.5f, 1f, 0.75f);   // purple
                if (s == ST_StatDefOf.ButcheryFleshSpeed || s == ST_StatDefOf.ButcheryFleshEfficiency)
                    return new Color(1f, 0.4f, 0.4f, 0.75f);   // red
            }
            return defaultTint;
        }

        /// <summary>
        /// Gets the best survival tool for the current job using the actual SurvivalTools logic.
        /// Returns both the tool and the required stats.
        /// </summary>
        private static (Thing tool, List<StatDef> requiredStats) GetActiveToolForJob(Pawn pawn, Job job)
        {
            if (pawn == null || job == null || !pawn.CanUseSurvivalTools())
                return (null, null);

            var requiredStats = SurvivalToolUtility.RelevantStatsFor(job.workGiverDef, job);
            if (requiredStats.NullOrEmpty())
                return (null, requiredStats);

            // Debug (cooldowned) – extremely rare; safe to keep
            if (SurvivalToolUtility.IsDebugLoggingEnabled)
            {
                var key = $"Drawing_Tool_{pawn.ThingID}_{job.def?.defName ?? "null"}";
                if (SurvivalToolUtility.ShouldLogWithCooldown(key))
                {
                    var bestForLog = pawn.GetBestSurvivalTool(requiredStats);
                    Log.Message($"[SurvivalTools.Drawing] {pawn.LabelShort} doing {job.def?.defName ?? "null"} " +
                                $"(WG: {job.workGiverDef?.defName ?? "null"}) needs {string.Join(", ", requiredStats.Select(s => s.defName))} " +
                                $"-> drawing {bestForLog?.LabelShort ?? "no tool"}");
                }
            }

            // First try: normal system (real or virtual)
            var best = pawn.GetBestSurvivalTool(requiredStats);
            if (best != null) return (best, requiredStats);

            // Second try: virtual wrapper for held tool-stuffs
            var inner = pawn.inventory?.innerContainer;
            if (inner != null)
            {
                // scan once; no LINQ alloc in hot path
                for (int i = 0; i < inner.Count; i++)
                {
                    var thing = inner[i];
                    if (thing?.def == null || !thing.def.IsToolStuff()) continue;

                    var ext = thing.def.GetModExtension<SurvivalToolProperties>();
                    if (ext?.baseWorkStatFactors == null) continue;

                    // Does this stuff help any of the required stats?
                    for (int m = 0; m < ext.baseWorkStatFactors.Count; m++)
                    {
                        var mod = ext.baseWorkStatFactors[m];
                        if (mod?.stat != null && requiredStats.Contains(mod.stat))
                            return (VirtualSurvivalTool.FromThing(thing), requiredStats);
                    }
                }
            }

            return (null, requiredStats);
        }
    }
}
