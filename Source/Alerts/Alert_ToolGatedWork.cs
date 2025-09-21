// RimWorld 1.6 / C# 7.3
// Source/Alerts/Alert_ToolGatedWork.cs
//
// Lightweight, non-spam alert for tool gating feedback
// - Updates every 600 ticks, only in Hardcore/Nightmare with alert enabled
// - Shows pawns blocked from work due to missing tools
// - Throttles per-pawn repetition (3000 ticks)
// - Allocation-free hot loops, pooled lists
// - Uses exact JobGate.ShouldBlock logic for accuracy

using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using SurvivalTools.Gating;

namespace SurvivalTools
{
    public sealed class Alert_ToolGatedWork : Alert
    {
        private List<PawnGatingIssue> _gatedPawns;
        private int _lastUpdateTick = -9999;
        private readonly Dictionary<Pawn, int> _lastShownTick = new Dictionary<Pawn, int>();

        private const int UpdateIntervalTicks = 600; // Update rarely
        private const int PawnThrottleTicks = 3000; // Don't repeat same pawn too often
        private const int MaxPawnsToShow = 10; // Keep explanation short

        // Representative WorkGiverDefs for checking (avoid scanning all)
        private static WorkGiverDef _miningWG;
        private static WorkGiverDef _constructWG;
        private static WorkGiverDef _plantCutWG;
        private static WorkGiverDef _plantHarvestWG;
        private static WorkGiverDef _smithingWG;

        static Alert_ToolGatedWork()
        {
            // Cache representative WorkGivers for performance
            _miningWG = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Mine");
            _constructWG = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToBlueprints")
                          ?? DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructFinishFrames");
            _plantCutWG = DefDatabase<WorkGiverDef>.GetNamedSilentFail("CutPlants")
                         ?? DefDatabase<WorkGiverDef>.GetNamedSilentFail("PlantsCut");
            _plantHarvestWG = DefDatabase<WorkGiverDef>.GetNamedSilentFail("PlantHarvest")
                             ?? DefDatabase<WorkGiverDef>.GetNamedSilentFail("GrowerHarvest");
            _smithingWG = DefDatabase<WorkGiverDef>.GetNamedSilentFail("SmithWeapons")
                         ?? DefDatabase<WorkGiverDef>.GetNamedSilentFail("Smith");
        }

        public override AlertPriority Priority => AlertPriority.Medium;

        public override AlertReport GetReport()
        {
            var settings = SurvivalTools.Settings;
            if (settings == null) return AlertReport.Inactive;

            // Only show in Hardcore/Nightmare with alert enabled
            if (!settings.hardcoreMode && !settings.extraHardcoreMode) return AlertReport.Inactive;
            if (!settings.showGatingAlert) return AlertReport.Inactive;

            // Update gated pawns list periodically
            int now = Find.TickManager?.TicksGame ?? 0;
            if (_gatedPawns == null || now - _lastUpdateTick >= UpdateIntervalTicks)
            {
                _lastUpdateTick = now;
                UpdateGatedPawns(now);
            }

            return (_gatedPawns != null && _gatedPawns.Count > 0)
                ? AlertReport.Active
                : AlertReport.Inactive;
        }

        public override string GetLabel()
        {
            return "ST_Alert_ToolGating_Label".Translate();
        }

        public override TaggedString GetExplanation()
        {
            if (_gatedPawns == null || _gatedPawns.Count == 0)
                return TaggedString.Empty;

            var sb = new StringBuilder(256);

            int shown = 0;
            for (int i = 0; i < _gatedPawns.Count && shown < MaxPawnsToShow; i++)
            {
                var issue = _gatedPawns[i];
                if (issue.pawn != null)
                {
                    sb.AppendLine($"{issue.pawn.LabelShort}: {issue.workType} ({issue.reason})");
                    shown++;
                }
            }

            if (_gatedPawns.Count > MaxPawnsToShow)
            {
                int remaining = _gatedPawns.Count - MaxPawnsToShow;
                sb.AppendLine($"… {"ST_Alert_ToolGating_AndMore".Translate(remaining)}");
            }

            return sb.ToString().TrimEnd();
        }

        private void UpdateGatedPawns(int currentTick)
        {
            if (_gatedPawns == null)
                _gatedPawns = new List<PawnGatingIssue>();
            else
                _gatedPawns.Clear();

            var map = Find.CurrentMap;
            if (map == null) return;

            var colonists = map.mapPawns?.FreeColonistsSpawned;
            if (colonists == null) return;

            // Check colonists for tool gating issues (no LINQ in hot loop)
            for (int i = 0; i < colonists.Count; i++)
            {
                var pawn = colonists[i];
                if (pawn == null || pawn.Dead || pawn.Downed || !pawn.Awake()) continue;
                if (pawn.RaceProps == null || !pawn.RaceProps.Humanlike) continue;
                if (pawn.guest != null && !pawn.IsColonist) continue;

                // Throttle per-pawn repetition
                if (_lastShownTick.TryGetValue(pawn, out int lastShown))
                {
                    if (currentTick - lastShown < PawnThrottleTicks)
                        continue;
                }

                // Check work types with available work
                CheckPawnForGatedWork(pawn, map, currentTick);
            }
        }

        private void CheckPawnForGatedWork(Pawn pawn, Map map, int currentTick)
        {
            // Check Mining (if any mine designations exist)
            if (_miningWG != null && pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.Mining) > 0)
            {
                if (HasMiningWork(map) && IsBlockedForWork(pawn, _miningWG, "ST_Alert_WorkType_Mining".Translate()))
                {
                    _lastShownTick[pawn] = currentTick;
                    return; // One issue per pawn per update
                }
            }

            // Check Construction (if any blueprints/frames exist)
            if (_constructWG != null && pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.Construction) > 0)
            {
                if (HasConstructionWork(map) && IsBlockedForWork(pawn, _constructWG, "ST_Alert_WorkType_Construction".Translate()))
                {
                    _lastShownTick[pawn] = currentTick;
                    return;
                }
            }

            // Check Plant Cutting (if any cut designations exist)
            if (_plantCutWG != null && pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.PlantCutting) > 0)
            {
                if (HasPlantCuttingWork(map) && IsBlockedForWork(pawn, _plantCutWG, "ST_Alert_WorkType_PlantCutting".Translate()))
                {
                    _lastShownTick[pawn] = currentTick;
                    return;
                }
            }

            // Check Plant Harvesting (if any harvest designations exist)
            if (_plantHarvestWG != null && pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.PlantCutting) > 0)
            {
                if (HasPlantHarvestWork(map) && IsBlockedForWork(pawn, _plantHarvestWG, "ST_Alert_WorkType_PlantHarvest".Translate()))
                {
                    _lastShownTick[pawn] = currentTick;
                    return;
                }
            }

            // Check Smithing (if enabled and we gate smithing work)
            if (_smithingWG != null && pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.Smithing) > 0)
            {
                if (IsBlockedForWork(pawn, _smithingWG, "ST_Alert_WorkType_Smithing".Translate()))
                {
                    _lastShownTick[pawn] = currentTick;
                    return;
                }
            }
        }

        private bool IsBlockedForWork(Pawn pawn, WorkGiverDef workGiver, string workTypeLabel)
        {
            if (workGiver == null) return false;

            // Use exact JobGate.ShouldBlock logic
            if (JobGate.ShouldBlock(pawn, workGiver, null, false, out var key, out var a1, out var a2))
            {
                string reason = key.Translate(a1, a2);
                _gatedPawns.Add(new PawnGatingIssue
                {
                    pawn = pawn,
                    workType = workTypeLabel,
                    reason = reason
                });
                return true;
            }
            return false;
        }

        private static bool HasMiningWork(Map map)
        {
            var designations = map.designationManager?.AllDesignations;
            if (designations == null) return false;

            // Check for mine designations (no LINQ)
            foreach (var des in designations)
            {
                if (des != null && des.def == DesignationDefOf.Mine)
                    return true;
            }
            return false;
        }

        private static bool HasConstructionWork(Map map)
        {
            // Check for construction frames or blueprints
            var frames = map.listerThings?.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            if (frames != null && frames.Count > 0) return true;

            var blueprints = map.listerThings?.ThingsInGroup(ThingRequestGroup.Blueprint);
            if (blueprints != null && blueprints.Count > 0) return true;

            return false;
        }

        private static bool HasPlantCuttingWork(Map map)
        {
            var designations = map.designationManager?.AllDesignations;
            if (designations == null) return false;

            // Check for plant cut designations (no LINQ)
            foreach (var des in designations)
            {
                if (des != null && des.def == DesignationDefOf.CutPlant)
                    return true;
            }
            return false;
        }

        private static bool HasPlantHarvestWork(Map map)
        {
            var designations = map.designationManager?.AllDesignations;
            if (designations == null) return false;

            // Check for plant harvest designations (no LINQ)
            foreach (var des in designations)
            {
                if (des != null && des.def == DesignationDefOf.HarvestPlant)
                    return true;
            }
            return false;
        }

        private struct PawnGatingIssue
        {
            public Pawn pawn;
            public string workType;
            public string reason;
        }
    }
}