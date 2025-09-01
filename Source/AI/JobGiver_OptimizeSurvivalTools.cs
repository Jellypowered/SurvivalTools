using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools
{
    public class JobGiver_OptimizeSurvivalTools : ThinkNode_JobGiver
    {
        // Tuning knobs - Base intervals for optimization
        private const int OPTIMIZE_TICK_MIN = 3600;   // ~3.0 in-game hours
        private const int OPTIMIZE_TICK_MAX = 14400;  // ~6.0 in-game hours

        // Reduced frequency when AutoTool is enabled (once per day)
        private const int OPTIMIZE_TICK_MIN_AUTOTOOL = 60000;  // ~24 in-game hours
        private const int OPTIMIZE_TICK_MAX_AUTOTOOL = 72000;  // ~30 in-game hours

        private void SetNextOptimizeTick(Pawn pawn, int min = -1, int max = -1)
        {
            // Use reduced frequency if AutoTool is enabled, otherwise use default
            if (min == -1 || max == -1)
            {
                if (SurvivalTools.Settings?.autoTool == true)
                {
                    min = OPTIMIZE_TICK_MIN_AUTOTOOL;
                    max = OPTIMIZE_TICK_MAX_AUTOTOOL;
                    string logKey = $"OptFreq_AutoTool_{pawn.ThingID}";
                    if (SurvivalToolUtility.IsDebugLoggingEnabled && SurvivalToolUtility.ShouldLogWithCooldown(logKey))
                        Log.Message($"[SurvivalTools.Optimizer] Using reduced optimization frequency for {pawn.LabelShort} (AutoTool enabled): ~{min / 2500f:F1}-{max / 2500f:F1} in-game hours");
                }
                else
                {
                    min = OPTIMIZE_TICK_MIN;
                    max = OPTIMIZE_TICK_MAX;
                    string logKey = $"OptFreq_Standard_{pawn.ThingID}";
                    if (SurvivalToolUtility.IsDebugLoggingEnabled && SurvivalToolUtility.ShouldLogWithCooldown(logKey))
                        Log.Message($"[SurvivalTools.Optimizer] Using standard optimization frequency for {pawn.LabelShort}: ~{min / 2500f:F1}-{max / 2500f:F1} in-game hours");
                }
            }

            var comp = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
            comp?.Optimized(min, max);
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (SurvivalTools.Settings == null || !SurvivalTools.Settings.toolOptimization)
                return null;

            var assignmentTracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
            if (!pawn.CanUseSurvivalTools() || assignmentTracker == null)
                return null;

            // Skip optimization if pawn is downed, unconscious, or on bed rest
            if (pawn.Downed || !pawn.Awake() || pawn.InBed())
            {
                SetNextOptimizeTick(pawn);
                return null;
            }

            var map = pawn.MapHeld;
            if (map == null)
            {
                SetNextOptimizeTick(pawn);
                return null;
            }

            var curAssignment = assignmentTracker.CurrentSurvivalToolAssignment;
            var heldTools = pawn.GetHeldSurvivalTools().ToList();

            // 1. Drop any tools that are no longer needed or allowed.
            foreach (var tool in heldTools)
            {
                if (tool is SurvivalTool st &&
                    (!curAssignment.filter.Allows(st) || !pawn.NeedsSurvivalTool(st)) &&
                    assignmentTracker.forcedHandler.AllowedToAutomaticallyDrop(tool))
                {
                    if (SurvivalToolUtility.IsDebugLoggingEnabled)
                        Log.Message($"[SurvivalTools.Optimizer] {pawn.LabelShort} is dropping unneeded tool: {tool.LabelShort}");

                    SetNextOptimizeTick(pawn, 300, 600); // Short cooldown after dropping
                    return pawn.DequipAndTryStoreSurvivalTool(tool);
                }
            }

            // 2. Drop duplicate tools (keeping only the best of each type).
            var duplicateToolToDrop = FindDuplicateToolToDrop(pawn, heldTools);
            if (duplicateToolToDrop != null)
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    Log.Message($"[SurvivalTools.Optimizer] {pawn.LabelShort} is dropping duplicate tool: {duplicateToolToDrop.LabelShort}");

                SetNextOptimizeTick(pawn, 300, 600); // Short cooldown after dropping
                return pawn.DequipAndTryStoreSurvivalTool(duplicateToolToDrop);
            }

            // 3. Determine which stats are relevant for the pawn's work.
            var workRelevantStats = pawn.AssignedToolRelevantWorkGiversStatDefs().Distinct().ToList();
            if (workRelevantStats.NullOrEmpty())
            {
                SetNextOptimizeTick(pawn);
                return null;
            }

            // 3. Check cooldown, but bypass if the pawn has no tool for a relevant job.
            bool hasAnyRelevantTool = heldTools.Any(t => t is SurvivalTool st && st.WorkStatFactors.Any(m => workRelevantStats.Contains(m.stat)));
            if (hasAnyRelevantTool && !assignmentTracker.NeedsOptimization)
                return null;

            if (SurvivalToolUtility.IsDebugLoggingEnabled)
                Log.Message($"[SurvivalTools.Optimizer] Running tool optimization for {pawn.LabelShort}. Relevant stats: {string.Join(", ", workRelevantStats.Select(s => s.defName))}");

            // 4. Find the best possible tool to acquire.
            SurvivalTool bestNewTool = FindBestToolToAcquire(pawn, workRelevantStats, curAssignment, heldTools);

            if (bestNewTool == null)
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    Log.Message($"[SurvivalTools.Optimizer] No better tool found for {pawn.LabelShort}.");
                SetNextOptimizeTick(pawn);
                return null;
            }

            // 5. Formulate job to swap tools.
            Thing toolToDrop = GetToolToDrop(pawn, bestNewTool, workRelevantStats, heldTools);
            int heldToolOffset = 0;

            if (toolToDrop != null && assignmentTracker.forcedHandler.AllowedToAutomaticallyDrop(toolToDrop))
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    Log.Message($"[SurvivalTools.Optimizer] {pawn.LabelShort} will drop {toolToDrop.LabelShort} to make space for {bestNewTool.LabelShort}.");
                pawn.jobs.jobQueue.EnqueueFirst(pawn.DequipAndTryStoreSurvivalTool(toolToDrop, enqueueCurrent: false));
                heldToolOffset = -1;
            }

            if (pawn.CanCarryAnyMoreSurvivalTools(heldToolOffset))
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    Log.Message($"[SurvivalTools.Optimizer] {pawn.LabelShort} is creating job to pick up {bestNewTool.LabelShort}.");

                var pickupJob = JobMaker.MakeJob(JobDefOf.TakeInventory, bestNewTool);
                pickupJob.count = 1;
                SetNextOptimizeTick(pawn, 600, 900); // Short cooldown during pickup
                return pickupJob;
            }

            if (SurvivalToolUtility.IsDebugLoggingEnabled)
                Log.Message($"[SurvivalTools.Optimizer] {pawn.LabelShort} found better tool {bestNewTool.LabelShort}, but cannot carry more.");

            SetNextOptimizeTick(pawn);
            return null;
        }

        private SurvivalTool FindBestToolToAcquire(Pawn pawn, List<StatDef> workRelevantStats, SurvivalToolAssignment curAssignment, List<Thing> heldTools)
        {
            SurvivalTool bestNewTool = null;
            float bestScore = 0f;

            // Initialize best score with the best currently held tool
            foreach (var tool in heldTools)
            {
                float score = SurvivalToolScore(tool, workRelevantStats);
                if (score > bestScore)
                {
                    bestScore = score;
                }
            }
            if (SurvivalToolUtility.IsDebugLoggingEnabled)
                Log.Message($"[SurvivalTools.Optimizer] {pawn.LabelShort}'s best current tool score: {bestScore:F2}");

            var potentialTools = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < potentialTools.Count; i++)
            {
                if (potentialTools[i] is SurvivalTool potentialTool &&
                    potentialTool.Spawned &&
                    !potentialTool.IsForbidden(pawn) &&
                    !potentialTool.IsBurning() &&
                    curAssignment.filter.Allows(potentialTool) &&
                    ToolIsAcquirableByPolicy(pawn, potentialTool) &&
                    pawn.CanReserveAndReach(potentialTool, PathEndMode.OnCell, pawn.NormalMaxDanger()))
                {
                    float potentialScore = SurvivalToolScore(potentialTool, workRelevantStats);

                    // Don't pick up tools that have no relevant stats (score would be 0)
                    if (potentialScore <= 0f)
                        continue;

                    // Check if pawn already has a tool of the same type
                    var sameTypeHeldTool = FindSameTypeHeldTool(pawn, potentialTool, heldTools);
                    if (sameTypeHeldTool != null)
                    {
                        float sameTypeScore = SurvivalToolScore(sameTypeHeldTool, workRelevantStats);

                        // Only consider this tool if it's better than the same-type tool we already have
                        if (potentialScore <= sameTypeScore)
                        {
                            if (SurvivalToolUtility.IsDebugLoggingEnabled)
                                Log.Message($"[SurvivalTools.Optimizer] {pawn.LabelShort} skipping {potentialTool.LabelShort} (score: {potentialScore:F2}) - already has better same-type tool {sameTypeHeldTool.LabelShort} (score: {sameTypeScore:F2})");
                            continue;
                        }

                        if (SurvivalToolUtility.IsDebugLoggingEnabled)
                            Log.Message($"[SurvivalTools.Optimizer] {pawn.LabelShort} considering {potentialTool.LabelShort} (score: {potentialScore:F2}) to replace same-type tool {sameTypeHeldTool.LabelShort} (score: {sameTypeScore:F2})");
                    }

                    if (SurvivalToolUtility.IsDebugLoggingEnabled)
                        Log.Message($"[SurvivalTools.Optimizer] {pawn.LabelShort} evaluating {potentialTool.LabelShort}: score {potentialScore:F2} vs best {bestScore:F2}");

                    if (potentialScore > bestScore)
                    {
                        bestScore = potentialScore;
                        bestNewTool = potentialTool;
                    }
                }
            }

            return bestNewTool;
        }

        private Thing GetToolToDrop(Pawn pawn, SurvivalTool newTool, List<StatDef> workRelevantStats, List<Thing> heldTools)
        {
            // First priority: if we're picking up a tool of the same type, drop the worse same-type tool
            var sameTypeHeldTool = FindSameTypeHeldTool(pawn, newTool, heldTools);
            if (sameTypeHeldTool != null)
            {
                var assignmentTracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
                if (assignmentTracker?.forcedHandler.AllowedToAutomaticallyDrop(sameTypeHeldTool) ?? true)
                {
                    if (SurvivalToolUtility.IsDebugLoggingEnabled)
                        Log.Message($"[SurvivalTools.Optimizer] {pawn.LabelShort} will drop same-type tool {sameTypeHeldTool.LabelShort} for better {newTool.LabelShort}.");
                    return sameTypeHeldTool;
                }
            }

            // If we're already at the carry limit, we must drop a tool.
            if (!pawn.CanCarryAnyMoreSurvivalTools())
            {
                // Find the worst tool we are currently holding.
                Thing worstTool = null;
                float worstScore = float.MaxValue;

                foreach (var tool in heldTools)
                {
                    var assignmentTracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
                    if (!(assignmentTracker?.forcedHandler.AllowedToAutomaticallyDrop(tool) ?? true))
                        continue;

                    float score = SurvivalToolScore(tool, workRelevantStats);
                    if (score < worstScore)
                    {
                        worstScore = score;
                        worstTool = tool;
                    }
                }
                return worstTool;
            }
            return null;
        }

        private static bool ToolIsAcquirableByPolicy(Pawn pawn, SurvivalTool tool)
        {
            if (tool.IsInAnyStorage())
                return true;

            if (SurvivalTools.Settings?.pickupFromStorageOnly == true)
                return false;

            return pawn.Map.areaManager.Home[tool.Position];
        }

        private static float SurvivalToolScore(Thing toolThing, List<StatDef> workRelevantStats)
        {
            if (!(toolThing is SurvivalTool tool))
                return 0f;

            float optimality = 0f;
            var workStatFactors = tool.WorkStatFactors.ToList();

            foreach (var stat in workRelevantStats)
            {
                // Only count stats that the tool actually has modifiers for
                var modifier = workStatFactors.FirstOrDefault(m => m.stat == stat);
                if (modifier != null)
                {
                    optimality += modifier.value;
                }
            }

            if (tool.def.useHitPoints)
            {
                float hpFrac = tool.MaxHitPoints > 0 ? (float)tool.HitPoints / tool.MaxHitPoints : 0f;
                float lifespanRemaining = tool.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan) * hpFrac;
                optimality *= LifespanDaysToOptimalityMultiplierCurve.Evaluate(lifespanRemaining);
            }

            return optimality;
        }

        private SurvivalTool FindDuplicateToolToDrop(Pawn pawn, List<Thing> heldTools)
        {
            var assignmentTracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
            var survivalTools = heldTools.OfType<SurvivalTool>().ToList();

            // Group tools by their functional type (same stat modifiers)
            var toolGroups = new Dictionary<string, List<SurvivalTool>>();

            foreach (var tool in survivalTools)
            {
                // Create a key based on the stats the tool modifies
                var statsKey = string.Join(",", tool.WorkStatFactors.Select(f => f.stat.defName).OrderBy(s => s));

                if (string.IsNullOrEmpty(statsKey))
                    continue; // Skip tools with no stat modifiers

                if (!toolGroups.ContainsKey(statsKey))
                    toolGroups[statsKey] = new List<SurvivalTool>();

                toolGroups[statsKey].Add(tool);
            }

            // Find groups with multiple tools (duplicates)
            foreach (var group in toolGroups.Values)
            {
                if (group.Count > 1)
                {
                    // Find the best tool in this group
                    var bestTool = group.OrderByDescending(t => CalculateToolScore(t)).First();

                    // Find a worse tool to drop (that we're allowed to drop)
                    var toolToDrop = group
                        .Where(t => t != bestTool && (assignmentTracker?.forcedHandler.AllowedToAutomaticallyDrop(t) ?? true))
                        .OrderBy(t => CalculateToolScore(t))
                        .FirstOrDefault();

                    if (toolToDrop != null)
                    {
                        if (SurvivalToolUtility.IsDebugLoggingEnabled)
                            Log.Message($"[SurvivalTools.Optimizer] {pawn.LabelShort} found duplicate tools - keeping {bestTool.LabelShort}, dropping {toolToDrop.LabelShort}");
                        return toolToDrop;
                    }
                }
            }

            return null;
        }

        private float CalculateToolScore(SurvivalTool tool)
        {
            // Calculate a general score for the tool based on all its modifiers and condition
            float score = 0f;

            foreach (var modifier in tool.WorkStatFactors)
            {
                score += modifier.value;
            }

            // Factor in tool condition
            if (tool.def.useHitPoints)
            {
                float hpFrac = tool.MaxHitPoints > 0 ? (float)tool.HitPoints / tool.MaxHitPoints : 0f;
                float lifespanRemaining = tool.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan) * hpFrac;
                score *= LifespanDaysToOptimalityMultiplierCurve.Evaluate(lifespanRemaining);
            }

            return score;
        }

        private static SurvivalTool FindSameTypeHeldTool(Pawn pawn, SurvivalTool targetTool, List<Thing> heldTools)
        {
            return heldTools.OfType<SurvivalTool>()
                .FirstOrDefault(heldTool => AreSameToolType(heldTool, targetTool));
        }

        private static bool AreSameToolType(SurvivalTool tool1, SurvivalTool tool2)
        {
            // Tools are the same type if they modify the same set of stats
            var tool1Stats = tool1.WorkStatFactors.Select(f => f.stat).ToHashSet();
            var tool2Stats = tool2.WorkStatFactors.Select(f => f.stat).ToHashSet();

            // If both tools have no stat modifiers, they're not useful tools
            if (tool1Stats.Count == 0 || tool2Stats.Count == 0)
                return false;

            // Tools are same type if they have the same set of stat modifiers
            return tool1Stats.SetEquals(tool2Stats);
        }

        private static readonly SimpleCurve LifespanDaysToOptimalityMultiplierCurve = new SimpleCurve
        {
            new CurvePoint(0f,   0.04f),
            new CurvePoint(0.5f, 0.20f),
            new CurvePoint(1f,   0.50f),
            new CurvePoint(2f,   1.00f),
            new CurvePoint(4f,   1.00f),
            new CurvePoint(999f, 10.0f)
        };
    }
}