using System;
using System.Linq;
using System.Collections.Concurrent;
using Verse;
using RimWorld;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools
{
    public class StatPart_SurvivalTool : StatPart
    {
        // Track how many times we've emitted a full zero-value diagnostic for a given pawn+stat
        // Key: "{pawnThingID}|{statDefName}" -> count
        private static readonly ConcurrentDictionary<string, int> _zeroWarningCounts = new ConcurrentDictionary<string, int>();

        // Track whether we've already written a simulation trace file for a given pawn+stat
        // Key: "{pawnThingID}|{statDefName}" -> true
        private static readonly ConcurrentDictionary<string, bool> _simTraceWritten = new ConcurrentDictionary<string, bool>();
        // Track whether we've already written a detailed diagnostic trace for a given pawn+stat
        private static readonly ConcurrentDictionary<string, bool> _detailedTraceWritten = new ConcurrentDictionary<string, bool>();

        public override string ExplanationPart(StatRequest req)
        {
            if (parentStat == null) return null;

            // Handle tool showing its own stats
            if (req.Thing is SurvivalTool tool)
            {
                // Tools should show their own stat modifiers for all relevant stats
                float factor = CollectionExtensions.GetStatFactorFromList(tool.WorkStatFactors.ToList(), parentStat);
                if (factor > 0f)
                    return $"{tool.LabelCapNoCount}: x{factor.ToStringPercent()}";
                return null;
            }

            // Handle tool-stuff showing its own stats
            if (req.Thing?.def.IsToolStuff() == true)
            {
                // First try SurvivalToolProperties (for things like cloth)
                var props = req.Thing.def.GetModExtension<SurvivalToolProperties>();
                if (props?.baseWorkStatFactors != null)
                {
                    float factor = CollectionExtensions.GetStatFactorFromList(props.baseWorkStatFactors, parentStat);
                    if (factor > 0f)
                        return $"{req.Thing.LabelCapNoCount}: x{factor.ToStringPercent()}";
                }

                // Then try StuffPropsTool (for things like Bioferrite/Obsidian)
                var stuffProps = req.Thing.def.GetModExtension<StuffPropsTool>();
                if (stuffProps?.toolStatFactors != null)
                {
                    float factor = CollectionExtensions.GetStatFactorFromList(stuffProps.toolStatFactors, parentStat);
                    if (factor > 0f)
                        return $"{req.Thing.LabelCapNoCount}: x{factor.ToStringPercent()}";
                }
            }

            if (req.Thing is Pawn pawn && PawnToolValidator.CanUseSurvivalTools(pawn))
            {
                // First try: actual SurvivalTool instance (best tool)
                var bestTool = pawn.GetBestSurvivalTool(parentStat);
                if (bestTool != null)
                {
                    float factor = CollectionExtensions.GetStatFactorFromList(bestTool.WorkStatFactors.ToList(), parentStat);
                    return $"{bestTool.LabelCapNoCount}: x{factor.ToStringPercent()}";
                }

                // Second try: tool-stuff (cloth/wool/etc.) residing on pawn (inventory/equipment)
                var toolStuff = pawn.GetAllUsableSurvivalTools()
                                    .FirstOrDefault(t => t.def.IsToolStuff() &&
                                                         t.def.GetModExtension<SurvivalToolProperties>()?
                                                           .baseWorkStatFactors?.Any(m => m.stat == parentStat) == true);
                if (toolStuff != null)
                {
                    // get factor from the def's baseWorkStatFactors
                    var props = toolStuff.def.GetModExtension<SurvivalToolProperties>();
                    float factor = props?.baseWorkStatFactors != null ?
                        CollectionExtensions.GetStatFactorFromList(props.baseWorkStatFactors, parentStat) :
                        NoToolStatFactor;
                    return $"{toolStuff.LabelCapNoCount}: x{factor.ToStringPercent()}";
                }

                return $"{"NoTool".Translate()}: x{NoToolStatFactor.ToStringPercent()}";
            }
            return null;
        }

        // Static flag to prevent recursive stat calculation calls (thread-local to avoid cross-call leakage)
        [System.ThreadStatic]
        private static bool _isCalculatingStats;

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (parentStat == null) return;

            // If we're already calculating stats on this thread, bail out to avoid re-entrancy
            // (other codepaths like GetBestSurvivalTool may call GetStatValue and re-enter here)
            if (_isCalculatingStats)
            {
                // Do not apply survival-tool modifiers during recursive stat evaluation
                return;
            }

            // Handle tool showing its own stats
            if (req.Thing is SurvivalTool tool)
            {
                float factor = CollectionExtensions.GetStatFactorFromList(tool.WorkStatFactors.ToList(), parentStat);
                if (factor > 0f)
                    val *= factor;
                return;
            }

            // Handle tool-stuff showing its own stats
            if (parentStat == null) return;

            // Handle tool showing its own stats (duplicate check for other code paths)
            if (req.Thing is SurvivalTool toolInst)
            {
                float factor = CollectionExtensions.GetStatFactorFromList(toolInst.WorkStatFactors.ToList(), parentStat);
                if (factor > 0f)
                    val *= factor;
                return;
            }

            // Handle tool-stuff showing its own stats
            if (req.Thing?.def.IsToolStuff() == true)
            {
                var props = req.Thing.def.GetModExtension<SurvivalToolProperties>();
                if (props?.baseWorkStatFactors != null)
                {
                    float factor = CollectionExtensions.GetStatFactorFromList(props.baseWorkStatFactors, parentStat);
                    if (factor > 0f)
                    {
                        val *= factor;
                        return;
                    }
                }

                var stuffProps = req.Thing.def.GetModExtension<StuffPropsTool>();
                if (stuffProps?.toolStatFactors != null)
                {
                    float factor = CollectionExtensions.GetStatFactorFromList(stuffProps.toolStatFactors, parentStat);
                    if (factor > 0f)
                    {
                        val *= factor;
                        return;
                    }
                }
            }

            if (req.Thing is Pawn pawn && PawnToolValidator.CanUseSurvivalTools(pawn))
            {
                bool wasCalculating = _isCalculatingStats;
                _isCalculatingStats = true;
                try
                {
                    var bestTool = pawn.GetBestSurvivalTool(parentStat);
                    if (bestTool != null)
                    {
                        float factor = CollectionExtensions.GetStatFactorFromList(bestTool.WorkStatFactors.ToList(), parentStat);
                        float valBefore = val;
                        float effectiveBase;
                        var partsForStat = parentStat?.parts;
                        if (valBefore > 0f)
                        {
                            effectiveBase = valBefore;
                        }
                        else if (partsForStat == null || partsForStat.Count <= 1)
                        {
                            effectiveBase = parentStat?.defaultBaseValue ?? 1f;
                        }
                        else
                        {
                            effectiveBase = valBefore;
                        }
                        float valAfter = effectiveBase * factor;
                        LogStatPartSummary(pawn, parentStat, pawn?.CurJob?.def, bestTool.LabelCapNoCount, factor, $"valBefore={valBefore} valAfter={valAfter}");
                        val = valAfter;
                        return;
                    }

                    var toolStuff = pawn.GetAllUsableSurvivalTools()
                        .FirstOrDefault(t => t.def.IsToolStuff() &&
                            t.def.GetModExtension<SurvivalToolProperties>()?.baseWorkStatFactors?.Any(m => m.stat == parentStat) == true);
                    if (toolStuff != null)
                    {
                        var props = toolStuff.def.GetModExtension<SurvivalToolProperties>();
                        float factor = props?.baseWorkStatFactors != null ?
                            CollectionExtensions.GetStatFactorFromList(props.baseWorkStatFactors, parentStat) :
                            this.NoToolStatFactor;
                        float valBeforeTS = val;
                        float effectiveBaseTS;
                        var partsForStatTS = parentStat?.parts;
                        if (valBeforeTS > 0f)
                        {
                            effectiveBaseTS = valBeforeTS;
                        }
                        else if (partsForStatTS == null || partsForStatTS.Count <= 1)
                        {
                            effectiveBaseTS = parentStat?.defaultBaseValue ?? 1f;
                            LogDebug($"StatPart_SurvivalTool: using defaultBaseValue fallback (toolStuff) for pawn={pawn?.LabelShort ?? "null"} stat={parentStat?.defName ?? "null"}");
                        }
                        else
                        {
                            effectiveBaseTS = valBeforeTS;
                        }
                        float valAfterTS = effectiveBaseTS * factor;
                        LogDebug($"StatPart_SurvivalTool: pawn={pawn?.LabelShort ?? "null"} job={pawn?.CurJob?.def?.defName ?? "null"} stat={parentStat?.defName ?? "null"} toolStuff={toolStuff.LabelCapNoCount} factor={factor} valBefore={valBeforeTS} valAfter={valAfterTS}", $"StatPart_ToolStuff_{pawn?.ThingID ?? "null"}_{parentStat?.defName ?? "null"}");
                        val = valAfterTS;
                        return;
                    }
                    float penaltyFactor = this.GetNoToolPenaltyFactor();
                    float valBeforeNT = val;
                    float effectiveBaseNT;
                    var partsForStatNT = parentStat?.parts;
                    if (valBeforeNT > 0f)
                    {
                        effectiveBaseNT = valBeforeNT;
                    }
                    else if (partsForStatNT == null || partsForStatNT.Count <= 1)
                    {
                        effectiveBaseNT = parentStat?.defaultBaseValue ?? 1f;
                        LogDebug($"StatPart_SurvivalTool: using defaultBaseValue fallback (noTool) for pawn={pawn?.LabelShort ?? "null"} stat={parentStat?.defName ?? "null"}");
                    }
                    else
                    {
                        effectiveBaseNT = valBeforeNT;
                    }
                    float valAfterNT = effectiveBaseNT * penaltyFactor;
                    LogDebug($"StatPart_SurvivalTool: pawn={pawn?.LabelShort ?? "null"} job={pawn?.CurJob?.def?.defName ?? "null"} stat={parentStat?.defName ?? "null"} noToolPenalty={penaltyFactor} valBefore={valBeforeNT} valAfter={valAfterNT}", $"StatPart_NoTool_{pawn?.ThingID ?? "null"}_{parentStat?.defName ?? "null"}");
                    val = valAfterNT;
                }
                finally
                {
                    _isCalculatingStats = wasCalculating;
                }
            }
            // ...existing code...
        }

        // Default factor when no tool is used (non-hardcore).
        private float noToolStatFactor = 0.3f;

        // Factor when no tool is used in hardcore mode (usually 0).
        private float noToolStatFactorHardcore = 0f;

        // Factor when no tool is used in extra hardcore mode (usually 0, can be specified separately).
        private float? noToolStatFactorExtraHardcore = null;

        // Expose NoToolStatFactor as a public property
        public float NoToolStatFactor => noToolStatFactor;

        // Expose penalty logic as a public method
        public float GetNoToolPenaltyFactor()
        {
            var settings = SurvivalTools.Settings;


            // Extra-hardcore mode: disallow jobs for required optional stats if tools are required
            if (settings?.extraHardcoreMode == true)
            {
                if (parentStat != null && settings.IsStatRequiredInExtraHardcore(parentStat))
                {
                    return 0f;
                }
                // If an explicit extra-hardcore factor was provided, use it; otherwise fallback to 0
                return noToolStatFactorExtraHardcore ?? 0f;
            }

            // Hardcore mode: use hardcore factor (usually 0)
            if (settings?.hardcoreMode == true)
            {
                return noToolStatFactorHardcore;
            }

            // Normal mode: respect global toggle
            if (settings != null && !settings.enableNormalModePenalties)
            {
                return 0.75f; // In normal mode, no work is disabled but is more efficient with tools
            }

            // Optional stats are unaffected in normal mode
            if (StatFilters.IsOptionalStat(parentStat))
                return 1f;

            // Special-case WorkSpeedGlobal: only apply penalties if any gate-eligible job is enabled in settings
            if (parentStat == ST_StatDefOf.WorkSpeedGlobal && settings != null)
            {
                var jobDict = settings.workSpeedGlobalJobGating;
                if (jobDict != null)
                {
                    bool anyGateEligibleJobEnabled = false;
                    foreach (var kvp in jobDict)
                    {
                        var jobDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail(kvp.Key);
                        if (jobDef != null && SurvivalToolUtility.ShouldGateByDefault(jobDef) && kvp.Value)
                        {
                            anyGateEligibleJobEnabled = true;
                            break;
                        }
                    }
                    if (!anyGateEligibleJobEnabled) return 1f;
                }
            }

            // Otherwise use the user-configurable normal mode factor if available
            return settings?.noToolStatFactorNormal ?? noToolStatFactor;
        }

        /// <summary>
        /// Determines if a stat is work-related and should have tool factors applied.
        /// This excludes item construction stats like WorkToMake, MaxHitPoints, DeteriorationRate, etc.
        /// </summary>
        private bool IsWorkRelatedStat(StatDef stat)
        {
            if (stat == null) return false;

            // These are the work stats that tools should affect
            var workStats = new[]
            {
                // Core work stats
                "TreeFellingSpeed", "PlantHarvestingSpeed", "SowingSpeed", "DiggingSpeed", "MiningYieldDigging",
                "ConstructionSpeed", "DeconstructionSpeed", "MaintenanceSpeed", "ResearchSpeed",
                "ButcheryFleshSpeed", "ButcheryFleshEfficiency", "MedicalOperationSpeed", "MedicalSurgerySuccessChance",
                "MedicalTendQuality", "CleaningSpeed", "WorkSpeedGlobal"
            };

            return workStats.Contains(stat.defName);
        }
    }
}



