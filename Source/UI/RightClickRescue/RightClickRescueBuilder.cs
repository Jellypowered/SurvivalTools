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
using SurvivalTools.Compat;
using SurvivalTools.Compatibility.TreeStack;
using System.Diagnostics;

namespace SurvivalTools.UI.RightClickRescue
{
    internal static class RightClickRescueBuilder
    {
        // Per-pawn breadcrumb cooldown
        private static readonly Dictionary<int, int> _rcLogCd = new Dictionary<int, int>();
        private const int RC_LOG_CD_TICKS = 300; // ~5 seconds
        private static readonly HashSet<StatDef> _statsLoggedThisClick = new HashSet<StatDef>();
        internal static void ResetClickStatLog() { _statsLoggedThisClick.Clear(); }
        private static bool AllowRCLog(Pawn p)
        {
            try
            {
                if (p == null) return false;
                int now = Find.TickManager?.TicksGame ?? 0;
                int id = p.thingIDNumber;
                if (_rcLogCd.TryGetValue(id, out var until) && until > now) return false;
                _rcLogCd[id] = now + RC_LOG_CD_TICKS;
                return true;
            }
            catch { return false; }
        }
        private static readonly List<IRescueTargetScanner> Scanners = new List<IRescueTargetScanner>
        {
            // Mining / construction
            new MineScanner(),
            new DeconstructScanner(),
            new UninstallScanner(),
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

            // Research (bench)
            new ResearchScanner(),
        };

        private static int _lastConsideredCount;
        internal static int LastConsideredCount => _lastConsideredCount;
        // Instrumentation aggregates (DevMode only read)
        internal struct ScannerRecord { public string Name; public long Ms; public string Reason; public bool Described; }
        private static readonly List<ScannerRecord> _scannerRecords = new List<ScannerRecord>();
        internal static IReadOnlyList<ScannerRecord> ScannerRecords => _scannerRecords;
        private static int _scanCanHandleFalse, _scanDescribeFailed, _scanTreeSuppressed, _scanWorkTypeDisabled, _scanDescribed, _gatingNeeded, _gatingNotNeeded;
        internal static (int canHandleFalse, int describeFailed, int treeSuppressed, int workTypeDisabled, int described, int gatingNeeded, int gatingNotNeeded) Counters => (_scanCanHandleFalse, _scanDescribeFailed, _scanTreeSuppressed, _scanWorkTypeDisabled, _scanDescribed, _gatingNeeded, _gatingNotNeeded);
        internal static void ResetInstrumentation()
        {
            _scannerRecords.Clear();
            _scanCanHandleFalse = _scanDescribeFailed = _scanTreeSuppressed = _scanWorkTypeDisabled = _scanDescribed = _gatingNeeded = _gatingNotNeeded = 0;
            _lastConsideredCount = 0;
            ScannerDiagnostics.Clear();
        }

        // Static singleton for sow stats (avoid first-click StatGatingHelper cost ~280ms); per-phase logging removed (summary click only)
        private static readonly List<StatDef> _sowStatsFast = new List<StatDef> { ST_StatDefOf.SowingSpeed };
        // Prewarm entrypoint (called by provider) to shift cold reflection (WG + JobDef static init) off the first right-click.
        internal static void PrewarmSow()
        {
            try
            {
                // Direct named lookup first (cheap) then fallback to previous reflection path.
                if (SowScanner._wg == null)
                {
                    SowScanner._wg = DefDatabase<WorkGiverDef>.GetNamedSilentFail("GrowerSow")
                                       ?? DefDatabase<WorkGiverDef>.GetNamedSilentFail("Grower_Sow")
                                       ?? WGResolve.ByWorkerTypes("WorkGiver_GrowerSow", "WorkGiver_Grower_Sow", "WorkGiver_Grower", "WorkGiver_PlantsSow", "WorkGiver_Sow");
                }
                if (SowScanner._job == null)
                {
                    // Force JobDefOf static ctor early; fallback name lookup
                    try { var _ = JobDefOf.Sow; } catch { }
                    SowScanner._job = JobDefOf.Sow ?? WGResolve.Job("Sow");
                }
            }
            catch { }
        }

        // Prewarm / cache for research scanner (eliminate repeated reflection & AccessTools type lookups)
        internal static void PrewarmResearch()
        {
            try { ResearchScanner.EnsureInit(); ResearchScanner.Warm(); } catch { }
        }

        private static bool PawnCanEverDo(Pawn pawn, WorkGiverDef wg)
        {
            try
            {
                if (pawn == null || wg == null) return false;
                var wt = wg.workType; if (wt == null) return true; // no work type = assume allowed
                if (pawn.WorkTypeIsDisabled(wt)) return false; // incapable
                // Check if pawn is assigned to this work (has priority > 0)
                if (pawn.workSettings != null)
                {
                    int priority = pawn.workSettings.GetPriority(wt);
                    if (priority == 0) return false; // not assigned
                }
                return true;
            }
            catch { return true; }
        }

        internal static bool IsSowableNow(Zone_Growing zone, IntVec3 c, Map map)
        {
            // Conservative fast checks mirroring vanilla sow WorkGiver logic
            try
            {
                if (zone == null || map == null || !c.IsValid || !c.InBounds(map)) return false;
                var plantDef = zone.GetPlantDefToGrow();
                if (plantDef == null) return false;
                // Skip if another non‑mature plant already here (can't sow yet)
                var existingPlant = c.GetPlant(map);
                if (existingPlant != null && !existingPlant.Destroyed && existingPlant.def != plantDef)
                {
                    // If it's a different plant and not ready for harvest, can't sow
                    if (!existingPlant.HarvestableNow) return false;
                }
                // Growth season / snow / adjacency checks (guarded via helper to avoid API variance)
                if (!GrowthSeasonNowSafe(c, map)) return false;
                if (!SnowAllowsPlantingSafe(c, map)) return false;
                if (AdjacentSowBlockerSafe(plantDef, c, map) != null) return false;
                // Terrain fertility check
                var terrainFert = map.fertilityGrid.FertilityAt(c);
                if (terrainFert < plantDef.plant.fertilityMin) return false;
                // No blocking thing (rock, building, etc.)
                var things = c.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    var th = things[i]; if (th.def.category == ThingCategory.Building && th.def.passability == Traversability.Impassable) return false;
                }
                return true;
            }
            catch { return false; }
        }

        // --- Safe wrappers (handle potential method signature differences across versions) ---
        private static bool GrowthSeasonNowSafe(IntVec3 c, Map map)
        {
            try
            {
                if (map == null) return true;
                // Approximate vanilla: require outdoor temperature above 0C (or plant min growth temp if available) and not in permanent winter (simplified)
                float temp = map.mapTemperature?.OutdoorTemp ?? 10f;
                return temp > 0f; // permissive; prevents false negatives that hide sow option
            }
            catch { return true; }
        }
        private static bool SnowAllowsPlantingSafe(IntVec3 c, Map map)
        {
            try { return PlantUtility.SnowAllowsPlanting(c, map); } catch { return true; }
        }
        private static Thing AdjacentSowBlockerSafe(ThingDef plant, IntVec3 c, Map map)
        {
            try { return PlantUtility.AdjacentSowBlocker(plant, c, map); } catch { return null; }
        }

        // Track feedback reason for disabled options
        private struct FeedbackReason
        {
            public bool anyScannersRan;
            public bool allWorkTypesDisabled;
            public bool noToolsAvailable;
            public bool notBlocked; // job doesn't need rescue (has tool already)
        }
        private static FeedbackReason _feedbackThisClick;

        internal static void TryAddRescueOptions(Pawn pawn, FloatMenuContext ctx, List<FloatMenuOption> options)
        {
            if (pawn == null || ctx == null || options == null) return;
            if (!SurvivalTools.Helpers.PawnEligibility.IsEligibleColonistHuman(pawn)) return; // exclude colony mechs, animals, wilds from all logic & logging
            var s = SurvivalToolsMod.Settings;
            if (s == null) return;
            if (!s.enableRightClickRescue) return; // feature gate
            if (!(s.hardcoreMode || s.extraHardcoreMode)) return; // Hardcore / Nightmare only per design
            // Prevent duplicate calls (Provider + Harmony patch)
            if (Provider_STPrioritizeWithRescue.AlreadySatisfiedThisClick()) return;

            // Reset feedback tracking
            _feedbackThisClick = new FeedbackReason();
            bool stcExternal = TreeSystemArbiter.Authority == TreeAuthority.SeparateTreeChopping; // external tree authority means suppress tree felling rescue entirely
            // Determine primary thing (first clicked valid thing) for scoring context
            Thing primaryThing = null;
            try
            {
                foreach (var t in ctx.ClickedThings)
                {
                    if (t != null) { primaryThing = t; break; }
                }
            }
            catch { }

            var cell = ctx.ClickedCell;
            var map = pawn.Map;

            // Lightweight cell context for early pruning
            bool hasMineDesignation = false, hasSmoothDesignation = false, hasCutDesignation = false, hasHarvestDesignation = false;
            bool hasNonSowThing = false;
            Zone_Growing growZone = null;
            if (cell.IsValid && map != null)
            {
                try
                {
                    var desigs = map.designationManager?.AllDesignationsAt(cell);
                    if (desigs != null)
                    {
                        foreach (var d in desigs)
                        {
                            if (d == null) continue;
                            var def = d.def;
                            if (def == DesignationDefOf.Mine) hasMineDesignation = true;
                            else if (def == DesignationDefOf.SmoothFloor || def == DesignationDefOf.SmoothWall) hasSmoothDesignation = true;
                            else if (def == DesignationDefOf.CutPlant) hasCutDesignation = true;
                            else if (def == DesignationDefOf.HarvestPlant) hasHarvestDesignation = true;
                        }
                    }
                    growZone = cell.GetZone(map) as Zone_Growing;
                    // Classify clicked things for pruning
                    foreach (var t in ctx.ClickedThings)
                    {
                        if (t == null) continue;
                        // Things that imply other work types: frames, buildings needing deconstruct/repair, mineable rock, filth (cleaning), research bench, etc.
                        if (t is Frame || t is Building || (t.def?.mineable ?? false)) { hasNonSowThing = true; break; }
                        // Filth: cleaning scanner; if we see filth treat as non-sow context
                        if (t is Filth) { hasNonSowThing = true; break; }
                    }
                }
                catch { }
            }
            bool sowContext = growZone != null && !hasMineDesignation && !hasSmoothDesignation && !hasCutDesignation && !hasHarvestDesignation && !hasNonSowThing;

            // Pure sow fast path (skip generic scanner pipeline + sorting + second pass)
            if (sowContext)
            {
                var settingsFast = SurvivalToolsMod.Settings;
                // Timing only retained for aggregate scanner record (no per-phase logs anymore)
                Stopwatch swFast = null; bool timing = Prefs.DevMode && settingsFast != null && settingsFast.debugLogging;
                try
                {
                    if (timing) swFast = Stopwatch.StartNew();
                    var zone = growZone; if (zone == null) goto fastRecordFail;
                    var plantDef = zone.GetPlantDefToGrow(); if (plantDef == null) goto fastRecordFail;
                    if (!IsSowableNow(zone, cell, map)) goto fastRecordFail;
                    // Resolve WG / Job via cached SowScanner statics (instantiate one if needed)
                    SowScanner._wg = SowScanner._wg ?? WGResolve.ByWorkerTypes(
                        "WorkGiver_PlantsSow", "WorkGiver_Sow", "WorkGiver_GrowerSow", "WorkGiver_Grower_Sow", "WorkGiver_Grower");
                    var wg = SowScanner._wg; if (wg == null) goto fastRecordFail;
                    SowScanner._job = SowScanner._job ?? (WGResolve.Of(() => JobDefOf.Sow) ?? WGResolve.Job("Sow"));
                    var job = SowScanner._job; if (job == null) goto fastRecordFail;
                    // Direct stat list (avoid reflection / heuristics on first click)
                    var stats = _sowStatsFast;
                    if (stats == null || stats.Count == 0) goto fastRecordFail;
                    if (!PawnCanEverDo(pawn, wg)) { _feedbackThisClick.anyScannersRan = true; _feedbackThisClick.allWorkTypesDisabled = true; goto fastRecordFail; } // work type disabled
                    if (stcExternal && stats.Exists(rs => rs == ST_StatDefOf.TreeFellingSpeed || ToolStatResolver.IsAliasOf(rs, ST_StatDefOf.TreeFellingSpeed))) goto fastRecordFail;
                    if (!JobGate.ShouldBlock(pawn, wg, job, false, out var rkFast, out var aFast1, out var aFast2)) { _gatingNotNeeded++; _feedbackThisClick.anyScannersRan = true; _feedbackThisClick.notBlocked = true; goto fastPathDone; }
                    _gatingNeeded++;
                    var firstRequired = stats[0]; if (firstRequired == null) goto fastRecordFail;
                    string toolName; bool canUpgrade = AssignmentSearchPreview.CanUpgradePreview(pawn, firstRequired, out toolName);
                    if (!canUpgrade)
                    {
                        _feedbackThisClick.anyScannersRan = true;
                        _feedbackThisClick.noToolsAvailable = true;
                        var suffix = "ST_GenericToolSuffix".Translate().ToStringSafe() ?? "tool";
                        toolName = (firstRequired.label ?? "tool").CapitalizeFirst() + " " + suffix;
                    }
                    string label = BuildOptionLabel("ST_Prioritize_Sow".Translate().ToStringSafe(), toolName);
                    var plantDefFast = plantDef; // capture for closure
                    Action act = () =>
                    {
                        bool immediate = KeyBindingDefOf.QueueOrder.IsDown; // Shift => immediate
                        ExecuteRescue(pawn, new RescueTarget
                        {
                            ClickCell = cell,
                            IconThing = null,
                            WorkGiverDef = wg,
                            JobDef = job,
                            RequiredStats = stats,
                            PriorityLabel = "ST_Prioritize_Sow".Translate().ToStringSafe(),
                            MakeJob = _ =>
                            {
                                var j = JobMaker.MakeJob(job, cell);
                                try { j.plantDefToSow = plantDefFast; } catch { }
                                return j;
                            }
                        }, firstRequired, immediate);
                    };
                    var opt = new FloatMenuOption(label, act) { iconThing = null, autoTakeable = false };
                    options.Add(FloatMenuUtility.DecoratePrioritizedTask(opt, pawn, new LocalTargetInfo(cell)));
                    Provider_STPrioritizeWithRescue.NotifyOptionAdded();
                    _lastConsideredCount = 1;
                    _scanDescribed++;
                    if (timing && swFast != null) { swFast.Stop(); _scannerRecords.Add(new ScannerRecord { Name = nameof(SowScanner), Ms = swFast.ElapsedMilliseconds, Reason = "Described(Fast)", Described = true }); }
                    return;
                fastRecordFail:
                    if (timing && swFast != null) { swFast.Stop(); _scannerRecords.Add(new ScannerRecord { Name = nameof(SowScanner), Ms = swFast.ElapsedMilliseconds, Reason = "DescribeFailed(Fast)", Described = false }); }
                fastPathDone:
                    _lastConsideredCount = 1; // we only considered sow
                    // Add disabled feedback if fast path considered but didn't add option
                    if (_feedbackThisClick.anyScannersRan)
                    {
                        AddDisabledFeedbackOption(pawn, options, _feedbackThisClick);
                    }
                    return; // fast path ends irrespective of success (success added option above)
                }
                catch (Exception exFast)
                {
                    if (Prefs.DevMode && settingsFast != null && settingsFast.debugLogging)
                        Log.Warning("[ST.RightClick] Sow fast path exception: " + exFast);
                    return;
                }
            }

            // Build temporary list of (scanner, provisional WGDef, score)
            var scored = new List<(IRescueTargetScanner scanner, WorkGiverDef wg, int score)>();
            if (Prefs.DevMode && s.debugLogging) ResetInstrumentation();
            foreach (var scanner in Scanners)
            {
                try
                {
                    Stopwatch sw = null; if (Prefs.DevMode && s.debugLogging) sw = Stopwatch.StartNew();
                    string reason = string.Empty; bool described = false;
                    // Early skip: if pure sow context run only SowScanner (do not record others at all)
                    if (sowContext && scanner.GetType() != typeof(SowScanner)) { if (sw != null) sw.Stop(); continue; }
                    if (!scanner.CanHandle(ctx)) { reason = "CanHandleFalse"; _scanCanHandleFalse++; }
                    else if (!scanner.TryDescribeTarget(pawn, ctx, out var desc) || desc.WorkGiverDef == null)
                    {
                        reason = "DescribeFailed";
                        // Append granular scanner-provided failure reason if available
                        if (ScannerDiagnostics.TryGet(scanner.GetType(), out var fr) && !string.IsNullOrEmpty(fr))
                            reason += ":" + fr;
                        _scanDescribeFailed++;
                    }
                    else
                    {
                        bool treeSuppressed = false;
                        if (stcExternal)
                        {
                            try
                            {
                                if (desc.RequiredStats != null && desc.RequiredStats.Exists(rs => rs == ST_StatDefOf.TreeFellingSpeed || ToolStatResolver.IsAliasOf(rs, ST_StatDefOf.TreeFellingSpeed)))
                                { treeSuppressed = true; reason = "TreeSuppressed"; _scanTreeSuppressed++; }
                            }
                            catch { }
                        }
                        if (!treeSuppressed)
                        {
                            if (!PawnCanEverDo(pawn, desc.WorkGiverDef)) { reason = "WorkTypeDisabled"; _scanWorkTypeDisabled++; }
                            else
                            {
                                var wg = desc.WorkGiverDef; int sc = Provider_STPrioritizeWithRescue.ScoreWGForClick(wg, primaryThing, cell, map);
                                scored.Add((scanner, wg, sc)); described = true; reason = "Described"; _scanDescribed++;
                            }
                        }
                    }
                    if (sw != null)
                    {
                        sw.Stop();
                        _scannerRecords.Add(new ScannerRecord { Name = scanner.GetType().Name, Ms = sw.ElapsedMilliseconds, Reason = reason, Described = described });
                    }
                }
                catch { }
            }

            // Sort descending by score
            scored.Sort((a, b) => b.score.CompareTo(a.score));
            _lastConsideredCount = scored.Count;

            bool success = false;
            // Pass 1: prioritized order
            foreach (var entry in scored)
            {
                if (Provider_STPrioritizeWithRescue.AlreadySatisfiedThisClick()) break;
                try
                {
                    if (!entry.scanner.TryDescribeTarget(pawn, ctx, out var desc)) continue;
                    if (!PawnCanEverDo(pawn, desc.WorkGiverDef)) continue; // work type disabled now
                    if (desc.RequiredStats == null || desc.RequiredStats.Count == 0) continue;
                    if (stcExternal && desc.RequiredStats.Exists(rs => rs == ST_StatDefOf.TreeFellingSpeed || ToolStatResolver.IsAliasOf(rs, ST_StatDefOf.TreeFellingSpeed))) continue; // suppress tree option
                    if (!JobGate.ShouldBlock(pawn, desc.WorkGiverDef, desc.JobDef, false, out var rk, out var a1, out var a2)) { _gatingNotNeeded++; continue; }
                    _gatingNeeded++;
                    var firstRequired = desc.RequiredStats[0]; if (firstRequired == null) continue;
                    string toolName; bool canUpgrade = AssignmentSearchPreview.CanUpgradePreview(pawn, firstRequired, out toolName);
                    if (!canUpgrade)
                    {
                        var suffix = "ST_GenericToolSuffix".Translate().ToStringSafe() ?? "tool";
                        toolName = (firstRequired.label ?? "tool").CapitalizeFirst() + " " + suffix;
                    }
                    string label = BuildOptionLabel(desc.PriorityLabel, toolName);
                    var capture = desc; var reqStat = firstRequired;
                    Action act = () =>
                    {
                        bool immediate = KeyBindingDefOf.QueueOrder.IsDown;
                        ExecuteRescue(pawn, capture, reqStat, immediate);
                    };
                    var opt = new FloatMenuOption(label, act) { iconThing = capture.IconThing, autoTakeable = false };
                    options.Add(FloatMenuUtility.DecoratePrioritizedTask(opt, pawn, new LocalTargetInfo(desc.ClickCell)));
                    Provider_STPrioritizeWithRescue.NotifyOptionAdded();
                    success = true;
                    break;
                }
                catch (Exception ex) { Log.Warning("[SurvivalTools.RightClickRescue] Scanner (score pass) exception: " + ex); }
            }

            // Pass 2: unfiltered original order fallback (only if nothing succeeded)
            if (!success)
            {
                int workTypeDisabledCount = 0;
                int notBlockedCount = 0;
                int noToolCount = 0;
                int scannersRan = 0;

                foreach (var scanner in Scanners)
                {
                    if (Provider_STPrioritizeWithRescue.AlreadySatisfiedThisClick()) break;
                    try
                    {
                        if (!scanner.CanHandle(ctx)) continue;
                        scannersRan++;
                        if (!scanner.TryDescribeTarget(pawn, ctx, out var desc)) continue;
                        if (!PawnCanEverDo(pawn, desc.WorkGiverDef)) { workTypeDisabledCount++; continue; }
                        if (desc.RequiredStats == null || desc.RequiredStats.Count == 0) continue;
                        if (stcExternal && desc.RequiredStats.Exists(rs => rs == ST_StatDefOf.TreeFellingSpeed || ToolStatResolver.IsAliasOf(rs, ST_StatDefOf.TreeFellingSpeed))) continue; // suppress tree option
                        if (!JobGate.ShouldBlock(pawn, desc.WorkGiverDef, desc.JobDef, false, out var rk, out var a1, out var a2)) { _gatingNotNeeded++; notBlockedCount++; continue; }
                        _gatingNeeded++;
                        var firstRequired = desc.RequiredStats[0]; if (firstRequired == null) continue;
                        string toolName; bool canUpgrade = AssignmentSearchPreview.CanUpgradePreview(pawn, firstRequired, out toolName);
                        if (!canUpgrade)
                        {
                            noToolCount++;
                            var suffix = "ST_GenericToolSuffix".Translate().ToStringSafe() ?? "tool";
                            toolName = (firstRequired.label ?? "tool").CapitalizeFirst() + " " + suffix;
                        }
                        string label = BuildOptionLabel(desc.PriorityLabel, toolName);
                        var capture = desc; var reqStat = firstRequired;
                        Action act = () =>
                        {
                            bool immediate = KeyBindingDefOf.QueueOrder.IsDown;
                            ExecuteRescue(pawn, capture, reqStat, immediate);
                        };
                        var opt = new FloatMenuOption(label, act) { iconThing = capture.IconThing, autoTakeable = false };
                        options.Add(FloatMenuUtility.DecoratePrioritizedTask(opt, pawn, new LocalTargetInfo(desc.ClickCell)));
                        Provider_STPrioritizeWithRescue.NotifyOptionAdded();
                        success = true;
                        break;
                    }
                    catch (Exception ex) { Log.Warning("[SurvivalTools.RightClickRescue] Scanner (fallback) exception: " + ex); }
                }

                // Add disabled feedback if scanners ran but nothing added
                _feedbackThisClick.anyScannersRan = scannersRan > 0;
                _feedbackThisClick.allWorkTypesDisabled = workTypeDisabledCount > 0 && notBlockedCount == 0 && noToolCount == 0;
                _feedbackThisClick.noToolsAvailable = noToolCount > 0 && workTypeDisabledCount == 0 && notBlockedCount == 0;
                _feedbackThisClick.notBlocked = notBlockedCount > 0 && workTypeDisabledCount == 0 && noToolCount == 0;
            }

            // Add disabled feedback option if rescue was considered but couldn't be added
            if (!success && _feedbackThisClick.anyScannersRan)
            {
                AddDisabledFeedbackOption(pawn, options, _feedbackThisClick);
            }

            // (Timing logged by provider; instrumentation captured above.)
        }

        private static void AddDisabledFeedbackOption(Pawn pawn, List<FloatMenuOption> options, FeedbackReason reason)
        {
            string label;
            try
            {
                if (reason.allWorkTypesDisabled)
                {
                    label = $"{pawn.LabelShort} is not assigned to any work types that could be performed here. Check the Work tab.";
                }
                else if (reason.noToolsAvailable)
                {
                    label = $"No suitable tools are available for {pawn.LabelShort} to pick up. Craft or buy the required tools.";
                }
                else if (reason.notBlocked)
                {
                    label = $"{pawn.LabelShort} already has the required tool or doesn't need one for this job.";
                }
                else
                {
                    return; // No feedback to show
                }

                var disabledOption = new FloatMenuOption(label, null)
                {
                    Disabled = true,
                    autoTakeable = false
                };
                options.Add(disabledOption);
                // Notify so duplicate calls are prevented
                Provider_STPrioritizeWithRescue.NotifyOptionAdded();
            }
            catch { }
        }

        private static string BuildOptionLabel(string priorityLabel, string toolName)
        {
            string label;
            try
            {
                label = $"{priorityLabel} (" + "ST_WillFetchTool".Translate(toolName) + ")";
            }
            catch { label = priorityLabel + " (will fetch " + toolName + ")"; }
            // If the queue (Shift) key is held at menu build time, indicate Immediate execution intent
            try
            {
                if (KeyBindingDefOf.QueueOrder != null && KeyBindingDefOf.QueueOrder.IsDown) label += " (Immediate)"; // TODO: optional future translation key
            }
            catch { }
            return label;
        }

        // Lightweight execution logger with cooldown to avoid log spam when players issue rapid rescues.
        internal static class RescueExecLogger
        {
            private static int _lastLogTick = -9999;
            private const int COOLDOWN_TICKS = 90; // ~1.5s @60 tps
            internal static void Write(string jobDefName, bool immediate, bool prereqs, bool upgradeQueued, int dropJobs)
            {
                try
                {
                    int now = Find.TickManager?.TicksGame ?? 0;
                    if (now - _lastLogTick < COOLDOWN_TICKS) return;
                    _lastLogTick = now;
                    Verse.Log.Message($"[RightClick] Rescue exec: job={jobDefName} immediate={immediate} prereqs={prereqs} upgradeQueued={upgradeQueued} dropJobs={dropJobs}");
                }
                catch { }
            }
        }

        private static void ExecuteRescue(Pawn pawn, RescueTarget capture, StatDef stat, bool immediate = false)
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

            bool prereqs = upgradeQueued || dropJobs > 0;

            // Debug log (dev only)
            // Log cooldown (avoid spam when rapidly issuing rescues); 90 tick (~1.5s) window
            if (Prefs.DevMode && settings.debugLogging)
            {
                try { RescueExecLogger.Write(job.def.defName, immediate, prereqs, upgradeQueued, dropJobs); } catch { }
            }

            if (!immediate)
            {
                // Always enqueue forced job at tail; never interrupt current job regardless of prerequisites.
                try { pawn.jobs?.jobQueue?.EnqueueLast(job, JobTag.Misc); }
                catch
                {
                    // Fallback if enqueue fails: last resort start (should be rare)
                    if (worker != null && pawn.jobs.TryTakeOrderedJobPrioritizedWork(job, worker, capture.ClickCell)) return;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }
                return;
            }

            // Immediate (Shift)
            if (prereqs)
            {
                // Enqueue forced job (runs after front-queued drops/equip), then interrupt current job now.
                try { pawn.jobs?.jobQueue?.EnqueueLast(job, JobTag.Misc); }
                catch { /* ignore */ }
                try
                {
                    var cur = pawn.jobs?.curJob;
                    bool canInterrupt = cur == null || cur.def == null || cur.def.playerInterruptible;
                    if (canInterrupt)
                        pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, true);
                    // else: leave current job; chain will start after it finishes (still acceptable fallback)
                }
                catch { }
                return;
            }

            // Immediate with no prerequisites: start job now for instant feedback.
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

    // --- Scanner diagnostics helper (granular failure reasons without changing interface contract) ---
    internal static class ScannerDiagnostics
    {
        private static readonly Dictionary<Type, string> _lastFail = new Dictionary<Type, string>();
        internal static void Fail<T>(string reason) where T : RightClickRescueBuilder.IRescueTargetScanner => _lastFail[typeof(T)] = reason;
        internal static bool TryGet(Type t, out string reason) => _lastFail.TryGetValue(t, out reason);
        internal static void Clear() => _lastFail.Clear();
    }

    // ---------------- Scanners ----------------
    // Each scanner mirrors the spec given in the user request, using WGResolve helpers.

    internal sealed class MineScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        private static WorkGiverDef _wg; private static JobDef _job;
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedCell.IsValid;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default;
            var cell = ctx.ClickedCell;
            var map = pawn?.Map;
            if (pawn == null || !cell.IsValid || map == null || !cell.InBounds(map)) return false;
            if (map.designationManager.DesignationAt(cell, DesignationDefOf.Mine) == null) return false;
            _wg = _wg ?? WGResolve.ByWorkerTypes("WorkGiver_Miner", "WorkGiver_Mine");
            _job = _job ?? (WGResolve.Of(() => JobDefOf.Mine) ?? WGResolve.Job("Mine"));
            var wg = _wg; var job = _job;
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
        private static WorkGiverDef _wg; private static JobDef _job;
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedThings.Count > 0;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; if (pawn == null) return false;
            var map = pawn.Map;
            foreach (var t in ctx.ClickedThings)
            {
                if (map.designationManager.DesignationOn(t, DesignationDefOf.Deconstruct) == null) continue;
                _wg = _wg ?? WGResolve.ByWorkerTypes("WorkGiver_Deconstruct", "WorkGiver_ConstructDeconstruct");
                _job = _job ?? (WGResolve.Of(() => JobDefOf.Deconstruct) ?? WGResolve.Job("Deconstruct"));
                var wg = _wg; var job = _job;
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

    internal sealed class UninstallScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        private static WorkGiverDef _wg; private static JobDef _job;
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedThings.Count > 0;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; if (pawn == null) return false;
            var map = pawn.Map;
            foreach (var t in ctx.ClickedThings)
            {
                if (t == null) continue;
                // Require uninstall designation (mirrors vanilla Prioritize uninstall visibility)
                var des = map.designationManager.DesignationOn(t, DesignationDefOf.Uninstall);
                if (des == null) continue;
                _wg = _wg ?? WGResolve.ByWorkerTypes("WorkGiver_Uninstall");
                _job = _job ?? WGResolve.Job("Uninstall");
                var wg = _wg; var job = _job;
                desc = new RightClickRescueBuilder.RescueTarget
                {
                    ClickCell = t.Position,
                    IconThing = t,
                    WorkGiverDef = wg,
                    JobDef = job,
                    RequiredStats = StatGatingHelper.GetStatsForWorkGiver(wg) ?? new List<StatDef> { ST_StatDefOf.DeconstructionSpeed },
                    PriorityLabel = "ST_Prioritize_Uninstall".Translate().ToStringSafe() ?? "Prioritize uninstall",
                    MakeJob = _ => JobMaker.MakeJob(job, t)
                };
                return desc.WorkGiverDef != null && desc.JobDef != null;
            }
            return false;
        }
    }

    internal sealed class SmoothWallScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        private static WorkGiverDef _wg; private static JobDef _job;
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedCell.IsValid;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; var cell = ctx.ClickedCell; var map = pawn?.Map; if (pawn == null || map == null) return false;
            if (map.designationManager.DesignationAt(cell, DesignationDefOf.SmoothWall) == null) return false;
            _wg = _wg ?? WGResolve.ByWorkerTypes("WorkGiver_ConstructSmoothWall");
            _job = _job ?? WGResolve.Job("SmoothWall");
            var wg = _wg; var job = _job;
            // Build required + optional stat list manually to ensure ConstructionSpeed is primary gate.
            var stats = StatGatingHelper.GetStatsForWorkGiver(wg);
            if (stats == null || stats.Count == 0) stats = new List<StatDef> { StatDefOf.ConstructionSpeed };
            else if (!stats.Contains(StatDefOf.ConstructionSpeed)) stats.Insert(0, StatDefOf.ConstructionSpeed);
            // TODO[SMOOTHING_TOOL_PURPOSE]: future dedicated smoothing tool may elevate SmoothingSpeed weighting here.
            desc = new RightClickRescueBuilder.RescueTarget
            {
                ClickCell = cell,
                WorkGiverDef = wg,
                JobDef = job,
                IconThing = null,
                RequiredStats = stats,
                PriorityLabel = "ST_Prioritize_SmoothWall".Translate().ToStringSafe(),
                MakeJob = _ => JobMaker.MakeJob(job, cell)
            };
            return desc.WorkGiverDef != null && desc.JobDef != null;
        }
    }

    internal sealed class SmoothFloorScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        private static WorkGiverDef _wg; private static JobDef _job;
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedCell.IsValid;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; var cell = ctx.ClickedCell; var map = pawn?.Map; if (pawn == null || map == null) return false;
            if (map.designationManager.DesignationAt(cell, DesignationDefOf.SmoothFloor) == null) return false;
            _wg = _wg ?? WGResolve.ByWorkerTypes("WorkGiver_ConstructSmoothFloor");
            _job = _job ?? WGResolve.Job("SmoothFloor");
            var wg = _wg; var job = _job;
            var stats = StatGatingHelper.GetStatsForWorkGiver(wg);
            if (stats == null || stats.Count == 0) stats = new List<StatDef> { StatDefOf.ConstructionSpeed };
            else if (!stats.Contains(StatDefOf.ConstructionSpeed)) stats.Insert(0, StatDefOf.ConstructionSpeed);
            // TODO[SMOOTHING_TOOL_PURPOSE]: future dedicated smoothing tool may elevate SmoothingSpeed weighting here.
            desc = new RightClickRescueBuilder.RescueTarget
            {
                ClickCell = cell,
                WorkGiverDef = wg,
                JobDef = job,
                IconThing = null,
                RequiredStats = stats,
                PriorityLabel = "ST_Prioritize_SmoothFloor".Translate().ToStringSafe(),
                MakeJob = _ => JobMaker.MakeJob(job, cell)
            };
            return desc.WorkGiverDef != null && desc.JobDef != null;
        }
    }

    internal sealed class FinishFrameScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        private static WorkGiverDef _wg; private static JobDef _job;
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedThings.Count > 0;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; if (pawn == null) return false;
            foreach (var t in ctx.ClickedThings)
            {
                if (t is Frame f)
                {
                    _wg = _wg ?? WGResolve.ByWorkerTypes("WorkGiver_ConstructFinishFrames");
                    _job = _job ?? WGResolve.Job("FinishFrame");
                    var wg = _wg; var job = _job;
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
        private static WorkGiverDef _wg; private static JobDef _job;
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedThings.Count > 0;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; if (pawn == null) return false; var map = pawn.Map;
            foreach (var t in ctx.ClickedThings)
            {
                var p = t as Plant;
                if (p == null) continue;
                if (map.designationManager.DesignationOn(p, DesignationDefOf.CutPlant) == null) continue;
                // External STC authority: do not expose tree felling rescue (tree cut designations on trees)
                if (TreeSystemArbiter.Authority == TreeAuthority.SeparateTreeChopping && (p.def?.plant?.IsTree ?? false)) continue;
                _wg = _wg ?? WGResolve.ByWorkerTypes("WorkGiver_PlantsCut", "WorkGiver_CutPlant");
                _job = _job ?? (WGResolve.Of(() => JobDefOf.CutPlant) ?? WGResolve.Job("CutPlant"));
                var wg = _wg; var job = _job;
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
        private static WorkGiverDef _wg; private static JobDef _job;
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedThings.Count > 0;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; if (pawn == null) return false; var map = pawn.Map;
            foreach (var t in ctx.ClickedThings)
            {
                var p = t as Plant;
                if (p == null) continue;
                if (map.designationManager.DesignationOn(p, DesignationDefOf.HarvestPlant) == null) continue;
                bool isTree = p.def.plant?.IsTree ?? false;
                // External STC authority: skip tree harvest (ChopWood) entirely
                if (isTree && TreeSystemArbiter.Authority == TreeAuthority.SeparateTreeChopping) continue;
                _wg = _wg ?? WGResolve.ByWorkerTypes("WorkGiver_PlantsHarvest", "WorkGiver_Harvest");
                _job = _job ?? (WGResolve.Of(() => JobDefOf.Harvest) ?? WGResolve.Job("Harvest"));
                var wg = _wg; var job = _job;
                var labelKey = isTree ? "ChopWood" : "Harvest";
                var label = (labelKey == "ChopWood" ? "ST_Prioritize_ChopWood" : "ST_Prioritize_Harvest").Translate().ToStringSafe();
                desc = new RightClickRescueBuilder.RescueTarget
                {
                    ClickCell = p.Position,
                    IconThing = p,
                    WorkGiverDef = wg,
                    JobDef = job,
                    RequiredStats = StatGatingHelper.GetStatsForWorkGiver(wg) ?? (isTree ? new List<StatDef> { ST_StatDefOf.PlantHarvestingSpeed, ST_StatDefOf.TreeFellingSpeed } : new List<StatDef> { ST_StatDefOf.PlantHarvestingSpeed }),
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
        internal static WorkGiverDef _wg; internal static JobDef _job;
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedCell.IsValid;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; if (pawn == null) return false; var map = pawn.Map; var cell = ctx.ClickedCell; if (!cell.IsValid) return false;
            var zone = cell.GetZone(map) as Zone_Growing; if (zone == null) { ScannerDiagnostics.Fail<SowScanner>("NoZone"); return false; }
            var plantDef = zone.GetPlantDefToGrow(); if (plantDef == null) { ScannerDiagnostics.Fail<SowScanner>("PlantDefNull"); return false; }
            // Expanded WG resolution attempts (vanilla + historical + mod variants)
            if (_wg == null)
            {
                _wg = WGResolve.ByWorkerTypes(
                    "WorkGiver_PlantsSow",
                    "WorkGiver_Sow",
                    "WorkGiver_GrowerSow",
                    "WorkGiver_Grower_Sow",
                    "WorkGiver_Grower"
                );
            }
            var wg = _wg;
            if (wg == null) { ScannerDiagnostics.Fail<SowScanner>("WGNull"); return false; }
            _job = _job ?? (WGResolve.Of(() => JobDefOf.Sow) ?? WGResolve.Job("Sow"));
            var job = _job;
            if (job == null) { ScannerDiagnostics.Fail<SowScanner>("JobNull"); return false; }
            if (!RightClickRescueBuilder.IsSowableNow(zone, cell, map)) { ScannerDiagnostics.Fail<SowScanner>("NotSowable"); return false; }
            var stats = StatGatingHelper.GetStatsForWorkGiver(wg) ?? new List<StatDef> { ST_StatDefOf.SowingSpeed };
            if (stats == null || stats.Count == 0) { ScannerDiagnostics.Fail<SowScanner>("NoStats"); return false; }
            var plantDefCap = plantDef;
            desc = new RightClickRescueBuilder.RescueTarget
            {
                ClickCell = cell,
                IconThing = null,
                WorkGiverDef = wg,
                JobDef = job,
                RequiredStats = stats,
                PriorityLabel = "ST_Prioritize_Sow".Translate().ToStringSafe(),
                MakeJob = _ =>
                {
                    var j = JobMaker.MakeJob(job, cell);
                    try { j.plantDefToSow = plantDefCap; } catch { }
                    return j;
                }
            };
            return true;
        }
    }

    internal sealed class RepairScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        private static WorkGiverDef _wg; private static JobDef _job;
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedThings.Count > 0;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; if (pawn == null) return false;
            foreach (var t in ctx.ClickedThings)
            {
                var b = t as Building;
                if (b == null) continue;
                if (b.HitPoints >= b.MaxHitPoints) continue;
                _wg = _wg ?? WGResolve.ByWorkerTypes("WorkGiver_Repair");
                _job = _job ?? (WGResolve.Of(() => JobDefOf.Repair) ?? WGResolve.Job("Repair"));
                var wg = _wg; var job = _job;
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
        private static WorkGiverDef _wg; private static JobDef _job;
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
            _wg = _wg ?? WGResolve.ByWorkerTypes("WorkGiver_Clean");
            _job = _job ?? (WGResolve.Of(() => JobDefOf.Clean) ?? WGResolve.Job("Clean"));
            var wg = _wg; var job = _job;
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

    internal sealed class ResearchScanner : RightClickRescueBuilder.IRescueTargetScanner
    {
        private static WorkGiverDef _wg; private static JobDef _job;
        private static Type _benchType;
        private static bool _inited;
        private static Func<object> _getCurrentProject; // returns active project (ResearchProjectDef) or null
        private static int _projCacheTick;
        private static object _projCache;
        private const int PROJ_CACHE_INTERVAL = 30; // half second @60 tps
        private static bool _accessorFallbackBuilt;
        private static bool _wgFallbackTried; // ensure we only do an expensive fallback scan once if needed
                                              // Heuristic bench cache: ThingDef -> true if recognized as a research bench surrogate (for mods like Research Reinvented)
        private static readonly System.Collections.Generic.Dictionary<ThingDef, bool> _benchDefCache = new System.Collections.Generic.Dictionary<ThingDef, bool>();
        private static bool HeuristicBench(Thing t)
        {
            if (t == null) return false; var def = t.def; if (def == null) return false;
            if (_benchDefCache.TryGetValue(def, out var cached)) return cached;
            bool result = false;
            try
            {
                string dn = def.defName ?? string.Empty;
                if (!result && dn.IndexOf("Research", StringComparison.OrdinalIgnoreCase) >= 0) result = true;
                if (!result && dn.IndexOf("Lab", StringComparison.OrdinalIgnoreCase) >= 0) result = true; // some mods use *Lab
                                                                                                          // Building flag (some defs expose a bool researchBench or similar)
                try
                {
                    var b = def.building; if (b != null)
                    {
                        var bt = b.GetType();
                        var fi = bt.GetField("researchBench", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (fi != null)
                        {
                            object val = null; try { val = fi.GetValue(b); } catch { }
                            if (val is bool bb && bb) result = true;
                        }
                        if (!result)
                        {
                            var pi = bt.GetProperty("ResearchBench", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                            if (pi != null)
                            {
                                object val = null; try { val = pi.GetValue(b, null); } catch { }
                                if (val is bool pb && pb) result = true;
                            }
                        }
                    }
                }
                catch { }
                // Stat bases referencing ResearchSpeed
                if (!result)
                {
                    try
                    {
                        var statBases = def.statBases;
                        if (statBases != null)
                        {
                            foreach (var sb in statBases)
                            {
                                var s = sb?.stat; if (s == null) continue;
                                var sn = s.defName ?? s.label ?? string.Empty;
                                if (sn.IndexOf("ResearchSpeed", StringComparison.OrdinalIgnoreCase) >= 0) { result = true; break; }
                            }
                        }
                    }
                    catch { }
                }
                // Comps with research in the name
                if (!result)
                {
                    try
                    {
                        var twc = t as ThingWithComps; // only ThingWithComps exposes AllComps
                        var comps = twc?.AllComps;
                        if (comps != null)
                        {
                            for (int i = 0; i < comps.Count; i++)
                            {
                                var c = comps[i]; var n = c?.GetType()?.Name; if (n == null) continue;
                                if (n.IndexOf("Research", StringComparison.OrdinalIgnoreCase) >= 0) { result = true; break; }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            _benchDefCache[def] = result;
            return result;
        }
        internal static void EnsureInit()
        {
            if (_inited) return; _inited = true;
            try
            {
                // Resolve bench type once (covers mod namespaces)
                _benchType = HarmonyLib.AccessTools.TypeByName("RimWorld.Building_ResearchBench") ?? HarmonyLib.AccessTools.TypeByName("Building_ResearchBench");
                // Build delegate for current research project
                var rm = Find.ResearchManager;
                if (rm != null)
                {
                    var type = rm.GetType();
                    var field = type.GetField("currentProj", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (field != null)
                    {
                        _getCurrentProject = () =>
                        {
                            try { return field.GetValue(Find.ResearchManager); } catch { return null; }
                        };
                    }
                    else
                    {
                        var prop = type.GetProperty("CurrentProj", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        if (prop != null)
                        {
                            _getCurrentProject = () =>
                            {
                                try { return prop.GetValue(Find.ResearchManager, null); } catch { return null; }
                            };
                        }
                    }
                    // Fallback: search any field/property whose type name contains "ResearchProject" once (covers naming changes like currentProjInt)
                    if (_getCurrentProject == null && !_accessorFallbackBuilt)
                    {
                        _accessorFallbackBuilt = true;
                        try
                        {
                            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                            var members = type.GetMembers(flags);
                            foreach (var m in members)
                            {
                                try
                                {
                                    System.Type mt = null; Func<object> del = null;
                                    if (m is System.Reflection.FieldInfo fi)
                                    {
                                        mt = fi.FieldType;
                                        del = () => { try { return fi.GetValue(Find.ResearchManager); } catch { return null; } };
                                    }
                                    else if (m is System.Reflection.PropertyInfo pi && pi.GetIndexParameters().Length == 0)
                                    {
                                        mt = pi.PropertyType;
                                        del = () => { try { return pi.GetValue(Find.ResearchManager, null); } catch { return null; } };
                                    }
                                    if (mt != null && mt.Name.IndexOf("ResearchProject", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        _getCurrentProject = del; break;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }
                if (_getCurrentProject == null) _getCurrentProject = () => null;
                // Touch once to JIT delegate
                try { var _ = _getCurrentProject(); } catch { }
            }
            catch { }
        }
        internal static void Warm() { try { EnsureInit(); } catch { } }
        public bool CanHandle(FloatMenuContext ctx) => ctx.ClickedThings.Count > 0;
        public bool TryDescribeTarget(Pawn pawn, FloatMenuContext ctx, out RightClickRescueBuilder.RescueTarget desc)
        {
            desc = default; if (pawn == null) return false;
            EnsureInit();
            // Cached current project (avoid delegate + potential manager churn every frame)
            try
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                if (now - _projCacheTick >= PROJ_CACHE_INTERVAL)
                {
                    _projCache = _getCurrentProject();
                    _projCacheTick = now;
                }
            }
            catch { }
            if (_projCache == null) { return false; }
            var benchType = _benchType;
            foreach (var t in ctx.ClickedThings)
            {
                if (t == null) continue;
                bool isBench = false;
                if (benchType != null && benchType.IsAssignableFrom(t.GetType())) isBench = true; else if (HeuristicBench(t)) isBench = true;
                if (!isBench) continue;
                // Correct vanilla worker type is WorkGiver_Researcher. Older attempts used _Research / _DoResearch which miss and trigger slow reflection fallback.
                if (_wg == null)
                {
                    _wg = WGResolve.ByWorkerTypes("WorkGiver_Researcher", "Researcher", "WorkGiver_Research", "WorkGiver_DoResearch");
                    // If still null, perform a one-time broad fallback: scan defs for any worker whose defName or worker class contains "Research" and whose workType matches Research work type.
                    if (_wg == null && !_wgFallbackTried)
                    {
                        _wgFallbackTried = true;
                        try
                        {
                            WorkTypeDef researchWT = null;
                            try { researchWT = WorkTypeDefOf.Research; } catch { }
                            foreach (var def in DefDatabase<WorkGiverDef>.AllDefs)
                            {
                                if (def == null) continue;
                                bool match = false;
                                try
                                {
                                    var w = def.Worker; // may allocate; only runs once worst case
                                    var wt = w?.GetType();
                                    var name = wt?.Name;
                                    if (!match && name != null && name.IndexOf("Research", StringComparison.OrdinalIgnoreCase) >= 0) match = true;
                                    if (!match && def.defName != null && def.defName.IndexOf("Research", StringComparison.OrdinalIgnoreCase) >= 0) match = true;
                                    if (!match && researchWT != null && def.workType == researchWT) match = true;
                                }
                                catch { }
                                if (match) { _wg = def; break; }
                            }
                        }
                        catch { }
                    }
                }
                _job = _job ?? (WGResolve.Of(() => JobDefOf.Research) ?? WGResolve.Job("Research"));
                var wg = _wg; var job = _job;
                var reqStat = Compat.CompatAPI.GetResearchSpeedStat();
                var reqList = new List<StatDef>(); if (reqStat != null) reqList.Add(reqStat);
                desc = new RightClickRescueBuilder.RescueTarget
                {
                    ClickCell = t.Position,
                    IconThing = t,
                    WorkGiverDef = wg,
                    JobDef = job,
                    RequiredStats = reqList,
                    PriorityLabel = "ST_Prioritize_Research".Translate().ToStringSafe() ?? "Prioritize research",
                    MakeJob = _ => JobMaker.MakeJob(job, t)
                };
                return desc.WorkGiverDef != null && desc.JobDef != null;
            }
            return false;
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
