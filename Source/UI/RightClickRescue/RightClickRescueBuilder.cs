// RimWorld 1.6 / C# 7.3
// Source/UI/RightClickRescue/RightClickRescueBuilder.cs
// Helper that scans the clicked cell for supported prioritized jobs (initially Mining) that are
// currently blocked by Survival Tools gating and offers an enabled rescue float menu option that
// queues a tool equip/pickup followed by the player-forced job, preserving Nightmare carry invariant.

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using SurvivalTools.Assign;
using SurvivalTools.Gating;
using SurvivalTools.Helpers;

namespace SurvivalTools.UI.RightClickRescue
{
    internal static class RightClickRescueBuilder
    {
        private static readonly List<IRescueTargetScanner> Scanners = new List<IRescueTargetScanner>
        {
            // Mining / construction
            new MineScanner(),
            new DeconstructScanner(),
            new SmoothWallScanner(),
            new SmoothFloorScanner(),
            new FinishFrameScanner(),

            // Plants / growing
            new CutPlantScanner(),
            new HarvestPlantScanner(),
            new SowScanner(),

            // Repair / clean
            new RepairScanner(),
            new CleanScanner(),
        };

        private static int _lastLoggedTick = -1;
        internal static void TryAddRescueOptions(Pawn pawn, FloatMenuContext ctx, List<FloatMenuOption> options)
        {
            if (pawn == null || ctx == null || options == null) return;
            var s = SurvivalToolsMod.Settings;
            if (s == null) return;
            if (!s.enableRightClickRescue) return; // feature gate
            if (!(s.hardcoreMode || s.extraHardcoreMode)) return; // Hardcore / Nightmare only per design
            foreach (var scanner in Scanners)
            {
                try
                {
                    if (!scanner.CanHandle(ctx)) continue;
                    if (!scanner.TryDescribeTarget(pawn, ctx, out var desc)) continue;
                    if (desc.RequiredStats == null || desc.RequiredStats.Count == 0) continue;

                    // Ask JobGate (forced=false). If not blocked, vanilla already supplied enabled option.
                    if (!JobGate.ShouldBlock(pawn, desc.WorkGiverDef, desc.JobDef, false, out var reasonKey, out var a1, out var a2))
                    {
                        if (Prefs.DevMode && s.debugLogging) Log.Message($"[ST.RightClick] Scanner {scanner.GetType().Name} target not gated – skipping.");
                        continue;
                    }
                    var firstRequired = desc.RequiredStats[0];
                    if (firstRequired == null) continue;

                    // Preview: can we upgrade? (lightweight). If none found, still offer generic rescue to allow manual pickup path.
                    string toolName;
                    bool canUpgrade = AssignmentSearchPreview.CanUpgradePreview(pawn, firstRequired, out toolName);
                    if (!canUpgrade)
                    {
                        var suffix = "ST_GenericToolSuffix".Translate().ToStringSafe() ?? "tool";
                        toolName = (firstRequired.label ?? "tool").CapitalizeFirst() + " " + suffix;
                        if (Prefs.DevMode && s.debugLogging) Log.Message($"[ST.RightClick] No upgrade candidate for {firstRequired.defName}; using generic label.");
                    }

                    string label = BuildOptionLabel(desc.PriorityLabel, toolName);
                    var capture = desc; // avoid modified closure
                    var reqStat = firstRequired;

                    Action act = () => ExecuteRescue(pawn, capture, reqStat);
                    var opt = new FloatMenuOption(label, act)
                    {
                        iconThing = capture.IconThing,
                        autoTakeable = false
                    };
                    options.Add(FloatMenuUtility.DecoratePrioritizedTask(opt, pawn, new LocalTargetInfo(desc.ClickCell)));
                    if (Prefs.DevMode && (_lastLoggedTick != GenTicks.TicksGame))
                    {
                        _lastLoggedTick = GenTicks.TicksGame;
                        Log.Message($"[ST.RightClick] Added rescue option (stat {firstRequired.defName}) at {ctx.ClickedCell}.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[SurvivalTools.RightClickRescue] Scanner exception: " + ex);
                }
            }
        }

        private static string BuildOptionLabel(string priorityLabel, string toolName)
        {
            try
            {
                return $"{priorityLabel} (" + "ST_WillFetchTool".Translate(toolName) + ")";
            }
            catch { return priorityLabel + " (will fetch " + toolName + ")"; }
        }

        private static void ExecuteRescue(Pawn pawn, RescueTarget capture, StatDef stat)
        {
            var settings = SurvivalToolsMod.Settings; if (pawn == null || capture.JobDef == null || settings == null) return;

            // 1) Queue upgrade for stat (reuse assignment system). We may defer starting the forced job
            // until after any queued upgrade + drop jobs to prevent the player order from being lost when
            // drop jobs pre-empt and the gated job immediately fails for lack of tools.
            bool upgradeQueued = AssignmentSearch.TryUpgradeFor(
                pawn,
                stat,
                settings.assignMinGainPct,
                settings.assignSearchRadius,
                settings.assignPathCostBudget,
                Assign.AssignmentSearch.QueuePriority.Front,
                "RightClickRescue");
            if (!upgradeQueued && Prefs.DevMode && SurvivalToolsMod.Settings.debugLogging)
            {
                Log.Message($"[ST.RightClick] No upgrade queued for {pawn.LabelShort} / {stat.defName}; may proceed with existing tools.");
            }

            // 2) Enforce Nightmare carry invariant. Capture how many drop jobs we enqueued so we can decide
            // whether to defer the forced job (so it runs AFTER drops + upgrade fetch).
            int dropJobs = 0;
            Thing keeper = null;
            if (settings.extraHardcoreMode)
            {
                int allowed = Assign.AssignmentSearch.GetEffectiveCarryLimit(pawn, settings);
                keeper = NightmareCarryEnforcer.SelectKeeperForJob(pawn, stat);
                dropJobs = NightmareCarryEnforcer.EnforceNow(pawn, keeper, allowed, "right-click-rescue");
                if (!NightmareCarryEnforcer.IsCompliant(pawn, keeper, allowed))
                {
                    Messages.Message("ST_DropExtraToolsFirst".Translate(), pawn, MessageTypeDefOf.RejectInput);
                    return;
                }
            }

            // 3) Build forced job. If we have any prerequisite queued actions (upgrade or drops), defer by
            // enqueuing the forced job at the tail so the sequence becomes: drops -> upgrade -> forced job.
            Job job = capture.MakeJob(pawn);
            if (job == null)
            {
                Messages.Message("ST_CouldNotCreateJob".Translate(), pawn, MessageTypeDefOf.RejectInput);
                return;
            }
            job.playerForced = true;
            var worker = capture.WorkGiverDef?.Worker; // may be null

            bool mustDefer = upgradeQueued || dropJobs > 0; // ensure prerequisites run first
            if (mustDefer)
            {
                // Enqueue at the end so earlier EnqueueFirst drop jobs and the upgrade job (queued Front) execute first.
                try
                {
                    pawn.jobs?.jobQueue?.EnqueueLast(job, JobTag.Misc);
                    if (Prefs.DevMode && SurvivalToolsMod.Settings.debugLogging)
                    {
                        Log.Message($"[ST.RightClick] Deferred forced job '{job.def.defName}' after {(dropJobs > 0 ? dropJobs + " drop(s)" : "no drops")} {(upgradeQueued ? "+ upgrade" : "")}. QueueCount={(pawn.jobs?.jobQueue?.Count ?? 0)}");
                    }
                }
                catch
                {
                    // Fallback: if enqueue fails, attempt immediate start (better than losing the command)
                    if (worker != null && pawn.jobs.TryTakeOrderedJobPrioritizedWork(job, worker, capture.ClickCell)) return;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }
                return;
            }

            // No prerequisites – start immediately so player gets instant feedback.
            if (worker != null && pawn.jobs.TryTakeOrderedJobPrioritizedWork(job, worker, capture.ClickCell)) return;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        // --- Contracts ---
        internal interface IRescueTargetScanner
        {
            bool CanHandle(FloatMenuContext ctx);
            bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RescueTarget desc);
        }

        internal struct RescueTarget
        {
            public IntVec3 ClickCell;
            public Thing IconThing;
            public WorkGiverDef WorkGiverDef;
            public JobDef JobDef;
            public List<StatDef> RequiredStats;
            public string PriorityLabel;
            public Func<Pawn, Job> MakeJob;
        }
    }

    // ---------------- Scanners ----------------
    // Each scanner mirrors the spec given in the user request, using WGResolve helpers.

    internal sealed class MineScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedCell.IsValid;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default;
            var cell = ctx.ClickedCell;
            var map = pawn?.Map;
            if (pawn == null || !cell.IsValid || map == null || !cell.InBounds(map)) return false;
            if (map.designationManager.DesignationAt(cell, DesignationDefOf.Mine) == null) return false;
            var wg = WGResolve.ByWorkerTypes("WorkGiver_Miner", "WorkGiver_Mine");
            var job = WGResolve.Of(() => JobDefOf.Mine) ?? WGResolve.Job("Mine");
            var label = "ST_Prioritize_Mine".Translate().ToStringSafe();
            desc = new RightClickRescueBuilder.RescueTarget
            {
                ClickCell = cell,
                IconThing = null,
                WorkGiverDef = wg,
                JobDef = job,
                RequiredStats = StatGatingHelper.GetStatsForWorkGiver(wg) ?? new List<StatDef> { ST_StatDefOf.DiggingSpeed },
                PriorityLabel = label,
                MakeJob = _ => JobMaker.MakeJob(job, cell)
            };
            return desc.WorkGiverDef != null && desc.JobDef != null;
        }
    }

    internal sealed class DeconstructScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedThings.Count > 0;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; if (pawn == null) return false;
            var map = pawn.Map;
            foreach (var t in ctx.ClickedThings)
            {
                if (map.designationManager.DesignationOn(t, DesignationDefOf.Deconstruct) == null) continue;
                var wg = WGResolve.ByWorkerTypes("WorkGiver_Deconstruct", "WorkGiver_ConstructDeconstruct");
                var job = WGResolve.Of(() => JobDefOf.Deconstruct) ?? WGResolve.Job("Deconstruct");
                desc = new RightClickRescueBuilder.RescueTarget
                {
                    ClickCell = t.Position,
                    IconThing = t,
                    WorkGiverDef = wg,
                    JobDef = job,
                    RequiredStats = StatGatingHelper.GetStatsForWorkGiver(wg) ?? new List<StatDef> { StatDefOf.ConstructionSpeed },
                    PriorityLabel = "ST_Prioritize_Deconstruct".Translate().ToStringSafe(),
                    MakeJob = _ => JobMaker.MakeJob(job, t)
                };
                return desc.WorkGiverDef != null && desc.JobDef != null;
            }
            return false;
        }
    }

    internal sealed class SmoothWallScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedCell.IsValid;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; var cell = ctx.ClickedCell; var map = pawn?.Map; if (pawn == null || map == null) return false;
            if (map.designationManager.DesignationAt(cell, DesignationDefOf.SmoothWall) == null) return false;
            var wg = WGResolve.ByWorkerTypes("WorkGiver_ConstructSmoothWall");
            var job = WGResolve.Job("SmoothWall");
            desc = new RightClickRescueBuilder.RescueTarget
            {
                ClickCell = cell,
                WorkGiverDef = wg,
                JobDef = job,
                IconThing = null,
                RequiredStats = StatGatingHelper.GetStatsForWorkGiver(wg) ?? new List<StatDef> { StatDefOf.ConstructionSpeed },
                PriorityLabel = "ST_Prioritize_SmoothWall".Translate().ToStringSafe(),
                MakeJob = _ => JobMaker.MakeJob(job, cell)
            };
            return desc.WorkGiverDef != null && desc.JobDef != null;
        }
    }

    internal sealed class SmoothFloorScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedCell.IsValid;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; var cell = ctx.ClickedCell; var map = pawn?.Map; if (pawn == null || map == null) return false;
            if (map.designationManager.DesignationAt(cell, DesignationDefOf.SmoothFloor) == null) return false;
            var wg = WGResolve.ByWorkerTypes("WorkGiver_ConstructSmoothFloor");
            var job = WGResolve.Job("SmoothFloor");
            desc = new RightClickRescueBuilder.RescueTarget
            {
                ClickCell = cell,
                WorkGiverDef = wg,
                JobDef = job,
                IconThing = null,
                RequiredStats = StatGatingHelper.GetStatsForWorkGiver(wg) ?? new List<StatDef> { StatDefOf.ConstructionSpeed },
                PriorityLabel = "ST_Prioritize_SmoothFloor".Translate().ToStringSafe(),
                MakeJob = _ => JobMaker.MakeJob(job, cell)
            };
            return desc.WorkGiverDef != null && desc.JobDef != null;
        }
    }

    internal sealed class FinishFrameScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedThings.Count > 0;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; if (pawn == null) return false;
            foreach (var t in ctx.ClickedThings)
            {
                if (t is Frame f)
                {
                    var wg = WGResolve.ByWorkerTypes("WorkGiver_ConstructFinishFrames");
                    var job = WGResolve.Job("FinishFrame");
                    desc = new RightClickRescueBuilder.RescueTarget
                    {
                        ClickCell = f.Position,
                        IconThing = f,
                        WorkGiverDef = wg,
                        JobDef = job,
                        RequiredStats = StatGatingHelper.GetStatsForWorkGiver(wg) ?? new List<StatDef> { StatDefOf.ConstructionSpeed },
                        PriorityLabel = "ST_Prioritize_FinishFrame".Translate().ToStringSafe(),
                        MakeJob = _ => JobMaker.MakeJob(job, f)
                    };
                    return desc.WorkGiverDef != null && desc.JobDef != null;
                }
            }
            return false;
        }
    }

    internal sealed class CutPlantScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedThings.Count > 0;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; if (pawn == null) return false; var map = pawn.Map;
            foreach (var t in ctx.ClickedThings)
            {
                var p = t as Plant;
                if (p == null) continue;
                if (map.designationManager.DesignationOn(p, DesignationDefOf.CutPlant) == null) continue;
                var wg = WGResolve.ByWorkerTypes("WorkGiver_PlantsCut", "WorkGiver_CutPlant");
                var job = WGResolve.Of(() => JobDefOf.CutPlant) ?? WGResolve.Job("CutPlant");
                desc = new RightClickRescueBuilder.RescueTarget
                {
                    ClickCell = p.Position,
                    IconThing = p,
                    WorkGiverDef = wg,
                    JobDef = job,
                    RequiredStats = StatGatingHelper.GetStatsForWorkGiver(wg) ?? new List<StatDef> { ST_StatDefOf.PlantHarvestingSpeed },
                    PriorityLabel = "ST_Prioritize_CutPlant".Translate().ToStringSafe(),
                    MakeJob = _ => JobMaker.MakeJob(job, p)
                };
                return desc.WorkGiverDef != null && desc.JobDef != null;
            }
            return false;
        }
    }

    internal sealed class HarvestPlantScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedThings.Count > 0;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; if (pawn == null) return false; var map = pawn.Map;
            foreach (var t in ctx.ClickedThings)
            {
                var p = t as Plant;
                if (p == null) continue;
                if (map.designationManager.DesignationOn(p, DesignationDefOf.HarvestPlant) == null) continue;
                var wg = WGResolve.ByWorkerTypes("WorkGiver_PlantsHarvest", "WorkGiver_Harvest");
                var job = WGResolve.Of(() => JobDefOf.Harvest) ?? WGResolve.Job("Harvest");
                var labelKey = p.def.plant?.IsTree ?? false ? "ChopWood" : "Harvest";
                var label = (labelKey == "ChopWood" ? "ST_Prioritize_ChopWood" : "ST_Prioritize_Harvest").Translate().ToStringSafe();
                desc = new RightClickRescueBuilder.RescueTarget
                {
                    ClickCell = p.Position,
                    IconThing = p,
                    WorkGiverDef = wg,
                    JobDef = job,
                    RequiredStats = StatGatingHelper.GetStatsForWorkGiver(wg) ?? new List<StatDef> { ST_StatDefOf.PlantHarvestingSpeed, ST_StatDefOf.TreeFellingSpeed },
                    PriorityLabel = label,
                    MakeJob = _ => JobMaker.MakeJob(job, p)
                };
                return desc.WorkGiverDef != null && desc.JobDef != null;
            }
            return false;
        }
    }

    internal sealed class SowScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedCell.IsValid;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; if (pawn == null) return false; var map = pawn.Map; var cell = ctx.ClickedCell; if (!cell.IsValid) return false;
            var zone = cell.GetZone(map) as Zone_Growing; if (zone == null) return false;
            var wg = WGResolve.ByWorkerTypes("WorkGiver_PlantsSow", "WorkGiver_Sow");
            var job = WGResolve.Of(() => JobDefOf.Sow) ?? WGResolve.Job("Sow");
            desc = new RightClickRescueBuilder.RescueTarget
            {
                ClickCell = cell,
                IconThing = null,
                WorkGiverDef = wg,
                JobDef = job,
                RequiredStats = StatGatingHelper.GetStatsForWorkGiver(wg) ?? new List<StatDef> { ST_StatDefOf.PlantHarvestingSpeed },
                PriorityLabel = "ST_Prioritize_Sow".Translate().ToStringSafe(),
                MakeJob = _ => JobMaker.MakeJob(job, cell)
            };
            return desc.WorkGiverDef != null && desc.JobDef != null;
        }
    }

    internal sealed class RepairScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedThings.Count > 0;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; if (pawn == null) return false;
            foreach (var t in ctx.ClickedThings)
            {
                var b = t as Building;
                if (b == null) continue;
                if (b.HitPoints >= b.MaxHitPoints) continue;
                var wg = WGResolve.ByWorkerTypes("WorkGiver_Repair");
                var job = WGResolve.Of(() => JobDefOf.Repair) ?? WGResolve.Job("Repair");
                desc = new RightClickRescueBuilder.RescueTarget
                {
                    ClickCell = b.Position,
                    IconThing = b,
                    WorkGiverDef = wg,
                    JobDef = job,
                    RequiredStats = StatGatingHelper.GetStatsForWorkGiver(wg) ?? new List<StatDef> { StatDefOf.ConstructionSpeed },
                    PriorityLabel = "ST_Prioritize_Repair".Translate().ToStringSafe(),
                    MakeJob = _ => JobMaker.MakeJob(job, b)
                };
                return desc.WorkGiverDef != null && desc.JobDef != null;
            }
            return false;
        }
    }

    internal sealed class CleanScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedCell.IsValid;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; if (pawn == null) return false; var cell = ctx.ClickedCell; var map = pawn.Map; if (!cell.IsValid) return false;
            Filth filth = null; var things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                var f = things[i] as Filth;
                if (f == null) continue;
                try
                {
                    if (f.MapHeld != null && !f.Destroyed && f.def?.filth != null)
                    {
                        // 1.6: filth.cleanable field may be internal; approximate via cleaning work giver logic: any filth with non-zero thickness and not forbidden.
                        if (f.thickness > 0 && !f.IsForbidden(pawn)) { filth = f; break; }
                    }
                }
                catch { /* ignore */ }
            }
            if (filth == null) return false;
            var wg = WGResolve.ByWorkerTypes("WorkGiver_Clean");
            var job = WGResolve.Of(() => JobDefOf.Clean) ?? WGResolve.Job("Clean");
            desc = new RightClickRescueBuilder.RescueTarget
            {
                ClickCell = cell,
                IconThing = filth,
                WorkGiverDef = wg,
                JobDef = job,
                RequiredStats = StatGatingHelper.GetStatsForWorkGiver(wg) ?? new List<StatDef> { StatDefOf.CleaningSpeed },
                PriorityLabel = "ST_Prioritize_Clean".Translate().ToStringSafe(),
                MakeJob = _ => JobMaker.MakeJob(job, filth)
            };
            return desc.WorkGiverDef != null && desc.JobDef != null;
        }
    }

    // Lightweight preview helper until a richer AssignmentSearch.CanUpgradePreview exists.
    internal static class AssignmentSearchPreview
    {
        public static bool CanUpgradePreview(Pawn pawn, StatDef focusStat, out string friendlyToolName)
        {
            friendlyToolName = focusStat?.label?.CapitalizeFirst() ?? "tool";
            var settings = SurvivalToolsMod.Settings; if (pawn == null || focusStat == null || settings == null) return false;
            if (!settings.enableAssignments) return false;

            // Simple probe: enumerate held + virtual + equipment and see if any candidate would improve baseline.
            float baseline = SurvivalToolUtility.GetNoToolBaseline(focusStat);
            float best = 0f;
            Thing bestTool = null;
            foreach (var thing in pawn.GetAllUsableSurvivalTools())
            {
                if (thing?.def == null) continue;
                float factor = ToolStatResolver.GetToolStatFactor(thing.def, thing.Stuff, focusStat);
                if (factor <= baseline + 0.001f) continue;
                float score = Scoring.ToolScoring.Score(thing, pawn, focusStat);
                if (score > best)
                {
                    best = score; bestTool = thing;
                }
            }
            if (bestTool != null)
            {
                friendlyToolName = bestTool.LabelShortCap;
                return true;
            }
            return false;
        }
    }
}
