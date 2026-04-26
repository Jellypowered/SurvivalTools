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
using HarmonyLib;
using RimWorld;
using Verse;
using SurvivalTools.Gating;
using SurvivalTools.Helpers;

namespace SurvivalTools
{
    public sealed class Alert_ToolGatedWork : Alert
    {
        private List<PawnGatingIssue> _gatedPawns;
        private int _lastUpdateTick = -9999;
        // Phase 8: stickiness map (pawn -> hideAfterTick) - keeps alert visible for minimum duration
        // to prevent rapid flickering, but content updates in real-time
        private static readonly Dictionary<int, int> _stickyUntil = new Dictionary<int, int>(64);

        private const int MaxPawnsToShow = 10; // Keep explanation short

        // Representative WorkGiverDefs for checking (avoid scanning all)
        private static WorkGiverDef _miningWG;
        private static WorkGiverDef _constructWG;
        private static WorkGiverDef _plantCutWG;
        private static WorkGiverDef _plantHarvestWG;
        private static WorkGiverDef _researchWG;
        private static WorkGiverDef _deconstructWG;
        // Medical and butchery are resolved via stat-family scan (no guaranteed defName)
        private static WorkGiverDef _medicalWG;
        private static WorkGiverDef _butcheryWG;

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
            // Registered via StaticConstructorClass
            _researchWG = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Research");
            _deconstructWG = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Deconstruct");
            // Scan for first WG that maps to the relevant stat (no universal vanilla defName)
            _medicalWG = FindRepresentativeWGForStat(ST_StatDefOf.MedicalOperationSpeed);
            _butcheryWG = FindRepresentativeWGForStat(ST_StatDefOf.ButcheryFleshSpeed);
        }

        /// <summary>Returns the first WorkGiverDef in the database that requires the given stat.</summary>
        private static WorkGiverDef FindRepresentativeWGForStat(StatDef stat)
        {
            if (stat == null) return null;
            foreach (var wgDef in DefDatabase<WorkGiverDef>.AllDefs)
            {
                if (wgDef == null) continue;
                var stats = StatGatingHelper.GetStatsForWorkGiver(wgDef);
                if (stats != null && stats.Contains(stat)) return wgDef;
            }
            return null;
        }

        public override AlertPriority Priority => AlertPriority.Medium;

        public override AlertReport GetReport()
        {
            var settings = SurvivalToolsMod.Settings;
            if (settings == null) return AlertReport.Inactive;

            // Only show in Hardcore/Nightmare with alert enabled
            if (!settings.hardcoreMode && !settings.extraHardcoreMode) return AlertReport.Inactive;
            if (!settings.showGatingAlert) return AlertReport.Inactive;

            // Always update on every check for real-time responsiveness
            int now = Find.TickManager?.TicksGame ?? 0;
            UpdateGatedPawns(now);
            _lastUpdateTick = now;

            // If any gated pawns exist right now -> active
            bool hasCurrentIssues = _gatedPawns != null && _gatedPawns.Count > 0;

            // Stickiness: keep alert active for minimum duration even if issues resolve
            // This prevents rapid flickering when pawns are actively picking up tools
            bool stillSticky = false;
            if (_stickyUntil.Count > 0)
            {
                // Prune expired entries & check if any are still active
                var toRemove = (List<int>)null;
                foreach (var kv in _stickyUntil)
                {
                    if (now < kv.Value)
                    {
                        stillSticky = true; // at least one pawn still in sticky window
                    }
                    else
                    {
                        if (toRemove == null) toRemove = new List<int>(4);
                        toRemove.Add(kv.Key);
                    }
                }
                if (toRemove != null)
                {
                    for (int i = 0; i < toRemove.Count; i++) _stickyUntil.Remove(toRemove[i]);
                }
            }

            // Active if there are current issues OR we're still in sticky window
            bool active = hasCurrentIssues || stillSticky;

            return active ? AlertReport.Active : AlertReport.Inactive;
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
                // Use unified eligibility contract (consistent with Alert_ColonistNeedsSurvivalTool and JobGate)
                if (!PawnToolValidator.CanUseSurvivalTools(pawn)) continue;
                if (pawn.Downed || !pawn.Awake()) continue;

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
                    RegisterSticky(pawn, currentTick);
                    return; // One issue per pawn per update
                }
            }

            // Check Construction (if any blueprints/frames exist)
            if (_constructWG != null && pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.Construction) > 0)
            {
                if (HasConstructionWork(map) && IsBlockedForWork(pawn, _constructWG, "ST_Alert_WorkType_Construction".Translate()))
                {
                    RegisterSticky(pawn, currentTick);
                    return;
                }
            }

            // Check Plant Cutting (if any cut designations exist)
            if (_plantCutWG != null && pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.PlantCutting) > 0)
            {
                if (HasPlantCuttingWork(map) && IsBlockedForWork(pawn, _plantCutWG, "ST_Alert_WorkType_PlantCutting".Translate()))
                {
                    RegisterSticky(pawn, currentTick);
                    return;
                }
            }

            // Check Plant Harvesting (if any harvest designations exist)
            if (_plantHarvestWG != null && pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.PlantCutting) > 0)
            {
                if (HasPlantHarvestWork(map) && IsBlockedForWork(pawn, _plantHarvestWG, "ST_Alert_WorkType_PlantHarvest".Translate()))
                {
                    RegisterSticky(pawn, currentTick);
                    return;
                }
            }

            // Check Research (if active research project)
            if (_researchWG != null && pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.Research) > 0)
            {
                if (HasResearchWork() && IsBlockedForWork(pawn, _researchWG, "ST_Alert_WorkType_Research".Translate()))
                {
                    RegisterSticky(pawn, currentTick);
                    return;
                }
            }

            // Check Deconstruction (if any deconstruct designations exist)
            if (_deconstructWG != null && pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.Construction) > 0)
            {
                if (HasDeconstructWork(map) && IsBlockedForWork(pawn, _deconstructWG, "ST_Alert_WorkType_Deconstruct".Translate()))
                {
                    RegisterSticky(pawn, currentTick);
                    return;
                }
            }

            // Check Medical (if any patients need tending)
            if (_medicalWG != null && _medicalWG.workType != null && pawn.workSettings != null && pawn.workSettings.GetPriority(_medicalWG.workType) > 0)
            {
                if (HasMedicalWork(map) && IsBlockedForWork(pawn, _medicalWG, "ST_Alert_WorkType_Medical".Translate()))
                {
                    RegisterSticky(pawn, currentTick);
                    return;
                }
            }

            // Check Butchery (if slaughter or butchery work is available)
            if (_butcheryWG != null && _butcheryWG.workType != null && pawn.workSettings != null && pawn.workSettings.GetPriority(_butcheryWG.workType) > 0)
            {
                if (HasButcheryWork(map) && IsBlockedForWork(pawn, _butcheryWG, "ST_Alert_WorkType_Butchery".Translate()))
                {
                    RegisterSticky(pawn, currentTick);
                    return;
                }
            }
        }

        private bool IsBlockedForWork(Pawn pawn, WorkGiverDef workGiver, string workTypeLabel)
        {
            if (workGiver == null) return false;

            // Use exact JobGate.ShouldBlock logic
            if (JobGate.ShouldBlock(pawn, workGiver, (JobDef)null, false, out var key, out var a1, out var a2, queryOnly: true))
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

        private static bool HasResearchWork()
        {
            var rm = Find.ResearchManager;
            if (rm == null) return false;
            try
            {
                // currentProj may be a non-public field or renamed property across versions
                var fi = AccessTools.Field(rm.GetType(), "currentProj");
                if (fi != null) return fi.GetValue(rm) != null;
                var pi = AccessTools.Property(rm.GetType(), "CurrentProj");
                if (pi != null) return pi.GetValue(rm, null) != null;
            }
            catch { }
            return false;
        }

        private static bool HasDeconstructWork(Map map)
        {
            var designations = map.designationManager?.AllDesignations;
            if (designations == null) return false;
            foreach (var des in designations)
            {
                if (des != null && des.def == DesignationDefOf.Deconstruct)
                    return true;
            }
            return false;
        }

        private static bool HasMedicalWork(Map map)
        {
            var pawns = map.mapPawns?.AllPawnsSpawned;
            if (pawns == null) return false;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p != null && p.Spawned && p.health?.HasHediffsNeedingTend() == true)
                    return true;
            }
            return false;
        }

        private static bool HasButcheryWork(Map map)
        {
            // Slaughter-designated animals
            var designations = map.designationManager?.AllDesignations;
            if (designations != null)
            {
                foreach (var des in designations)
                {
                    if (des != null && des.def == DesignationDefOf.Slaughter)
                        return true;
                }
            }
            // Animal corpses in storage (awaiting butcher bills)
            var corpses = map.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
            if (corpses != null)
            {
                for (int i = 0; i < corpses.Count; i++)
                {
                    var corpse = corpses[i] as Corpse;
                    if (corpse?.InnerPawn?.RaceProps?.Animal == true && corpse.IsInAnyStorage())
                        return true;
                }
            }
            return false;
        }

        private struct PawnGatingIssue
        {
            public Pawn pawn;
            public string workType;
            public string reason;
        }

        private static void RegisterSticky(Pawn pawn, int now)
        {
            var settings = SurvivalToolsMod.Settings;
            if (pawn == null || settings == null) return;
            int minTicks = settings.toolGateAlertMinTicks;
            if (minTicks <= 0) return; // disabled
            int hideAfter = now + minTicks;
            _stickyUntil[pawn.thingIDNumber] = hideAfter;
        }
    }
}