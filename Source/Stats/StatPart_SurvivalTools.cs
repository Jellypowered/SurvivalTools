// RimWorld 1.6 / C# 7.3
// Source/Stats/StatPart_SurvivalTools.cs
//
// Phase 4: StatPart as the single math path for survival tool bonuses/penalties.
// Uses ToolScoring and ScoreCache for deterministic, cache-friendly calculations.

using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using SurvivalTools.Scoring;
using SurvivalTools.Helpers;

namespace SurvivalTools.Stats
{
    /// <summary>
    /// Phase 4: Unified StatPart for all survival tool stat modifications.
    /// Replaces scattered legacy stat math with a single deterministic path.
    /// </summary>
    public sealed class StatPart_SurvivalTools : StatPart
    {
        // Supported vanilla work stats (O(1) membership test)
        private static readonly HashSet<StatDef> SupportedWorkStats = new HashSet<StatDef>();

        // Cached StringBuilder for ExplanationPart (reuse to avoid allocations)
        private static readonly StringBuilder _explanationBuilder = new StringBuilder(256);

        // HP bucket tracking for cache invalidation (quantized to 10% steps)
        private static readonly Dictionary<int, int> _lastHpBuckets = new Dictionary<int, int>();

        // Lazy initialization flag
        private static bool _statsInitialized = false;

        /// <summary>
        /// Ensure supported stats are initialized (lazy, thread-safe)
        /// </summary>
        private static void EnsureSupportedStatsInitialized()
        {
            if (_statsInitialized) return;

            lock (SupportedWorkStats)
            {
                if (_statsInitialized) return;

                SupportedWorkStats.Clear();

                // Add vanilla work stats we support
                if (StatDefOf.MiningSpeed != null) SupportedWorkStats.Add(StatDefOf.MiningSpeed);
                if (StatDefOf.MiningYield != null) SupportedWorkStats.Add(StatDefOf.MiningYield);
                if (StatDefOf.ConstructionSpeed != null) SupportedWorkStats.Add(StatDefOf.ConstructionSpeed);
                if (StatDefOf.PlantWorkSpeed != null) SupportedWorkStats.Add(StatDefOf.PlantWorkSpeed);
                if (StatDefOf.PlantHarvestYield != null) SupportedWorkStats.Add(StatDefOf.PlantHarvestYield);

                // Add ST custom stats if available
                try
                {
                    if (ST_StatDefOf.DiggingSpeed != null) SupportedWorkStats.Add(ST_StatDefOf.DiggingSpeed);
                    if (ST_StatDefOf.TreeFellingSpeed != null) SupportedWorkStats.Add(ST_StatDefOf.TreeFellingSpeed);
                    if (ST_StatDefOf.PlantHarvestingSpeed != null) SupportedWorkStats.Add(ST_StatDefOf.PlantHarvestingSpeed);
                    if (ST_StatDefOf.SowingSpeed != null) SupportedWorkStats.Add(ST_StatDefOf.SowingSpeed);
                    if (ST_StatDefOf.MaintenanceSpeed != null) SupportedWorkStats.Add(ST_StatDefOf.MaintenanceSpeed);
                    if (ST_StatDefOf.DeconstructionSpeed != null) SupportedWorkStats.Add(ST_StatDefOf.DeconstructionSpeed);
                    if (ST_StatDefOf.ResearchSpeed != null) SupportedWorkStats.Add(ST_StatDefOf.ResearchSpeed);
                    if (ST_StatDefOf.CleaningSpeed != null) SupportedWorkStats.Add(ST_StatDefOf.CleaningSpeed);
                    if (ST_StatDefOf.MedicalOperationSpeed != null) SupportedWorkStats.Add(ST_StatDefOf.MedicalOperationSpeed);
                    if (ST_StatDefOf.MedicalSurgerySuccessChance != null) SupportedWorkStats.Add(ST_StatDefOf.MedicalSurgerySuccessChance);
                    if (ST_StatDefOf.ButcheryFleshSpeed != null) SupportedWorkStats.Add(ST_StatDefOf.ButcheryFleshSpeed);
                    if (ST_StatDefOf.ButcheryFleshEfficiency != null) SupportedWorkStats.Add(ST_StatDefOf.ButcheryFleshEfficiency);
                    if (ST_StatDefOf.MiningYieldDigging != null) SupportedWorkStats.Add(ST_StatDefOf.MiningYieldDigging);
                }
                catch
                {
                    // If ST stats are not available, continue with vanilla stats only
                }

                _statsInitialized = true;
            }
        }

        /// <summary>
        /// Transform stat value using unified tool scoring system.
        /// Zero allocations in hot path.
        /// </summary>
        public override void TransformValue(StatRequest req, ref float val)
        {
            EnsureSupportedStatsInitialized();

            if (parentStat == null || !SupportedWorkStats.Contains(parentStat))
                return;

            // Early-out blacklist
            if (!(req.Thing is Pawn pawn))
                return;

            if (pawn.RaceProps?.Animal == true || pawn.RaceProps?.IsMechanoid == true)
                return;

            if (pawn.IsQuestLodger() || pawn.Downed || pawn.GetPosture() != PawnPosture.Standing)
                return;

            // Check if pawn has required capacities (no LINQ)
            if (pawn.health?.capacities != null)
            {
                if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                    return;
                if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness))
                    return;
            }

            // Get the effective tool using ToolScoring
            var effectiveTool = ToolScoring.GetBestTool(pawn, parentStat, out float score);

            if (effectiveTool == null || score <= 0.001f)
            {
                // No meaningful tool - apply baseline penalty in Normal mode only
                var settings = SurvivalTools.Settings;
                if (settings != null && !settings.hardcoreMode && !settings.extraHardcoreMode && settings.enableNormalModePenalties)
                {
                    val *= settings.noToolStatFactorNormal;
                }
                // In Hardcore/Nightmare: do not block here, just leave value unmodified
                // The actual blocking happens in Phase 5 gating
                return;
            }

            // Check for HP bucket changes for cache invalidation
            CheckHpBucketChange(effectiveTool);

            // Apply tool factor using resolver (exactly matching current behavior)
            float toolFactor = ToolStatResolver.GetToolStatFactor(effectiveTool.def, effectiveTool.Stuff, parentStat);
            val *= toolFactor;

            // Phase 8: Wear pulse (only if factor actually modifies value and pawn currently has a job using this stat)
            try
            {
                // Pulse wear only if this tool provided an improvement over the toolless baseline.
                float baseline = SurvivalToolUtility.GetNoToolBaseline(parentStat);
                if (toolFactor > baseline + 0.001f)
                {
                    // Basic heuristic: if pawn has a current job and this stat is relevant, pulse wear.
                    // (Deep job->stat validation already happens earlier in scoring/gating; keep hot path cheap.)
                    if (pawn?.CurJob != null)
                    {
                        if (effectiveTool is SurvivalTool stTool)
                        {
                            Helpers.ST_WearService.TryPulseWear(pawn, stTool, parentStat);
                        }
                    }
                }
            }
            catch { /* swallow to avoid destabilizing stat math */ }
        }

        /// <summary>
        /// Provide explanation text for stat calculation.
        /// Uses cached StringBuilder to avoid allocations.
        /// </summary>
        public override string ExplanationPart(StatRequest req)
        {
            EnsureSupportedStatsInitialized();

            if (parentStat == null || !SupportedWorkStats.Contains(parentStat))
                return null;

            if (!(req.Thing is Pawn pawn) || !CanUseSurvivalTools(pawn))
                return null;

            _explanationBuilder.Clear();
            _explanationBuilder.AppendLine("ST_StatPart_Header".Translate());

            var effectiveTool = ToolScoring.GetBestTool(pawn, parentStat, out float score);

            if (effectiveTool == null || score <= 0.001f)
            {
                // No tool case
                var settings = SurvivalTools.Settings;
                if (settings != null && !settings.hardcoreMode && !settings.extraHardcoreMode && settings.enableNormalModePenalties)
                {
                    _explanationBuilder.AppendLine("ST_StatPart_NoToolPenalty".Translate(settings.noToolStatFactorNormal.ToStringPercent()));
                }
                else
                {
                    _explanationBuilder.AppendLine("ST_StatPart_NoToolPenalty".Translate("100%"));
                }
            }
            else
            {
                // Tool applied
                float toolFactor = ToolStatResolver.GetToolStatFactor(effectiveTool.def, effectiveTool.Stuff, parentStat);
                _explanationBuilder.AppendLine("ST_StatPart_ToolApplied".Translate(effectiveTool.LabelCap, toolFactor.ToStringPercent()));

                // Top contributors
                var contributors = ToolScoring.TopContributors(effectiveTool, pawn, parentStat, 2);
                for (int i = 0; i < contributors.Length; i++)
                {
                    var (thing, contribution) = contributors[i];
                    if (contribution > 0.001f)
                    {
                        _explanationBuilder.AppendLine("ST_StatPart_TopContrib".Translate(thing.LabelCap, (contribution * 100f).ToString("F1")));
                    }
                }
            }

            return _explanationBuilder.ToString();
        }

        /// <summary>
        /// Check if pawn can use survival tools (simplified check)
        /// </summary>
        private static bool CanUseSurvivalTools(Pawn pawn)
        {
            if (pawn?.RaceProps == null) return false;
            if (pawn.RaceProps.Animal || pawn.RaceProps.IsMechanoid) return false;
            if (pawn.IsQuestLodger() || pawn.Downed) return false;

            return pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) == true;
        }

        /// <summary>
        /// Check if tool's HP bucket has changed and invalidate cache if needed.
        /// Quantized to 10% steps to avoid excessive invalidation.
        /// </summary>
        private static void CheckHpBucketChange(Thing tool)
        {
            if (tool?.def?.useHitPoints != true || tool.MaxHitPoints <= 0)
                return;

            int thingId = tool.thingIDNumber;
            int currentBucket = (tool.HitPoints * 10) / tool.MaxHitPoints; // 0-10 scale

            if (_lastHpBuckets.TryGetValue(thingId, out int lastBucket))
            {
                if (currentBucket != lastBucket)
                {
                    ScoreCache.NotifyToolChanged(tool);
                    _lastHpBuckets[thingId] = currentBucket;
                }
            }
            else
            {
                _lastHpBuckets[thingId] = currentBucket;
            }
        }

        /// <summary>
        /// Clear HP bucket tracking for cleanup
        /// </summary>
        public static void ClearHpBucketTracking()
        {
            _lastHpBuckets.Clear();
        }
    }
}