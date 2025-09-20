// RimWorld 1.6 / C# 7.3
// Source/Stats/StatPart_SurvivalTool.cs
using System.Linq;
using System.Collections.Concurrent;
using Verse;
using RimWorld;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;
using Verse.AI;

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
                // HOT PATH – no LINQ
                float factor = CollectionExtensions.GetStatFactorFromList(tool.WorkStatFactors, parentStat);
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
                    // HOT PATH – no LINQ
                    float factor = CollectionExtensions.GetStatFactorFromList(bestTool.WorkStatFactors, parentStat);
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
                // HOT PATH – no LINQ
                float factor = CollectionExtensions.GetStatFactorFromList(tool.WorkStatFactors, parentStat);
                if (factor > 0f)
                    val *= factor;
                return;
            }

            // Handle tool-stuff showing its own stats
            if (parentStat == null) return;

            // Handle tool showing its own stats (duplicate check for other code paths)
            if (req.Thing is SurvivalTool toolInst)
            {
                // HOT PATH – no LINQ
                float factor = CollectionExtensions.GetStatFactorFromList(toolInst.WorkStatFactors, parentStat);
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
                        // HOT PATH – no LINQ
                        float factor = CollectionExtensions.GetStatFactorFromList(bestTool.WorkStatFactors, parentStat);
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

                    // HOT PATH – no LINQ
                    Thing toolStuff = null;
                    var usableTools = pawn.GetAllUsableSurvivalTools();
                    if (usableTools != null)
                    {
                        foreach (var t in usableTools)
                        {
                            if (t?.def?.IsToolStuff() == true)
                            {
                                var props = t.def.GetModExtension<SurvivalToolProperties>();
                                if (props?.baseWorkStatFactors != null)
                                {
                                    foreach (var mod in props.baseWorkStatFactors)
                                    {
                                        if (mod?.stat == parentStat)
                                        {
                                            toolStuff = t;
                                            break;
                                        }
                                    }
                                    if (toolStuff != null) break;
                                }
                            }
                        }
                    }
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
                    // If parent stat is CleaningSpeed or WorkSpeedGlobal and the job already
                    // requires some other gated work stat, do not apply the cleaning/global fallback.
                    if ((parentStat == ST_StatDefOf.CleaningSpeed || parentStat == ST_StatDefOf.WorkSpeedGlobal) && pawn.CurJob != null)
                    {
                        var jobStats = SurvivalToolUtility.StatsForJob(pawn.CurJob.def, pawn);
                        if (jobStats != null)
                        {
                            foreach (var s in jobStats)
                            {
                                if (s != null && s != parentStat && s.RequiresSurvivalTool())
                                {
                                    // Another gated stat is in effect for this job; do not apply cleaning/global penalty here.
                                    return;
                                }
                            }
                        }
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
                    // Non-spammy logging: only log once per pawn/stat per cooldown
                    string logKey = $"StatPart_NoTool_{pawn?.ThingID ?? "null"}_{parentStat?.defName ?? "null"}";
                    if (ST_Logging.ShouldLogWithCooldown(logKey))
                        LogDebug($"StatPart_SurvivalTool: pawn={pawn?.LabelShort ?? "null"} job={pawn?.CurJob?.def?.defName ?? "null"} stat={parentStat?.defName ?? "null"} noToolPenalty={penaltyFactor} valBefore={valBeforeNT} valAfter={valAfterNT}", logKey);

                    // Spawn a mote once when a job begins that suffers a penalty for cleaning/global work
                    TrySpawnPenaltyMote(pawn, parentStat, penaltyFactor);
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
        // Kept as a fallback for safety, but runtime value should come from settings.
        private float noToolStatFactor = 0.3f;

        // Expose NoToolStatFactor as a public property; prefer the value from settings when available
        public float NoToolStatFactor
        {
            get
            {
                try
                {
                    // Settings provide the user-tweakable 'noToolStatFactorNormal'
                    var s = SurvivalTools.Settings?.noToolStatFactorNormal;
                    if (s != null) return s.Value;
                }
                catch { }
                return noToolStatFactor;
            }
        }

        // Expose penalty logic as a public method
        public float GetNoToolPenaltyFactor()
        {
            // Delegate computation + caching to the centralized ToolFactorCache
            try
            {
                // If parentStat is null fall back to the default
                if (parentStat == null) return noToolStatFactor;
                return SurvivalToolUtility.ToolFactorCache.GetOrComputeNoToolPenalty(parentStat);
            }
            catch
            {
                return noToolStatFactor;
            }
        }

        // Track last job reference per pawn to ensure motes spawn only once per job start
        private static readonly ConcurrentDictionary<int, Job> _lastMoteJob = new ConcurrentDictionary<int, Job>();

        private static void TrySpawnPenaltyMote(Pawn pawn, StatDef stat, float penaltyFactor)
        {
            if (pawn == null || pawn.CurJob == null) return;

            // Only apply motes for CleaningSpeed or WorkSpeedGlobal
            if (stat != ST_StatDefOf.CleaningSpeed && stat != ST_StatDefOf.WorkSpeedGlobal) return;

            // If the current job actually uses other gated stats, skip spawning for the cleaning/global fallback
            var jobStats = SurvivalToolUtility.StatsForJob(pawn.CurJob.def, pawn);
            if (jobStats != null)
            {
                foreach (var s in jobStats)
                {
                    if (s == null || s == stat) continue;
                    if (s.RequiresSurvivalTool())
                    {
                        // Another gated stat is in use; do not show cleaning/global fallback mote
                        return;
                    }
                }
            }

            Job current = pawn.CurJob;
            if (_lastMoteJob.TryGetValue(pawn.thingIDNumber, out var recorded) && ReferenceEquals(recorded, current))
                return; // already shown for this job

            // Record the job so we don't spam motes
            _lastMoteJob[pawn.thingIDNumber] = current;

            try
            {
                string statLabel = stat == ST_StatDefOf.WorkSpeedGlobal ? "Global" : "Cleaning";
                string shortText = $"Slow ({statLabel} x{penaltyFactor.ToString("F2")})";
                // Position above pawn
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, shortText, 3.5f);

                // Non-spammy log once per pawn/stat per cooldown
                string logk = $"SurvivalTools.StatPart_{pawn.ThingID}_{stat.defName}";
                if (ST_Logging.ShouldLogWithCooldown(logk))
                    ST_Logging.LogDebug($"[SurvivalTools.StatPart] {pawn.LabelShort} missing {statLabel} tool — penalty applied ({penaltyFactor.ToString("F2")}x).", logk);
            }
            catch { /* best-effort mote */ }
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



