
// RimWorld 1.6 / C# 7.3
// Source/Assign/AssignmentSearch.cs
//
// Phase 6: Pre-work auto-equip without ping-pong
// - Searches for better tools before starting work
// - Respects carry limits by difficulty
// - Implements hysteresis to prevent re-upgrading
// - LINQ-free, pooled buffers for performance

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using SurvivalTools.Scoring;
using SurvivalTools.Helpers;

namespace SurvivalTools.Assign
{
    public static class AssignmentSearch
    {
        // Pooled collections to avoid allocations
        private static readonly List<Thing> _candidateBuffer = new List<Thing>(64);
        private static readonly List<Thing> _inventoryBuffer = new List<Thing>(16);
        private static readonly List<Thing> _stockpileBuffer = new List<Thing>(32);

        // Hysteresis tracking: pawnID -> (lastUpgradeTick, lastEquippedDefName)
        private static readonly Dictionary<int, HysteresisData> _hysteresisData = new Dictionary<int, HysteresisData>();

        // Anti-recursion: pawnID -> processing flag
        private static readonly Dictionary<int, bool> _processingPawns = new Dictionary<int, bool>();

        private const int HysteresisTicksNormal = 5000;
        private const float HysteresisExtraGainPct = 0.05f; // +5% for re-upgrade
        private const float GatingEpsilon = 0.001f;

        private struct HysteresisData
        {
            public int lastUpgradeTick;
            public string lastEquippedDefName;
        }

        private struct ToolCandidate
        {
            public Thing tool;
            public float score;
            public float gainPct;
            public int pathCost;
            public ToolLocation location;
        }

        private enum ToolLocation
        {
            Inventory,
            Equipment,
            SameCell,
            Stockpile,
            HomeArea,
            Nearby
        }

        /// <summary>
        /// Returns true if we queued an equip/haul job; original job should be retried afterward.
        /// </summary>
        public static bool TryUpgradeFor(Pawn pawn, StatDef workStat, float minGainPct, float radius, int pathCostBudget)
        {
            Log.Message($"[SurvivalTools.Assignment] TryUpgradeFor called: pawn={pawn?.LabelShort}, workStat={workStat?.defName}, minGainPct={minGainPct:P1}, radius={radius}, pathCostBudget={pathCostBudget}");

            // Early-out blacklist
            if (!CanPawnUpgrade(pawn))
            {
                Log.Message($"[SurvivalTools.Assignment] CanPawnUpgrade failed for {pawn?.LabelShort}");
                return false;
            }

            // Anti-recursion check
            int pawnID = pawn.thingIDNumber;
            if (_processingPawns.TryGetValue(pawnID, out bool processing) && processing)
            {
                Log.Message($"[SurvivalTools.Assignment] Anti-recursion triggered for {pawn.LabelShort}");
                return false;
            }

            try
            {
                _processingPawns[pawnID] = true;
                bool result = TryUpgradeForInternal(pawn, workStat, minGainPct, radius, pathCostBudget);
                Log.Message($"[SurvivalTools.Assignment] TryUpgradeFor result for {pawn.LabelShort}: {result}");
                return result;
            }
            finally
            {
                _processingPawns[pawnID] = false;
            }
        }

        private static bool TryUpgradeForInternal(Pawn pawn, StatDef workStat, float minGainPct, float radius, int pathCostBudget)
        {
            if (workStat == null)
            {
                Log.Message($"[SurvivalTools.Assignment] workStat is null for {pawn?.LabelShort}");
                return false;
            }

            // Get current score and tool
            var currentTool = ToolScoring.GetBestTool(pawn, workStat, out float currentScore);
            string currentDefName = currentTool?.def?.defName ?? "none";
            Log.Message($"[SurvivalTools.Assignment] Current tool for {pawn.LabelShort} / {workStat.defName}: {currentDefName} (score: {currentScore:F3})");

            // Check hysteresis
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (IsInHysteresis(pawn.thingIDNumber, currentTick, currentDefName, minGainPct))
            {
                Log.Message($"[SurvivalTools.Assignment] Hysteresis check failed for {pawn.LabelShort}");
                return false;
            }

            // Find best candidate
            var candidate = FindBestCandidate(pawn, workStat, currentScore, minGainPct, radius, pathCostBudget);
            if (candidate.tool == null)
            {
                Log.Message($"[SurvivalTools.Assignment] No candidate tool found for {pawn.LabelShort}");
                return false;
            }

            Log.Message($"[SurvivalTools.Assignment] Found candidate tool for {pawn.LabelShort}: {candidate.tool.LabelShort} (score: {candidate.score:F3}, gain: {candidate.gainPct:P1}, location: {candidate.location})");

            // Queue the job to acquire/equip the tool
            if (QueueAcquisitionJob(pawn, candidate))
            {
                Log.Message($"[SurvivalTools.Assignment] Successfully queued acquisition job for {pawn.LabelShort}: {candidate.tool.LabelShort}");

                // Update hysteresis
                _hysteresisData[pawn.thingIDNumber] = new HysteresisData
                {
                    lastUpgradeTick = currentTick,
                    lastEquippedDefName = candidate.tool.def.defName
                };

                // Notify score cache
                ScoreCache.NotifyInventoryChanged(pawn);
                if (candidate.tool != null)
                    ScoreCache.NotifyToolChanged(candidate.tool);

                return true;
            }
            else
            {
                Log.Message($"[SurvivalTools.Assignment] Failed to queue acquisition job for {pawn.LabelShort}: {candidate.tool.LabelShort}");
            }

            return false;
        }

        private static bool CanPawnUpgrade(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed)
                return false;

            if (pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                return false;

            if (pawn.guest != null && !pawn.IsColonist)
                return false;

            if (!pawn.Awake())
                return false;

            // Check essential capacities
            if (pawn.health?.capacities == null)
                return false;

            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                return false;

            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
                return false;

            return true;
        }

        private static bool IsInHysteresis(int pawnID, int currentTick, string currentDefName, float minGainPct)
        {
            if (!_hysteresisData.TryGetValue(pawnID, out var data))
                return false;

            int ticksSinceUpgrade = currentTick - data.lastUpgradeTick;
            if (ticksSinceUpgrade < HysteresisTicksNormal)
            {
                // Still in hysteresis period - require extra gain to re-upgrade
                if (data.lastEquippedDefName == currentDefName)
                {
                    // Same tool, need extra gain
                    return minGainPct < (minGainPct + HysteresisExtraGainPct);
                }
            }

            return false;
        }

        private static ToolCandidate FindBestCandidate(Pawn pawn, StatDef workStat, float currentScore, float minGainPct, float radius, int pathCostBudget)
        {
            var bestCandidate = new ToolCandidate();
            float baseline = SurvivalToolUtility.GetNoToolBaseline(workStat);
            bool needsGatingRescue = currentScore <= baseline + GatingEpsilon;

            // Search order: inventory → equipment → same cell → stockpiles → home area → nearby
            SearchInventory(pawn, workStat, currentScore, minGainPct, needsGatingRescue, ref bestCandidate);
            if (bestCandidate.tool != null && bestCandidate.location == ToolLocation.Inventory)
                return bestCandidate; // Already in inventory, best case

            SearchEquipment(pawn, workStat, currentScore, minGainPct, needsGatingRescue, ref bestCandidate);
            if (bestCandidate.tool != null && bestCandidate.location == ToolLocation.Equipment)
                return bestCandidate; // On belt/pack, very good

            SearchSameCell(pawn, workStat, currentScore, minGainPct, needsGatingRescue, ref bestCandidate);
            if (bestCandidate.tool != null && bestCandidate.location == ToolLocation.SameCell)
                return bestCandidate; // Same cell, excellent

            SearchStockpiles(pawn, workStat, currentScore, minGainPct, needsGatingRescue, radius, pathCostBudget, ref bestCandidate);
            SearchHomeArea(pawn, workStat, currentScore, minGainPct, needsGatingRescue, radius, pathCostBudget, ref bestCandidate);
            SearchNearby(pawn, workStat, currentScore, minGainPct, needsGatingRescue, radius, pathCostBudget, ref bestCandidate);

            return bestCandidate;
        }

        private static void SearchInventory(Pawn pawn, StatDef workStat, float currentScore, float minGainPct, bool needsGatingRescue, ref ToolCandidate bestCandidate)
        {
            var inventory = pawn.inventory?.innerContainer;
            if (inventory == null)
                return;

            _inventoryBuffer.Clear();
            for (int i = 0; i < inventory.Count; i++)
            {
                var thing = inventory[i];
                if (thing != null && IsValidTool(thing, workStat))
                    _inventoryBuffer.Add(thing);
            }

            EvaluateCandidates(_inventoryBuffer, pawn, workStat, currentScore, minGainPct, needsGatingRescue,
                               ToolLocation.Inventory, 0, ref bestCandidate);
        }

        private static void SearchEquipment(Pawn pawn, StatDef workStat, float currentScore, float minGainPct, bool needsGatingRescue, ref ToolCandidate bestCandidate)
        {
            var equipment = pawn.equipment?.AllEquipmentListForReading;
            if (equipment == null)
                return;

            _candidateBuffer.Clear();
            for (int i = 0; i < equipment.Count; i++)
            {
                var thing = equipment[i];
                if (thing != null && IsValidTool(thing, workStat))
                    _candidateBuffer.Add(thing);
            }

            EvaluateCandidates(_candidateBuffer, pawn, workStat, currentScore, minGainPct, needsGatingRescue,
                               ToolLocation.Equipment, 0, ref bestCandidate);
        }

        private static void SearchSameCell(Pawn pawn, StatDef workStat, float currentScore, float minGainPct, bool needsGatingRescue, ref ToolCandidate bestCandidate)
        {
            var cell = pawn.Position;
            if (!cell.IsValid || pawn.Map == null)
                return;

            var thingsAtCell = pawn.Map.thingGrid.ThingsAt(cell);
            if (thingsAtCell == null)
                return;

            _candidateBuffer.Clear();
            foreach (var thing in thingsAtCell)
            {
                if (thing != null && thing != pawn && IsValidTool(thing, workStat))
                    _candidateBuffer.Add(thing);
            }

            EvaluateCandidates(_candidateBuffer, pawn, workStat, currentScore, minGainPct, needsGatingRescue,
                               ToolLocation.SameCell, 1, ref bestCandidate);
        }

        private static void SearchStockpiles(Pawn pawn, StatDef workStat, float currentScore, float minGainPct, bool needsGatingRescue, float radius, int pathCostBudget, ref ToolCandidate bestCandidate)
        {
            if (pawn.Map?.zoneManager?.AllZones == null)
                return;

            _stockpileBuffer.Clear();
            var zones = pawn.Map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                var zone = zones[i] as Zone_Stockpile;
                if (zone?.AllContainedThings == null)
                    continue;

                foreach (var thing in zone.AllContainedThings)
                {
                    if (thing != null && IsValidTool(thing, workStat) &&
                        IsWithinRadius(pawn.Position, thing.Position, radius))
                    {
                        _stockpileBuffer.Add(thing);
                    }
                }
            }

            EvaluateCandidatesWithPathCost(_stockpileBuffer, pawn, workStat, currentScore, minGainPct, needsGatingRescue,
                                          ToolLocation.Stockpile, pathCostBudget, ref bestCandidate);
        }

        private static void SearchHomeArea(Pawn pawn, StatDef workStat, float currentScore, float minGainPct, bool needsGatingRescue, float radius, int pathCostBudget, ref ToolCandidate bestCandidate)
        {
            if (pawn.Map?.areaManager?.Home == null)
                return;

            var homeArea = pawn.Map.areaManager.Home;
            var listerThings = pawn.Map.listerThings;

            // Search tools in home area
            var toolDefs = GetRelevantToolDefs(workStat);
            _candidateBuffer.Clear();

            for (int i = 0; i < toolDefs.Count; i++)
            {
                var toolDef = toolDefs[i];
                var thingsOfDef = listerThings.ThingsOfDef(toolDef);

                for (int j = 0; j < thingsOfDef.Count; j++)
                {
                    var thing = thingsOfDef[j];
                    if (thing != null && homeArea[thing.Position] &&
                        IsWithinRadius(pawn.Position, thing.Position, radius) &&
                        IsValidTool(thing, workStat))
                    {
                        _candidateBuffer.Add(thing);
                    }
                }
            }

            EvaluateCandidatesWithPathCost(_candidateBuffer, pawn, workStat, currentScore, minGainPct, needsGatingRescue,
                                          ToolLocation.HomeArea, pathCostBudget, ref bestCandidate);
        }

        private static void SearchNearby(Pawn pawn, StatDef workStat, float currentScore, float minGainPct, bool needsGatingRescue, float radius, int pathCostBudget, ref ToolCandidate bestCandidate)
        {
            if (pawn.Map?.listerThings == null)
                return;

            var listerThings = pawn.Map.listerThings;
            var toolDefs = GetRelevantToolDefs(workStat);

            _candidateBuffer.Clear();
            for (int i = 0; i < toolDefs.Count; i++)
            {
                var toolDef = toolDefs[i];
                var thingsOfDef = listerThings.ThingsOfDef(toolDef);

                for (int j = 0; j < thingsOfDef.Count; j++)
                {
                    var thing = thingsOfDef[j];
                    if (thing != null && IsWithinRadius(pawn.Position, thing.Position, radius) &&
                        IsValidTool(thing, workStat))
                    {
                        _candidateBuffer.Add(thing);
                    }
                }
            }

            EvaluateCandidatesWithPathCost(_candidateBuffer, pawn, workStat, currentScore, minGainPct, needsGatingRescue,
                                          ToolLocation.Nearby, pathCostBudget, ref bestCandidate);
        }

        private static void EvaluateCandidates(List<Thing> candidates, Pawn pawn, StatDef workStat, float currentScore,
                                             float minGainPct, bool needsGatingRescue, ToolLocation location, int pathCost,
                                             ref ToolCandidate bestCandidate)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                var tool = candidates[i];
                if (!CanPawnReserveAndReach(pawn, tool))
                    continue;

                var score = ToolScoring.Score(tool, pawn, workStat);
                var gainPct = (score - currentScore) / Math.Max(currentScore, 0.001f); if (ShouldConsiderTool(score, currentScore, gainPct, minGainPct, needsGatingRescue))
                {
                    if (bestCandidate.tool == null || score > bestCandidate.score ||
                        (Math.Abs(score - bestCandidate.score) < 0.001f && location < bestCandidate.location))
                    {
                        bestCandidate = new ToolCandidate
                        {
                            tool = tool,
                            score = score,
                            gainPct = gainPct,
                            pathCost = pathCost,
                            location = location
                        };
                    }
                }
            }
        }

        private static void EvaluateCandidatesWithPathCost(List<Thing> candidates, Pawn pawn, StatDef workStat, float currentScore,
                                                          float minGainPct, bool needsGatingRescue, ToolLocation location, int pathCostBudget,
                                                          ref ToolCandidate bestCandidate)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                var tool = candidates[i];
                if (!CanPawnReserveAndReach(pawn, tool))
                    continue;

                int pathCost = GetPathCost(pawn, tool);
                if (pathCost > pathCostBudget)
                    continue;

                var score = ToolScoring.Score(tool, pawn, workStat);
                var gainPct = (score - currentScore) / Math.Max(currentScore, 0.001f); if (ShouldConsiderTool(score, currentScore, gainPct, minGainPct, needsGatingRescue))
                {
                    if (bestCandidate.tool == null || score > bestCandidate.score ||
                        (Math.Abs(score - bestCandidate.score) < 0.001f && pathCost < bestCandidate.pathCost))
                    {
                        bestCandidate = new ToolCandidate
                        {
                            tool = tool,
                            score = score,
                            gainPct = gainPct,
                            pathCost = pathCost,
                            location = location
                        };
                    }
                }
            }
        }

        private static bool ShouldConsiderTool(float toolScore, float currentScore, float gainPct, float minGainPct, bool needsGatingRescue)
        {
            if (needsGatingRescue)
            {
                // For gating rescue, any improvement is good
                return toolScore > currentScore + GatingEpsilon;
            }

            // Normal case: require minimum gain percentage
            return gainPct >= minGainPct;
        }

        private static bool QueueAcquisitionJob(Pawn pawn, ToolCandidate candidate)
        {
            if (candidate.tool == null)
            {
                Log.Message($"[SurvivalTools.Assignment] QueueAcquisitionJob: candidate.tool is null for {pawn?.LabelShort}");
                return false;
            }

            Log.Message($"[SurvivalTools.Assignment] QueueAcquisitionJob: attempting to queue job for {pawn.LabelShort} to acquire {candidate.tool.LabelShort} from {candidate.location}");

            // Check carry limits before acquiring
            if (!CanCarryAdditionalTool(pawn))
            {
                Log.Message($"[SurvivalTools.Assignment] Carry limit reached for {pawn.LabelShort}, trying to drop worst tool");
                // Try to drop worst tool first
                if (!TryDropWorstTool(pawn))
                {
                    Log.Message($"[SurvivalTools.Assignment] Failed to drop worst tool for {pawn.LabelShort}");
                    return false;
                }
                Log.Message($"[SurvivalTools.Assignment] Successfully dropped worst tool for {pawn.LabelShort}");
            }

            Job job = null;

            switch (candidate.location)
            {
                case ToolLocation.Inventory:
                    // Already in inventory, just equip
                    job = JobMaker.MakeJob(JobDefOf.Equip, candidate.tool);
                    Log.Message($"[SurvivalTools.Assignment] Created Equip job for {pawn.LabelShort} (tool in inventory)");
                    break;

                case ToolLocation.Equipment:
                case ToolLocation.SameCell:
                    // Pick up and equip
                    job = JobMaker.MakeJob(JobDefOf.Equip, candidate.tool);
                    Log.Message($"[SurvivalTools.Assignment] Created Equip job for {pawn.LabelShort} (tool at {candidate.location})");
                    break;

                default:
                    // Haul to inventory first
                    job = JobMaker.MakeJob(JobDefOf.TakeInventory, candidate.tool);
                    job.count = 1;
                    Log.Message($"[SurvivalTools.Assignment] Created TakeInventory job for {pawn.LabelShort} (tool at {candidate.location})");
                    break;
            }

            if (job == null)
            {
                Log.Message($"[SurvivalTools.Assignment] Failed to create job for {pawn.LabelShort}");
                return false;
            }

            // Try to reserve before queueing
            if (!pawn.Reserve(candidate.tool, job, 1, -1, null, true))
            {
                Log.Message($"[SurvivalTools.Assignment] Failed to reserve {candidate.tool.LabelShort} for {pawn.LabelShort}");
                return false;
            }

            try
            {
                pawn.jobs?.jobQueue?.EnqueueFirst(job, JobTag.Misc);
                Log.Message($"[SurvivalTools.Assignment] Successfully enqueued {job.def.defName} job for {pawn.LabelShort} targeting {candidate.tool.LabelShort}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools.Assignment] Exception enqueueing job for {pawn.LabelShort}: {ex}");
                return false;
            }
        }

        private static bool CanCarryAdditionalTool(Pawn pawn)
        {
            var settings = SurvivalTools.Settings;
            if (settings == null)
                return true;

            int carryLimit = GetCarryLimit(settings);
            if (HasToolbelt(pawn))
                carryLimit = Math.Max(carryLimit, 3); // Toolbelt exception

            int currentTools = CountCarriedTools(pawn);
            return currentTools < carryLimit;
        }

        private static int GetCarryLimit(SurvivalToolsSettings settings)
        {
            if (settings.extraHardcoreMode)
                return 1; // Nightmare
            if (settings.hardcoreMode)
                return 2; // Hardcore
            return 3; // Normal
        }

        private static bool HasToolbelt(Pawn pawn)
        {
            // Stub: returns false unless specific apparel tag/comp detected
            // TODO: Implement toolbelt detection when available
            return false;
        }

        private static int CountCarriedTools(Pawn pawn)
        {
            int count = 0;

            // Count inventory tools
            var inventory = pawn.inventory?.innerContainer;
            if (inventory != null)
            {
                for (int i = 0; i < inventory.Count; i++)
                {
                    var thing = inventory[i];
                    if (thing != null && IsSurvivalTool(thing))
                        count++;
                }
            }

            // Count equipped tools
            var equipment = pawn.equipment?.AllEquipmentListForReading;
            if (equipment != null)
            {
                for (int i = 0; i < equipment.Count; i++)
                {
                    var thing = equipment[i];
                    if (thing != null && IsSurvivalTool(thing))
                        count++;
                }
            }

            return count;
        }

        private static bool TryDropWorstTool(Pawn pawn)
        {
            Thing worstTool = FindWorstCarriedTool(pawn);
            if (worstTool == null)
                return false;

            // Find nearest stockpile that accepts this tool
            var stockpile = FindNearestStockpileFor(pawn, worstTool);
            if (stockpile != null)
            {
                // Create a haul to cell job within the stockpile area
                var targetCell = stockpile.Cells.FirstOrDefault();
                if (targetCell.IsValid)
                {
                    var job = JobMaker.MakeJob(JobDefOf.HaulToCell, worstTool, targetCell);
                    job.count = 1;
                    pawn.jobs?.jobQueue?.EnqueueFirst(job, JobTag.Misc);
                    return true;
                }
            }

            // Fallback: drop on ground
            var dropJob = JobMaker.MakeJob(JobDefOf.DropEquipment, worstTool);
            pawn.jobs?.jobQueue?.EnqueueFirst(dropJob, JobTag.Misc);
            return true;
        }

        private static Thing FindWorstCarriedTool(Pawn pawn)
        {
            Thing worstTool = null;
            float worstScore = float.MaxValue;

            // Check inventory
            var inventory = pawn.inventory?.innerContainer;
            if (inventory != null)
            {
                for (int i = 0; i < inventory.Count; i++)
                {
                    var thing = inventory[i];
                    if (thing != null && IsSurvivalTool(thing))
                    {
                        float score = GetOverallToolScore(pawn, thing);
                        if (score < worstScore)
                        {
                            worstScore = score;
                            worstTool = thing;
                        }
                    }
                }
            }

            return worstTool;
        }

        private static Zone_Stockpile FindNearestStockpileFor(Pawn pawn, Thing thing)
        {
            if (pawn.Map?.zoneManager?.AllZones == null)
                return null;

            Zone_Stockpile nearest = null;
            float nearestDist = float.MaxValue;

            var zones = pawn.Map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                var stockpile = zones[i] as Zone_Stockpile;
                if (stockpile?.settings?.AllowedToAccept(thing) == true)
                {
                    float dist = pawn.Position.DistanceTo(stockpile.Cells.FirstOrDefault());
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = stockpile;
                    }
                }
            }

            return nearest;
        }

        private static bool IsValidTool(Thing thing, StatDef workStat)
        {
            if (thing == null || workStat == null)
                return false;

            if (!IsSurvivalTool(thing))
                return false;

            // Check if tool provides the work stat
            var factor = SurvivalToolUtility.GetToolProvidedFactor(thing as SurvivalTool, workStat);
            return factor > SurvivalToolUtility.GetNoToolBaseline(workStat) + GatingEpsilon;
        }

        private static bool IsSurvivalTool(Thing thing)
        {
            return thing is SurvivalTool ||
                   (thing?.def?.IsSurvivalTool() == true) ||
                   (thing?.def?.IsToolStuff() == true);
        }

        private static bool CanPawnReserveAndReach(Pawn pawn, Thing thing)
        {
            if (thing == null || pawn == null)
                return false;

            if (!pawn.CanReserveAndReach(thing, PathEndMode.Touch, Danger.None))
                return false;

            return true;
        }

        private static bool IsWithinRadius(IntVec3 center, IntVec3 target, float radius)
        {
            return center.DistanceTo(target) <= radius;
        }

        private static int GetPathCost(Pawn pawn, Thing thing)
        {
            if (pawn?.Map == null || thing == null)
                return int.MaxValue;

            // Simple approximation: distance * average movement cost
            float distance = pawn.Position.DistanceTo(thing.Position);

            // Estimate movement cost - most terrain is walkable at moderate cost
            int estimatedMoveCost = 13; // Default movement cost for most terrain

            return (int)(distance * estimatedMoveCost);
        }
        private static float GetOverallToolScore(Pawn pawn, Thing tool)
        {
            // Simple heuristic: average score across common work stats
            float totalScore = 0f;
            int statCount = 0;

            var commonStats = new[]
            {
                ST_StatDefOf.DiggingSpeed,
                StatDefOf.ConstructionSpeed,
                ST_StatDefOf.TreeFellingSpeed,
                ST_StatDefOf.PlantHarvestingSpeed
            };

            for (int i = 0; i < commonStats.Length; i++)
            {
                var stat = commonStats[i];
                if (stat != null && IsValidTool(tool, stat))
                {
                    totalScore += ToolScoring.Score(tool, pawn, stat);
                    statCount++;
                }
            }

            return statCount > 0 ? totalScore / statCount : 0f;
        }

        private static List<ThingDef> GetRelevantToolDefs(StatDef workStat)
        {
            // Return tool defs that could provide this work stat
            var result = new List<ThingDef>();

            foreach (var toolDef in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (toolDef?.IsSurvivalTool() == true || toolDef?.IsToolStuff() == true)
                {
                    // Quick check if this tool type could provide the stat
                    var dummyTool = new SurvivalTool();
                    dummyTool.def = toolDef;

                    var factor = SurvivalToolUtility.GetToolProvidedFactor(dummyTool, workStat);
                    if (factor > SurvivalToolUtility.GetNoToolBaseline(workStat) + GatingEpsilon)
                    {
                        result.Add(toolDef);
                    }
                }
            }

            return result;
        }
    }
}
