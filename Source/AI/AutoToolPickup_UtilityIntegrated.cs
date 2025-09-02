// RimWorld 1.6 / C# 7.3
// Source/AI/AutoToolPickup_UtilityIntegrated.cs
//
// Auto-Tool Pickup (utility-integrated)
// - Finds & picks up helpful tools before doing work
// - Virtual tool support (e.g., Cloth as tool-stuff)
// - Hardcore/extra-hardcore gating
// - Aggregated rejection logging to reduce spam
// - Safe map/area checks to avoid OOB errors in Area.get_Item

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    public static class Patch_JobGiver_Work_TryIssueJobPackage_AutoTool
    {
        private const int SearchRadius = 28;

        #region Entry

        public static void Postfix(Pawn pawn, JobIssueParams jobParams, ref ThinkResult __result)
        {
            try
            {
                if (pawn == null || __result.Job == null) return;

                if (!ShouldAttemptAutoTool(__result.Job, pawn, out var requiredStats))
                    return;

                bool isDebug = SurvivalToolUtility.IsDebugLoggingEnabled;
                var jobName = __result.Job.def?.defName ?? "unknown";
                string logKey = $"AutoTool_Running_{pawn.ThingID}_{jobName}";

                if (isDebug && SurvivalToolUtility.ShouldLogWithCooldown(logKey))
                {
                    var statList = requiredStats == null ? "(none)" : string.Join(", ", requiredStats.Select(s => s.defName));
                    Log.Message($"[SurvivalTools.AutoTool] Running for {pawn.LabelShort} doing {jobName} (needs {statList})");
                }

                // Already has a suitable tool?
                if (PawnHasHelpfulTool(pawn, requiredStats))
                {
                    if (isDebug && SurvivalToolUtility.ShouldLogWithCooldown($"AutoTool_Skip_{pawn.ThingID}"))
                        Log.Message($"[SurvivalTools.AutoTool] Pawn {pawn.LabelShort} already has a helpful tool. Skipping.");
                    return;
                }

                // Hardcore gating: if we can't acquire something, block the job
                if (SurvivalTools.Settings?.hardcoreMode == true)
                {
                    bool shouldBlockJob = false;

                    foreach (var stat in requiredStats ?? Enumerable.Empty<StatDef>())
                    {
                        if (ShouldBlockJobForMissingStat(stat) && !CanAcquireHelpfulToolNow(pawn, requiredStats, isDebug))
                        {
                            shouldBlockJob = true;
                            break;
                        }
                    }

                    if (shouldBlockJob)
                    {
                        if (isDebug)
                            Log.Message($"[SurvivalTools.AutoTool] Hardcore mode: No tool available for {pawn.LabelShort} doing {jobName}. Cancelling job.");
                        __result = ThinkResult.NoJob;
                        return;
                    }
                }

                // Find & enqueue pickup
                var bestTool = FindBestHelpfulTool(pawn, requiredStats, isDebug);
                if (bestTool == null)
                {
                    if (isDebug && SurvivalToolUtility.ShouldLogWithCooldown($"AutoTool_NoTool_{pawn.ThingID}"))
                        Log.Message($"[SurvivalTools.AutoTool] No suitable tool found for {pawn.LabelShort}.");
                    return;
                }

                __result = CreateToolPickupJobs(pawn, __result.Job, bestTool, requiredStats, __result.SourceNode);
            }
            catch (Exception ex)
            {
                // Fail gracefully: don't break job assignment pipeline for other mods.
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    Log.Error($"[SurvivalTools.AutoTool] Exception in AutoTool postfix: {ex}");
            }
        }

        #endregion

        #region Scan preconditions

        private static bool ShouldAttemptAutoTool(Job job, Pawn pawn, out List<StatDef> requiredStats)
        {
            requiredStats = null;

            if (SurvivalTools.Settings?.autoTool != true ||
                job == null ||
                pawn == null ||
                pawn.Map == null ||
                !pawn.CanUseSurvivalTools() ||
                pawn.Drafted ||
                pawn.InMentalState ||
                job.def == JobDefOf.TakeInventory)
            {
                return false;
            }

            // Special-case tree felling -> TreeFellingSpeed
            if (job.def == ST_JobDefOf.FellTree ||
                job.def == ST_JobDefOf.FellTreeDesignated ||
                (job.def == JobDefOf.CutPlant && job.targetA.Thing?.def?.plant?.IsTree == true))
            {
                requiredStats = new List<StatDef> { ST_StatDefOf.TreeFellingSpeed };
                return true;
            }

            requiredStats = SurvivalToolUtility.RelevantStatsFor(job.workGiverDef, job) ?? new List<StatDef>();
            return !requiredStats.NullOrEmpty();
        }

        private static bool PawnHasHelpfulTool(Pawn pawn, List<StatDef> requiredStats)
        {
            if (pawn == null) return false;
            // include VirtualSurvivalTool (inherits SurvivalTool)
            return pawn.GetAllUsableSurvivalTools()
                       .OfType<SurvivalTool>()
                       .Any(st => ToolImprovesAnyRequiredStat(st, requiredStats));
        }

        #endregion

        #region “Can acquire now?” probe

        /// <summary>
        /// Whether the pawn can acquire a helpful tool *now*. If no backing is spawned, we can
        /// still succeed when allowed and the item is discoverable via holder/minified proxy.
        /// </summary>
        private static bool CanAcquireHelpfulToolNow(Pawn pawn, List<StatDef> requiredStats, bool isDebug, bool allowNonSpawnedBecauseHolder = true)
        {
            try
            {
                if (pawn == null) return false;

                if (PawnHasHelpfulTool(pawn, requiredStats)) return true;

                var best = FindBestHelpfulTool(pawn, requiredStats, isDebug);
                if (best == null) return false;

                var backing = SurvivalToolUtility.BackingThing(best, pawn) ?? (best as Thing);
                if (backing == null)
                {
                    if (isDebug)
                    {
                        if (SurvivalToolUtility.ShouldLogWithCooldown($"AutoTool_NoBacking_{pawn.ThingID}"))
                            Log.Message($"[SurvivalTools.AutoTool] CanAcquireHelpfulToolNow: best tool {best.LabelCap} has no backing thing; allowNonSpawnedBecauseHolder={allowNonSpawnedBecauseHolder}");
                    }
                    return allowNonSpawnedBecauseHolder;
                }

                if (!backing.Spawned)
                {
                    if (!allowNonSpawnedBecauseHolder)
                    {
                        if (isDebug && SurvivalToolUtility.ShouldLogWithCooldown($"AutoTool_NotSpawned_{pawn.ThingID}"))
                            Log.Message($"[SurvivalTools.AutoTool] CanAcquireHelpfulToolNow: backing for {best.LabelCap} is not spawned and allowNonSpawnedBecauseHolder=false -> cannot acquire now.");
                        return false;
                    }

                    if (best is VirtualSurvivalTool) return true; // holder/minified was already resolved for virtuals

                    // If it sits inside a holder, also acceptable.
                    return backing.ParentHolder != null;
                }

                // Spawned: must be reachable & reservable
                bool canReserveAndReach = pawn.CanReserveAndReach(backing, PathEndMode.OnCell, pawn.NormalMaxDanger());
                if (isDebug && !canReserveAndReach && SurvivalToolUtility.ShouldLogWithCooldown($"AutoTool_NotReachable_{pawn.ThingID}"))
                    Log.Message($"[SurvivalTools.AutoTool] CanAcquireHelpfulToolNow: backing for {best.LabelCap} is spawned but pawn cannot reserve/reach it.");

                return canReserveAndReach;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Scanner

        private static SurvivalTool FindBestHelpfulTool(Pawn pawn, List<StatDef> requiredStats, bool isDebug)
        {
            try
            {
                requiredStats = requiredStats ?? new List<StatDef>();
                var assignment = pawn?.TryGetComp<Pawn_SurvivalToolAssignmentTracker>()?.CurrentSurvivalToolAssignment;

                SurvivalTool bestTool = null;
                float bestScore = 0f;

                if (pawn == null || pawn.Map == null)
                    return null;

                // Ensure pawn.Position is valid and in bounds before radial search
                if (!pawn.Position.IsValid || !pawn.Position.InBounds(pawn.Map))
                    return null;

                var nearby = GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, SearchRadius, true).ToList();

                var potentialSurvivalTools = nearby.OfType<SurvivalTool>().ToList();
                var potentialEnhancedItems = nearby
                    .Where(t => !(t is SurvivalTool) && t?.def?.GetModExtension<SurvivalToolProperties>() != null)
                    .ToList();

                if (isDebug && SurvivalToolUtility.ShouldLogWithCooldown($"AutoTool_Found_{pawn.ThingID}"))
                    Log.Message($"[SurvivalTools.AutoTool] Found {potentialSurvivalTools.Count} survival tools and {potentialEnhancedItems.Count} enhanced items in search radius");

                // Aggregate rejections to reduce spam: key = "DefName|Reason"
                var rejectCounts = new Dictionary<string, int>(64);
                void CountReject(ThingDef def, string reason)
                {
                    if (!isDebug || def == null) return;
                    var key = def.defName + "|" + NormalizeReason(reason);
                    rejectCounts[key] = rejectCounts.TryGetValue(key, out var c) ? c + 1 : 1;
                }
                string NormalizeReason(string reason)
                {
                    if (string.IsNullOrEmpty(reason)) return "(unspecified)";
                    reason = reason.Trim();
                    if (reason.StartsWith("Can't reach", StringComparison.OrdinalIgnoreCase)) return "Can't reach";
                    if (reason.StartsWith("Forbidden", StringComparison.OrdinalIgnoreCase)) return "Forbidden";
                    if (reason.StartsWith("Not spawned", StringComparison.OrdinalIgnoreCase)) return "Not spawned";
                    if (reason.StartsWith("Disallowed by assignment", StringComparison.OrdinalIgnoreCase)) return "Disallowed by assignment";
                    if (reason.StartsWith("No backing thing", StringComparison.OrdinalIgnoreCase)) return "No backing thing";
                    return reason;
                }

                // Real SurvivalTool objects
                foreach (var tool in potentialSurvivalTools)
                {
                    if (!IsViableCandidate(tool, pawn, assignment, requiredStats, out var reason))
                    {
                        CountReject(tool.def, reason);
                        continue;
                    }

                    float score = SurvivalToolScore(tool, pawn, requiredStats);
                    if (score <= 0f && !SurvivalToolUtility.IsHardcoreModeEnabled)
                        continue;

                    var backingThing = SurvivalToolUtility.BackingThing(tool, pawn) ?? (tool as Thing);
                    if (backingThing == null) continue;

                    // distance penalty (small)
                    try { score -= 0.01f * backingThing.Position.DistanceTo(pawn.Position); } catch { }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTool = tool;
                    }
                }

                // Enhanced items -> wrap as VirtualSurvivalTool
                foreach (var item in potentialEnhancedItems)
                {
                    if (item == null || item.def == null) continue;

                    Thing spawnedProxyForReach = null;

                    if (item.Spawned)
                    {
                        spawnedProxyForReach = item;
                    }
                    else
                    {
                        // Minified inner thing?
                        if (item is MinifiedThing min && min.InnerThing != null)
                        {
                            spawnedProxyForReach = min.InnerThing;
                        }
                        else if (item.ParentHolder != null)
                        {
                            try
                            {
                                var ph = item.ParentHolder;
                                Thing ownerThing = null;

                                var ownerProp = ph.GetType().GetProperty("Owner");
                                if (ownerProp != null) ownerThing = ownerProp.GetValue(ph) as Thing;

                                if (ownerThing == null)
                                {
                                    var parentProp = ph.GetType().GetProperty("Parent");
                                    if (parentProp != null) ownerThing = parentProp.GetValue(ph) as Thing;
                                }

                                if (ownerThing == null)
                                {
                                    var ownerField =
                                        ph.GetType().GetField("owner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                                        ph.GetType().GetField("holdingOwner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    if (ownerField != null) ownerThing = ownerField.GetValue(ph) as Thing;
                                }

                                if (ownerThing != null)
                                    spawnedProxyForReach = ownerThing;
                            }
                            catch
                            {
                                // Reflection can fail on modded holders - treat as not spawned for reach
                                spawnedProxyForReach = null;
                            }
                        }
                    }

                    if (spawnedProxyForReach == null || !spawnedProxyForReach.Spawned)
                    {
                        CountReject(item.def, "Not spawned");
                        continue;
                    }

                    if (item.IsForbidden(pawn))
                    {
                        CountReject(item.def, "Forbidden");
                        continue;
                    }

                    if (!pawn.CanReach(spawnedProxyForReach, PathEndMode.OnCell, Danger.Deadly))
                    {
                        CountReject(item.def, "Can't reach");
                        continue;
                    }

                    var virtualTool = VirtualSurvivalTool.FromThing(item);
                    if (virtualTool == null) continue;

                    if (!IsViableCandidate(
                            virtualTool,
                            pawn,
                            assignment,
                            requiredStats,
                            out var reasonVirtual,
                            allowNonSpawnedVirtual: true,
                            spawnedProxyForReach: spawnedProxyForReach,
                            ignoreAssignmentForVirtual: true))
                    {
                        CountReject(item.def, reasonVirtual);
                        continue;
                    }

                    float vscore = virtualTool.WorkStatFactors?
                        .Where(m => requiredStats.Contains(m.stat))
                        .Sum(m => m.value) ?? 0f;

                    try { vscore -= 0.01f * spawnedProxyForReach.Position.DistanceTo(pawn.Position); } catch { }

                    if (vscore > bestScore)
                    {
                        bestScore = vscore;
                        bestTool = virtualTool;
                    }
                }

                // Dump aggregated rejections (debug only, limited)
                if (isDebug && rejectCounts.Count > 0 && SurvivalToolUtility.ShouldLogWithCooldown($"AutoTool_Rejections_{pawn.ThingID}"))
                {
                    foreach (var kv in rejectCounts.OrderByDescending(k => k.Value))
                    {
                        var split = kv.Key.Split(new[] { '|' }, 2);
                        var defName = split[0];
                        var reason = split.Length > 1 ? split[1] : "(unspecified)";
                        Log.Message($"[SurvivalTools.AutoTool] Rejecting ({kv.Value}) {defName}: {reason}");
                    }
                }

                if (isDebug && bestTool != null && SurvivalToolUtility.ShouldLogWithCooldown($"AutoTool_Best_{pawn.ThingID}"))
                {
                    var backing = SurvivalToolUtility.BackingThing(bestTool, pawn) ?? (bestTool as Thing);
                    var pos = backing?.Spawned == true ? backing.Position.ToString() : "unknown";
                    Log.Message($"[SurvivalTools.AutoTool] Found best tool: {bestTool.LabelCap} at {pos} with score {bestScore:F2}");
                }

                return bestTool;
            }
            catch (Exception ex)
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    Log.Error($"[SurvivalTools.AutoTool] Exception in FindBestHelpfulTool: {ex}");
                return null;
            }
        }

        #endregion

        #region Candidate viability

        private static bool IsViableCandidate(
            SurvivalTool tool,
            Pawn pawn,
            SurvivalToolAssignment assignment,
            List<StatDef> requiredStats,
            out string reason,
            bool allowNonSpawnedVirtual = false,
            Thing spawnedProxyForReach = null,
            bool ignoreAssignmentForVirtual = false)
        {
            reason = null;

            try
            {
                if (tool == null || pawn == null || pawn.Map == null)
                {
                    reason = "Invalid";
                    return false;
                }

                var backing = SurvivalToolUtility.BackingThing(tool, pawn);
                if (backing == null)
                {
                    reason = "No backing thing";
                    return false;
                }

                bool isVirtual = tool is VirtualSurvivalTool;

                // Single reference used for map / reach checks
                Thing mapCheck = null;
                if (backing.Spawned)
                {
                    mapCheck = backing;
                }
                else if (isVirtual && allowNonSpawnedVirtual && spawnedProxyForReach != null && spawnedProxyForReach.Spawned)
                {
                    mapCheck = spawnedProxyForReach;
                }
                else
                {
                    reason = "Not spawned";
                    return false;
                }

                // Map + bounds checks
                if (mapCheck.Map == null || pawn.Map == null || mapCheck.Map != pawn.Map)
                {
                    reason = "Not on this map";
                    return false;
                }

                var map = pawn.Map;
                var pos = mapCheck.Position;
                if (!pos.IsValid || !pos.InBounds(map))
                {
                    reason = "Out of bounds";
                    return false;
                }

                // Forbidden (allow special pacifist override)
                if (mapCheck.IsForbidden(pawn))
                {
                    if (SurvivalTools.Settings?.allowPacifistEquip == true &&
                        pawn.WorkTagIsDisabled(WorkTags.Violent) &&
                        tool.def.IsWeapon &&
                        IsOurPacifistTool(tool.def))
                    {
                        // permitted
                    }
                    else
                    {
                        reason = "Is forbidden";
                        return false;
                    }
                }

                // Assignment filter (optionally ignore for virtuals like Cloth)
                if (assignment?.filter != null && !(isVirtual && ignoreAssignmentForVirtual))
                {
                    // Use assignment.filter.Allows(backing) safely
                    try
                    {
                        if (!assignment.filter.Allows(backing))
                        {
                            reason = "Disallowed by assignment";
                            return false;
                        }
                    }
                    catch
                    {
                        reason = "Assignment filter error";
                        return false;
                    }
                }

                // Storage/policy
                if (!ToolIsAcquirableByPolicy(pawn, tool, mapCheck))
                {
                    reason = "Disallowed by storage policy";
                    return false;
                }

                // Must *improve* one of required stats (or be present in hardcore depending on rules)
                if (requiredStats == null || requiredStats.Count == 0 || !ToolImprovesAnyRequiredStat(tool, requiredStats))
                {
                    string statsNeeded = (requiredStats != null && requiredStats.Count > 0)
                        ? " (" + string.Join(", ", requiredStats.Select(s => s.defName)) + ")"
                        : "";
                    reason = "Does not improve required stat" + statsNeeded;
                    return false;
                }

                if (mapCheck.IsBurning())
                {
                    reason = "Is burning";
                    return false;
                }

                if (!pawn.CanReserveAndReach(mapCheck, PathEndMode.OnCell, pawn.NormalMaxDanger()))
                {
                    reason = "Cannot reserve or reach";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = $"ViabilityException: {ex.Message}";
                return false;
            }
        }

        private static bool IsOurPacifistTool(ThingDef def)
        {
            var ext = SurvivalToolProperties.For(def);
            return ext != null && ext != SurvivalToolProperties.defaultValues;
        }

        #endregion

        #region Job creation

        private static ThinkResult CreateToolPickupJobs(Pawn pawn, Job originalJob, SurvivalTool toolToGet, List<StatDef> requiredStats, ThinkNode sourceNode)
        {
            try
            {
                if (pawn == null || originalJob == null || toolToGet == null) return new ThinkResult(originalJob, sourceNode, JobTag.Misc, false);

                var jobQueue = pawn.jobs?.jobQueue;
                jobQueue?.EnqueueFirst(originalJob);

                Job pickupJob = null;

                if (toolToGet is VirtualSurvivalTool vtool)
                {
                    // Prefer a pawn-aware backing thing
                    var thingOnMap = SurvivalToolUtility.BackingThing(vtool, pawn);

                    // If backing not on pawn’s map, try any spawned stack of that def
                    if (thingOnMap == null || thingOnMap.Map != pawn.Map)
                    {
                        try
                        {
                            thingOnMap = pawn.Map?.listerThings.ThingsOfDef(vtool.SourceDef)
                                .FirstOrDefault(t => !t.IsForbidden(pawn) && pawn.CanReach(t, PathEndMode.OnCell, Danger.Deadly));
                        }
                        catch { thingOnMap = null; }
                    }

                    if (thingOnMap == null)
                    {
                        if (SurvivalToolUtility.IsDebugLoggingEnabled && SurvivalToolUtility.ShouldLogWithCooldown($"AutoTool_NoPhysical_{pawn.ThingID}"))
                            Log.Warning($"[SurvivalTools.AutoTool] Could not find physical stack of {vtool.SourceDef?.defName ?? "unknown"} to pick up.");
                        return new ThinkResult(originalJob, sourceNode, JobTag.Misc, false);
                    }

                    pickupJob = JobMaker.MakeJob(JobDefOf.TakeInventory, thingOnMap);
                    pickupJob.count = 1;
                }
                else
                {
                    pickupJob = JobMaker.MakeJob(JobDefOf.TakeInventory, toolToGet);
                    pickupJob.count = 1;
                }

                // Replace same-type with better
                var sameTypeTool = FindSameTypeHeldTool(pawn, toolToGet);
                if (sameTypeTool != null)
                {
                    float currentToolScore = SurvivalToolScore(sameTypeTool, pawn, requiredStats);
                    float newToolScore = SurvivalToolScore(toolToGet, pawn, requiredStats);

                    if (newToolScore > currentToolScore)
                    {
                        if (SurvivalToolUtility.IsDebugLoggingEnabled && SurvivalToolUtility.ShouldLogWithCooldown($"AutoTool_Replace_{pawn.ThingID}"))
                            Log.Message($"[SurvivalTools.AutoTool] {pawn.LabelShort} will replace {sameTypeTool.LabelShort} (score: {currentToolScore:F2}) with better {toolToGet.LabelShort} (score: {newToolScore:F2}).");

                        jobQueue?.EnqueueFirst(pickupJob);
                        var dropJob = pawn.DequipAndTryStoreSurvivalTool(sameTypeTool, false);
                        return new ThinkResult(dropJob, sourceNode, JobTag.Misc, false);
                    }

                    if (SurvivalToolUtility.IsDebugLoggingEnabled && SurvivalToolUtility.ShouldLogWithCooldown($"AutoTool_SkipBetter_{pawn.ThingID}"))
                        Log.Message($"[SurvivalTools.AutoTool] {pawn.LabelShort} already has better tool of same type. Skipping pickup.");
                    return new ThinkResult(originalJob, sourceNode, JobTag.Misc, false);
                }

                // Make space if needed
                if (!pawn.CanCarryAnyMoreSurvivalTools())
                {
                    var toolToDrop = FindDroppableHeldTool(pawn, requiredStats, toolToGet);
                    if (toolToDrop != null)
                    {
                        if (SurvivalToolUtility.IsDebugLoggingEnabled && SurvivalToolUtility.ShouldLogWithCooldown($"AutoTool_Drop_{pawn.ThingID}"))
                            Log.Message($"[SurvivalTools.AutoTool] {pawn.LabelShort} will drop {toolToDrop.LabelShort} to pick up {toolToGet.LabelShort}.");

                        jobQueue?.EnqueueFirst(pickupJob);
                        var dropJob = pawn.DequipAndTryStoreSurvivalTool(toolToDrop, false);
                        return new ThinkResult(dropJob, sourceNode, JobTag.Misc, false);
                    }
                }

                return new ThinkResult(pickupJob, sourceNode, JobTag.Misc, false);
            }
            catch (Exception ex)
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    Log.Error($"[SurvivalTools.AutoTool] Exception creating pickup jobs: {ex}");
                return new ThinkResult(originalJob, sourceNode, JobTag.Misc, false);
            }
        }

        #endregion

        #region Tool selection helpers

        private static SurvivalTool FindDroppableHeldTool(Pawn pawn, List<StatDef> requiredStats, SurvivalTool toolWeWant)
        {
            try
            {
                var tracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
                var droppableTools = pawn.GetHeldSurvivalTools()
                    .OfType<SurvivalTool>()
                    .Where(t => tracker?.forcedHandler?.AllowedToAutomaticallyDrop(t) ?? true)
                    .ToList();

                if (droppableTools.NullOrEmpty()) return null;

                // Prefer dropping something irrelevant to the current job
                var irrelevantTool = droppableTools.FirstOrDefault(t => !ToolImprovesAnyRequiredStat(t, requiredStats));
                if (irrelevantTool != null)
                {
                    if (SurvivalToolUtility.IsDebugLoggingEnabled && SurvivalToolUtility.ShouldLogWithCooldown($"AutoTool_DropIrr_{pawn.ThingID}"))
                        Log.Message($"[SurvivalTools.AutoTool] {pawn.LabelShort} will drop irrelevant tool {irrelevantTool.LabelShort} for current job.");
                    return irrelevantTool;
                }

                // Otherwise drop the worst tool (only if the desired is better)
                float wantScore = SurvivalToolScore(toolWeWant, pawn, requiredStats);
                var worstTool = droppableTools
                    .Select(t => new { Tool = t, Score = SurvivalToolScore(t, pawn, requiredStats) })
                    .Where(x => x.Score < wantScore)
                    .OrderBy(x => x.Score)
                    .FirstOrDefault()?.Tool;

                if (worstTool != null && SurvivalToolUtility.IsDebugLoggingEnabled && SurvivalToolUtility.ShouldLogWithCooldown($"AutoTool_DropWorst_{pawn.ThingID}"))
                    Log.Message($"[SurvivalTools.AutoTool] {pawn.LabelShort} will drop lower-scoring tool {worstTool.LabelShort} for current job.");

                return worstTool;
            }
            catch (Exception ex)
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                    Log.Error($"[SurvivalTools.AutoTool] Exception in FindDroppableHeldTool: {ex}");
                return null;
            }
        }

        private static bool ToolImprovesAnyRequiredStat(SurvivalTool tool, List<StatDef> requiredStats)
        {
            requiredStats = requiredStats ?? new List<StatDef>();

            var workStatFactors = GetEffectiveWorkStatFactors(tool)?.ToList() ?? new List<StatModifier>();

            // Medical group
            var medicalStats = new[] { ST_StatDefOf.MedicalOperationSpeed, ST_StatDefOf.MedicalSurgerySuccessChance };
            if (requiredStats.Any(s => medicalStats.Contains(s)))
            {
                bool hasMedical = workStatFactors.Any(m => medicalStats.Contains(m.stat));
                if (hasMedical)
                {
                    if (SurvivalToolUtility.IsHardcoreModeEnabled) return true;
                    return workStatFactors.Any(m => medicalStats.Contains(m.stat) && m.value > 1.0f);
                }
            }

            // Butchery group
            var butcheryStats = new[] { ST_StatDefOf.ButcheryFleshSpeed, ST_StatDefOf.ButcheryFleshEfficiency };
            if (requiredStats.Any(s => butcheryStats.Contains(s)))
            {
                bool hasButchery = workStatFactors.Any(m => butcheryStats.Contains(m.stat));
                if (hasButchery)
                {
                    if (SurvivalToolUtility.IsHardcoreModeEnabled) return true;
                    return workStatFactors.Any(m => butcheryStats.Contains(m.stat) && m.value > 1.0f);
                }
            }

            // Cleaning is always optional; only “useful” if it actually improves
            if (requiredStats.Contains(ST_StatDefOf.CleaningSpeed))
            {
                bool hasCleaning = workStatFactors.Any(m => m.stat == ST_StatDefOf.CleaningSpeed);
                if (hasCleaning)
                    return workStatFactors.Any(m => m.stat == ST_StatDefOf.CleaningSpeed && m.value > 1.0f);
            }

            // Everything else
            var otherStats = requiredStats
                .Except(medicalStats)
                .Except(butcheryStats)
                .Where(s => s != ST_StatDefOf.CleaningSpeed)
                .ToList();

            if (otherStats.Any())
            {
                bool hasRequired = otherStats.Any(stat => workStatFactors.Any(m => m.stat == stat));
                if (hasRequired && SurvivalToolUtility.IsHardcoreModeEnabled)
                    return true; // In hardcore, presence is enough to unblock
                return otherStats.Any(stat => workStatFactors.Any(m => m.stat == stat && m.value > 1.0f));
            }

            return false;
        }

        private static IEnumerable<StatModifier> GetEffectiveWorkStatFactors(SurvivalTool tool)
        {
            var list = new List<StatModifier>();
            if (tool?.WorkStatFactors != null) list.AddRange(tool.WorkStatFactors);

            ThingDef sourceDef = null;
            if (tool is VirtualSurvivalTool v)
            {
                sourceDef = v.SourceDef;
                if (sourceDef == null)
                {
                    var backing = SurvivalToolUtility.BackingThing(tool, null);
                    if (backing != null) sourceDef = backing.def;
                }
            }

            if (sourceDef == null) sourceDef = tool?.def;

            var ext = sourceDef?.GetModExtension<SurvivalToolProperties>();
            if (ext?.baseWorkStatFactors != null)
            {
                foreach (var m in ext.baseWorkStatFactors)
                {
                    if (m?.stat == null) continue;
                    if (!list.Any(x => x.stat == m.stat))
                        list.Add(m);
                }
            }

            return list;
        }

        private static float SurvivalToolScore(SurvivalTool tool, Pawn pawn, List<StatDef> workRelevantStats)
        {
            float optimality = 0f;
            var workStatFactors = (tool?.WorkStatFactors != null) ? tool.WorkStatFactors.ToList() : new List<StatModifier>();
            bool hasAnyRequiredStat = false;

            foreach (var stat in workRelevantStats ?? Enumerable.Empty<StatDef>())
            {
                var modifier = workStatFactors.FirstOrDefault(m => m.stat == stat);
                if (modifier != null)
                {
                    if (SurvivalToolUtility.IsHardcoreModeEnabled && !IsMultitool(tool))
                    {
                        // Example rule: only sickles provide PlantHarvestingSpeed in hardcore mode
                        if (stat == ST_StatDefOf.PlantHarvestingSpeed &&
                            ToolUtility.ToolKindOf(tool) != STToolKind.Sickle)
                        {
                            continue;
                        }
                    }

                    hasAnyRequiredStat = true;
                    optimality += modifier.value;
                }
            }

            if (SurvivalToolUtility.IsHardcoreModeEnabled && hasAnyRequiredStat && optimality <= 0f)
                optimality = 0.1f;

            if (tool?.def?.useHitPoints == true)
            {
                try
                {
                    var backing = SurvivalToolUtility.BackingThing(tool, pawn);
                    ThingWithComps twc = backing as ThingWithComps;
                    if (twc != null)
                    {
                        float hpFrac = twc.MaxHitPoints > 0 ? (float)twc.HitPoints / twc.MaxHitPoints : 0f;
                        float lifespanRemaining = twc.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan) * hpFrac;
                        optimality *= LifespanDaysToOptimalityMultiplierCurve.Evaluate(lifespanRemaining);
                    }
                    else if (tool is SurvivalTool realTool)
                    {
                        float hpFrac = realTool.MaxHitPoints > 0 ? (float)realTool.HitPoints / realTool.MaxHitPoints : 0f;
                        float lifespanRemaining = realTool.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan) * hpFrac;
                        optimality *= LifespanDaysToOptimalityMultiplierCurve.Evaluate(lifespanRemaining);
                    }
                }
                catch { /* swallow stat retrieval errors */ }
            }

            return optimality;
        }

        private static SurvivalTool FindSameTypeHeldTool(Pawn pawn, SurvivalTool targetTool)
        {
            if (pawn == null || targetTool == null) return null;

            return pawn.GetHeldSurvivalTools()
                .OfType<SurvivalTool>()
                .FirstOrDefault(heldTool => AreSameToolType(heldTool, targetTool));
        }

        private static bool AreSameToolType(SurvivalTool tool1, SurvivalTool tool2)
        {
            var s1 = tool1?.WorkStatFactors?.Select(f => f.stat).ToHashSet() ?? new HashSet<StatDef>();
            var s2 = tool2?.WorkStatFactors?.Select(f => f.stat).ToHashSet() ?? new HashSet<StatDef>();
            if (s1.Count == 0 || s2.Count == 0) return false;
            return s1.SetEquals(s2);
        }

        private static bool IsMultitool(SurvivalTool tool)
        {
            if (tool?.def == null) return false;
            if (tool.def == ST_ThingDefOf.SurvivalTools_Multitool) return true;

            var defName = tool.def.defName?.ToLowerInvariant() ?? string.Empty;
            if (defName.Contains("multitool") || defName.Contains("omni") || defName.Contains("universal")) return true;

            return (tool.WorkStatFactors?.Count() ?? 0) >= 3;
        }

        #endregion

        #region Policy & settings helpers

        // Legacy forwarder for older call sites
        private static bool ToolIsAcquirableByPolicy(Pawn pawn, SurvivalTool tool)
        {
            var backing = SurvivalToolUtility.BackingThing(tool, pawn);
            return ToolIsAcquirableByPolicy(pawn, tool, backing);
        }

        // Safe home-area check using the same spawned map thing used for reach checks
        private static bool ToolIsAcquirableByPolicy(Pawn pawn, SurvivalTool tool, Thing forMapChecks)
        {
            try
            {
                if (pawn == null || pawn.Map == null || tool == null) return false;

                var backing = SurvivalToolUtility.BackingThing(tool, pawn) ?? (tool as Thing);
                if (backing != null && backing.IsInAnyStorage())
                    return true;

                if (SurvivalTools.Settings?.pickupFromStorageOnly == true)
                    return false;

                var mapThing = forMapChecks ?? backing;
                if (mapThing == null || !mapThing.Spawned || mapThing.Map != pawn.Map)
                    return false;

                var map = pawn.Map;
                var pos = mapThing.Position;
                if (!pos.IsValid || !pos.InBounds(map))
                    return false;

                var home = map.areaManager?.Home;
                if (home == null) return false;

                // Safe index access; we've already checked InBounds
                try
                {
                    return home[pos];
                }
                catch
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldBlockJobForMissingStat(StatDef stat)
        {
            var settings = SurvivalTools.Settings;
            if (settings == null) return false;

            bool isOptionalStat = IsOptionalStat(stat);
            if (!isOptionalStat) return true;

            if (settings.extraHardcoreMode && settings.IsStatRequiredInExtraHardcore(stat))
                return true;

            return false;
        }

        private static bool IsOptionalStat(StatDef stat)
        {
            return stat == ST_StatDefOf.CleaningSpeed ||
                   stat == ST_StatDefOf.ButcheryFleshSpeed ||
                   stat == ST_StatDefOf.ButcheryFleshEfficiency ||
                   stat == ST_StatDefOf.MedicalOperationSpeed ||
                   stat == ST_StatDefOf.MedicalSurgerySuccessChance;
        }

        private static readonly SimpleCurve LifespanDaysToOptimalityMultiplierCurve = new SimpleCurve
        {
            new CurvePoint(0f,   0.04f),
            new CurvePoint(0.5f, 0.20f),
            new CurvePoint(1f,   0.50f),
            new CurvePoint(2f,   1.00f),
            new CurvePoint(4f,   1.00f),
            new CurvePoint(999f, 1.00f)
        };

        #endregion
    }
}