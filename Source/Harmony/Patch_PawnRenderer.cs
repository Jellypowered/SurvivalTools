// RimWorld 1.6 / C# 7.3
// Source/Harmony/Patch_PawnRenderer.cs
//
// Purpose:
//   Ensures pawns visibly "use" survival tools while working by drawing the tool
//   in the same pass that vanilla uses for equipment/apparel extras.
//   This improves immersion, showing tools in-hand only when relevant.
//
// What this patch does:
//   - Draws an in-use SurvivalTool (real or virtual) while a pawn is performing a work job.
//   - Skips portrait / invisible renders and invalid pawn states.
//   - Skips drafted pawns unless they're actually performing a work job (i.e., relevant stats exist).
//   - Avoids double-drawing if the same physical item is already shown as Primary.
//   - Uses a small JobDef â†’ required-stats cache to reduce allocations.
//
// Future ideas (comments only, not implemented):
//   - Animated tools (e.g., swinging pick/axe, wrench turning, microscope oscillation).
//   - Per-job offsets/orientations so a hammer sits differently than a wrench.
//   - Sync lightweight animations to pawn tick or job progress for smooth motion.

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAndApparelExtras))]
    [HarmonyPriority(Priority.Last)] // draw last so our overlay isn't hidden by other mods
    [StaticConstructorOnStartup]     // ensure our type gets initialized at load
    public static class ShowWhileWorking_Draw_Postfix
    {
        // Cache: JobDef -> required stats to avoid repeat lookups/allocations.
        private static readonly Dictionary<JobDef, List<StatDef>> _jobStatCache =
            new Dictionary<JobDef, List<StatDef>>();

        // Optional: static ctor (no reload hook in 1.6 â€” we just start empty)
        static ShowWhileWorking_Draw_Postfix()
        {
            _jobStatCache.Clear();
            // If you later add a reload hook, clear the cache there as well.
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, Vector3 drawPos, Rot4 facing, PawnRenderFlags flags)
        {
            // Skip non-play renders / invisible / invalid pawns
            if ((flags & (PawnRenderFlags.Portrait | PawnRenderFlags.Invisible)) != 0) return;
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed) return;

            var job = pawn.CurJob;
            if (job == null || pawn.jobs?.curDriver == null) return;

            // Drafted behavior:
            //  - We generally skip drafted pawns (vanilla shows weapons),
            //  - but if they're actually doing a "work job" (i.e., stats are relevant),
            //    we'll draw the tool. We enforce this indirectly because we only draw
            //    when RelevantStatsFor() returns a non-empty set.
            //    (So no special hard block here â€” the stat check below is the gate.)

            // Decide which tool is in use (real or virtual) and what stats are required.
            var (toolThing, requiredStats) = GetActiveToolForJob(pawn, job);
            if (toolThing == null || toolThing.Destroyed) return;

            // Prevent double-draw if the same physical thing is already being rendered as Primary.
            var primary = pawn.equipment?.Primary;
            if (primary != null)
            {
                // Compare by backing thing for virtual wrappers so we don't draw a virtual wrapper
                // when the actual spawned thing is already drawn as primary.
                Thing backing = SurvivalToolUtility.BackingThing(toolThing as SurvivalTool, pawn);
                if (backing != null)
                {
                    if (ReferenceEquals(primary, backing)) return;
                }
                else
                {
                    if (ReferenceEquals(primary, toolThing)) return;
                    if (toolThing is VirtualTool vt && primary.def == vt.SourceDef) return;
                }
            }

            // Vanilla equipment distance factor (children hold closer).
            float distFactor = pawn.ageTracker?.CurLifeStage?.equipmentDrawDistanceFactor ?? 1f;

            // If pawn is "aiming" per vanilla rules, draw with aiming helper; otherwise draw as carried.
            var stanceBusy = pawn.stances?.curStance as Stance_Busy;
            bool canAim = stanceBusy != null
                          && !stanceBusy.neverAimWeapon
                          && !flags.HasFlag(PawnRenderFlags.NeverAimWeapon)
                          && stanceBusy.focusTarg.IsValid;

            // Tint virtual tools by work-type (green medical, red butchery, etc.)
            Color virtualTint = GetTintForVirtualTool(toolThing, requiredStats);

            if (canAim)
            {
                // --- Aiming-style draw (reuses vanilla orientation logic) ---
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

                float eqOffset = toolThing.def?.equippedDistanceOffset ?? 0f;
                drawPos += new Vector3(0f, 0f, 0.4f + eqOffset).RotatedBy(aimAngle) * distFactor;

                if (toolThing is VirtualTool)
                {
                    DrawVirtualTool(toolThing, drawPos, Quaternion.AngleAxis(aimAngle, Vector3.up), virtualTint);

                    // Animation hook (future):
                    //  - For â€œswingingâ€ tools, vary a small extra rotation (e.g., +/- 10Â°) with a sin wave
                    //    tied to pawn tick: float wobble = Mathf.Sin(Time.time * 6f) * 10f;
                    //  - Apply to 'aimAngle' before building the Quaternion.
                }
                else if (toolThing is ThingWithComps twc)
                {
                    PawnRenderUtility.DrawEquipmentAiming(twc, drawPos, aimAngle);
                }
            }
            else
            {
                // --- Carried-style draw (when not aiming) ---
                if (toolThing is VirtualTool)
                {
                    DrawVirtualTool(toolThing, drawPos, Quaternion.identity, virtualTint);

                    // Animation hook (future):
                    //  - Subtle bobbing while working: offset drawPos.y or Z by a small sin wave.
                    //  - Slight rotation oscillation to suggest use.
                }
                else if (toolThing is ThingWithComps twc)
                {
                    PawnRenderUtility.DrawCarriedWeapon(twc, drawPos, facing, distFactor);

                    // Animation hook (future):
                    //  - For hammers/saws, consider small periodic rotation around local Z.
                    //  - Could keyframe based on job progress if available.
                }
            }
        }

        // Draw a virtual tool with a tint (stat-driven)
        private static void DrawVirtualTool(Thing toolThing, Vector3 drawPos, Quaternion rot, Color tint)
        {
            var mat = toolThing.Graphic?.MatSingle;
            if (mat == null) return;

            var tex = mat.mainTexture as Texture2D;
            var tinted = tex != null
                ? MaterialPool.MatFrom(tex, ShaderDatabase.Transparent, tint)
                : mat;

            const float scale = 0.75f;
            Graphics.DrawMesh(
                MeshPool.plane10,
                Matrix4x4.TRS(drawPos, rot, new Vector3(scale, 1f, scale)),
                tinted,
                0
            );
        }

        // Pick a tint based on which stats the tool contributes to.
        private static Color GetTintForVirtualTool(Thing tool, List<StatDef> requiredStats)
        {
            var defaultTint = new Color(1f, 1f, 1f, 0.6f);
            if (tool == null || requiredStats == null || requiredStats.Count == 0) return defaultTint;

            // light-weight membership checks (no LINQ allocs in the hot path)
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
        /// Gets the active survival tool (real or virtual) for the pawn's current job,
        /// and the required stats for that job. Uses a small cache keyed by JobDef.
        /// Drafted pawns still pass through here; if RelevantStatsFor() returns empty,
        /// we naturally refrain from drawing anything (so drafted idle = no tool drawn).
        /// </summary>
        private static (Thing tool, List<StatDef> requiredStats) GetActiveToolForJob(Pawn pawn, Job job)
        {
            if (pawn == null || job == null || !PawnToolValidator.CanUseSurvivalTools(pawn))
                return (null, null);

            // Cache lookup
            if (!_jobStatCache.TryGetValue(job.def, out var requiredStats))
            {
                requiredStats = SurvivalToolUtility.RelevantStatsFor(job.workGiverDef, job) ?? new List<StatDef>();
                _jobStatCache[job.def] = requiredStats;
            }

            // If no relevant stats, nothing to draw (also filters out most drafted non-work jobs).
            if (requiredStats == null || requiredStats.Count == 0)
                return (null, requiredStats);

            // Debug logging (throttled)
            if (IsDebugLoggingEnabled)
            {
                // Summarized per-pawn/job drawing info to avoid per-tool spam in the renderer
                var bestForLog = pawn.GetBestSurvivalTool(requiredStats);
                LogDebugSummary(pawn, job.def, bestForLog);
            }

            // 1) Normal path: best survival tool (real or virtual) selected by our core logic.
            var best = pawn.GetBestSurvivalTool(requiredStats);
            if (best != null) return (best, requiredStats);

            // Held-only best tool edge case: if unified logic returns null but we have exactly one
            // held/equipped candidate that improves any required stat, use it rather than falling
            // all the way through to ad-hoc scoring / virtual wrapping. This can happen briefly
            // during cache warm-up or when expectedKind filtering raced against delayed stat init.
            try
            {
                var held = pawn.GetAllUsableSurvivalTools();
                SurvivalTool singleImprover = null;
                int improverCount = 0;
                foreach (var h in held)
                {
                    SurvivalTool st = h as SurvivalTool;
                    if (st == null && h?.def != null && h.def.IsToolStuff()) st = VirtualTool.FromThing(h);
                    if (st == null) continue;
                    if (SurvivalToolUtility.ToolImprovesAny(st, requiredStats))
                    {
                        improverCount++;
                        if (singleImprover == null) singleImprover = st;
                        if (improverCount > 1) break; // no longer a single improver case
                    }
                }
                if (improverCount == 1 && singleImprover != null)
                {
                    if (IsDebugLoggingEnabled && ShouldLogWithCooldown($"Drawing_SingleHeldFallback_{pawn.ThingID}_{job.def?.defName ?? "null"}"))
                        LogDecision($"Drawing_SingleHeldFallback_{pawn.ThingID}_{job.def?.defName ?? "null"}", $"[SurvivalTools.Drawing] {pawn.LabelShort} using single-held fallback {singleImprover.LabelCapNoCount} for {job.def?.defName ?? "null"}.");
                    return (singleImprover, requiredStats);
                }
            }
            catch { /* ignore fallback errors */ }

            // 1b) Defensive fallback: if core selection returned null (possibly due to delayed cache
            // initialization or stale factors), compute a one-off best candidate using the same
            // factor computation used by the runtime cache so the drawn tool matches the job's
            // effective stat contributor. This avoids showing the wrong tool (e.g., hammer when
            // the pawn is actually using an axe-like virtual tool for tree felling).
            try
            {
                float bestScore = 0f;
                Thing bestThing = null;

                var candidates = pawn.GetAllUsableSurvivalTools();
                if (candidates != null)
                {
                    var stats = requiredStats;
                    foreach (var cand in candidates)
                    {
                        if (cand == null) continue;

                        // Determine def + stuff for the candidate (virtual wrappers supply def)
                        ThingDef toolDef = cand.def;
                        ThingDef stuffDef = null;
                        SurvivalTool asTool = cand as SurvivalTool;
                        if (asTool != null) stuffDef = asTool.Stuff;

                        // Get base factors (may compute ad-hoc if cache not ready)
                        var factors = SurvivalToolUtility.ToolFactorCache.GetOrComputeToolFactors(toolDef, stuffDef, asTool);
                        if (factors == null || factors.Count == 0) continue;

                        // Sum relevant stat values
                        float score = 0f;
                        for (int si = 0; si < stats.Count; si++)
                        {
                            var sdef = stats[si];
                            if (sdef == null) continue;
                            for (int fi = 0; fi < factors.Count; fi++)
                            {
                                var fm = factors[fi];
                                if (fm?.stat == sdef) { score += fm.value; break; }
                            }
                        }

                        // Apply HP penalty for damaged physical tools
                        var backing = SurvivalToolUtility.BackingThing(asTool, pawn);
                        if (backing is Thing tb && tb.MaxHitPoints > 0 && tb.HitPoints < tb.MaxHitPoints)
                            score *= (float)tb.HitPoints / tb.MaxHitPoints;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestThing = cand;
                        }
                    }
                }

                if (bestThing != null) return (bestThing, requiredStats);
            }
            catch { /* fallback silently if anything goes wrong during draw-time selection */ }

            // 2) Fallback: wrap any relevant tool-stuff stack into a VirtualTool for display.
            var inner = pawn.inventory?.innerContainer;
            if (inner != null)
            {
                for (int i = 0; i < inner.Count; i++)
                {
                    var thing = inner[i];
                    if (thing?.def == null || !thing.def.IsToolStuff()) continue;

                    var ext = thing.def.GetModExtension<SurvivalToolProperties>();
                    if (ext?.baseWorkStatFactors == null) continue;

                    for (int m = 0; m < ext.baseWorkStatFactors.Count; m++)
                    {
                        var mod = ext.baseWorkStatFactors[m];
                        if (mod?.stat != null && requiredStats.Contains(mod.stat))
                            return (VirtualTool.FromThing(thing), requiredStats);
                    }
                }
            }

            return (null, requiredStats);
        }
    }
}
