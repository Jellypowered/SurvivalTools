// RimWorld 1.6 / C# 7.3
// Source/Assign/PreWork_AutoEquip.cs
//
// Phase 6: Pre-work auto-equip integration
// - Harmony prefix for Pawn_JobTracker.TryTakeOrderedJob
// - Provides seamless tool upgrading before work begins
// - Settings-driven behavior with performance safeguards

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using SurvivalTools.Gating;
using SurvivalTools.Scoring;
using SurvivalTools.Helpers;
using SurvivalTools.Assign; // NightmareCarryEnforcer
using SurvivalTools.Compat; // CompatAPI research stat
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Assign
{
    [HarmonyPatch]
    public static class PreWork_AutoEquip
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_JobTracker), "pawn");
        // Track pending WorkGiver tool checks between prefix and postfix
        private static readonly System.Collections.Generic.Dictionary<int, StatDef> _wgPendingStat = new System.Collections.Generic.Dictionary<int, StatDef>(64);
        private static readonly System.Collections.Generic.Dictionary<int, int> _nmToken = new System.Collections.Generic.Dictionary<int, int>(64); // pawnId -> cooldown tick
        private static readonly System.Collections.Generic.Dictionary<int, int> _nmLogCooldown = new System.Collections.Generic.Dictionary<int, int>(128); // pawnId -> nextTick for instrumentation

        // Throttle repeated PreWork cancels/logs per pawn+job+target+stat to prevent micro-stutter
        private static readonly System.Collections.Generic.Dictionary<int, int> _preworkCancelCD = new System.Collections.Generic.Dictionary<int, int>(128);
        private const int PREWORK_CANCEL_COOLDOWN_TICKS = 900; // ~15s default

        // (removed unused _patchApplied flag)

        /// <summary>
        /// Static constructor to verify patch application
        /// </summary>
        static PreWork_AutoEquip()
        {
            try
            {
                Log.Warning("[SurvivalTools.PreWork] PreWork_AutoEquip static constructor called - class is being loaded");
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools.PreWork] Static constructor failed: {ex}");
            }
        }

        // ---------------- Throttle Helpers for Cancel Operations ----------------

        /// <summary>
        /// Generate hash key for throttle cache: pawn + job + target + stat
        /// </summary>
        private static int HashThrottleKey(Pawn p, Job j, StatDef stat)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (p?.thingIDNumber ?? 0);
                h = h * 31 + (j?.def?.shortHash ?? 0);
                // Include main target identity to avoid spamming when switching targets
                var tgt = j?.GetTarget(TargetIndex.A);
                if (tgt.HasValue)
                {
                    if (tgt.Value.Thing != null)
                        h = h * 31 + tgt.Value.Thing.thingIDNumber;
                    else
                        h = h * 31 + tgt.Value.Cell.GetHashCode();
                }
                h = h * 31 + (stat?.shortHash ?? 0);
                return h;
            }
        }

        /// <summary>
        /// Check if a cancel operation is within cooldown period
        /// </summary>
        private static bool IsOnCancelCooldown(int key, int now)
        {
            return _preworkCancelCD.TryGetValue(key, out var until) && now < until;
        }

        /// <summary>
        /// Arm the cancel cooldown for a specific key
        /// </summary>
        private static void ArmCancelCooldown(int key, int now, int durationTicks)
        {
            _preworkCancelCD[key] = now + durationTicks;
        }

        // ---------------- Shared Guards & Helpers ----------------
        // Replaced by shared helper PawnEligibility.IsEligibleColonistHuman

        // Fast filter: if a job has no required/optional tool stats, skip all ST logic.
        private static bool JobUsesTools(Pawn pawn, Job job)
        {
            if (job == null) return false;
            var jd = job.def;
            if (jd == null) return false;
            // Explicit exclusions (tool-less utility jobs)
            if (jd == JobDefOf.Ingest || jd == JobDefOf.LayDown || jd == JobDefOf.Wait ||
                jd == JobDefOf.Wait_MaintainPosture || jd == JobDefOf.Goto || jd == JobDefOf.GotoWander)
                return false;
            // Explicit inclusion: research (not normally covered by generic RelevantStats resolver in some setups)
            if (jd == JobDefOf.Research || string.Equals(jd.defName, "Research", StringComparison.Ordinal)) return true;
            try
            {
                // Use helper to resolve associated WorkGiver (best-effort) then unified relevant stat resolver.
                var wg = SurvivalTools.Helpers.JobDefToWorkGiverDefHelper.GetWorkGiverDefForJob(jd);
                var statsFromJob = SurvivalToolUtility.RelevantStatsFor(wg, job) ?? new System.Collections.Generic.List<StatDef>();
                if (statsFromJob.Count > 0) return true;
                // Fallback: jobDef based (covers patterns where job instance targets differ)
                var statsFromJobDef = SurvivalToolUtility.RelevantStatsFor(wg, jd) ?? new System.Collections.Generic.List<StatDef>();
                // If still empty and this is research, force true (ensures pre-work logic runs)
                if (statsFromJobDef.Count == 0 && (jd == JobDefOf.Research || string.Equals(jd.defName, "Research", StringComparison.Ordinal))) return true;
                return statsFromJobDef.Count > 0;
            }
            catch { return true; } // fail open to avoid accidental suppression
        }

        // Reentrancy / churn gate
        private static readonly System.Collections.Generic.HashSet<int> _preworkActive = new System.Collections.Generic.HashSet<int>();
        private static readonly System.Collections.Generic.Dictionary<int, int> _lastStartTick = new System.Collections.Generic.Dictionary<int, int>();
        private static bool EnterPreworkGate(Pawn p, Job j)
        {
            if (p == null) return false;
            int id = p.thingIDNumber;
            if (!_preworkActive.Add(id)) return false; // already inside
            try
            {
                int cur = Find.TickManager?.TicksGame ?? 0;
                if (_lastStartTick.TryGetValue(id, out var last) && (cur - last) < 30) // ~0.5s
                {
                    _preworkActive.Remove(id);
                    return false;
                }
                _lastStartTick[id] = cur;
                return true;
            }
            catch { _preworkActive.Remove(id); return false; }
        }
        private static void ExitPreworkGate(Pawn p) { if (p != null) _preworkActive.Remove(p.thingIDNumber); }

        /// <summary>
        /// Harmony prefix for Pawn_JobTracker.TryTakeOrderedJob.
        /// Checks if job requires tools and attempts auto-equip if beneficial.
        /// </summary>
        // Single real method in 1.6: TryTakeOrderedJob(Job, JobTag?, bool requestQueueing)
        [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob), new[] { typeof(Job), typeof(JobTag?), typeof(bool) })]
        [HarmonyPriority(800)]
        private static class TryTakeOrderedJob_Prefix_3
        {
            static bool Prefix(Pawn_JobTracker __instance, Job job, JobTag? tag, bool requestQueueing)
            {
                try { return TryPreWorkAutoEquip(__instance, job, tag, requestQueueing); }
                catch (Exception ex) { LogError($"Exception in PreWork_AutoEquip.TryTakeOrderedJob Prefix: {ex}"); return true; }
            }
        }

        [HarmonyPriority(800)]
        private static bool StartJob_Prefix(
            Pawn_JobTracker __instance,
            Job newJob,
            JobCondition lastJobEndCondition,
            ThinkNode jobGiver,
            bool resumeCurJobAfterwards,
            bool cancelBusyStances,
            ThinkTreeDef thinkTree,
            JobTag? tag,
            bool fromQueue,
            bool canReturnCurJobToPool,
            bool? keepCarryingThingOverride,
            bool continueSleeping,
            bool addToJobsThisTick,
            bool preToilReservationsCanFail)
        {
            try { return TryPreWorkAutoEquip(__instance, newJob, aiPath: true); }
            catch (Exception e)
            {
                ST_Logging.LogWarning($"[SurvivalTools.PreWork] StartJob prefix error: {e}");
                return true;
            }
        }

        /// <summary>
        /// Reflection based hook installer for AI job start path. Called once from central Harmony bootstrap.
        /// Tries both method name variants (TryStartJob / StartJob) and multiple signatures. First hit wins.
        /// Safe no‑op on failure (dev log only) so a signature drift never hard breaks game start.
        /// </summary>
        private static MethodInfo GetStartJobExact()
        {
            var t = typeof(Pawn_JobTracker);
            var types = new Type[] {
                typeof(Job),
                typeof(JobCondition),
                typeof(ThinkNode),
                typeof(bool),
                typeof(bool),
                typeof(ThinkTreeDef),
                typeof(JobTag?),
                typeof(bool),
                typeof(bool),
                typeof(bool?),
                typeof(bool),
                typeof(bool),
                typeof(bool)
            };
            return AccessTools.Method(t, "StartJob", types);
        }

        public static void ApplyStartJobHook(HarmonyLib.Harmony h)
        {
            if (h == null) return;
            try
            {
                var exact = GetStartJobExact();
                var prefix = new HarmonyMethod(typeof(PreWork_AutoEquip).GetMethod("StartJob_Prefix", BindingFlags.Static | BindingFlags.NonPublic));
                if (exact != null)
                {
                    h.Patch(exact, prefix: prefix);
                    ST_Logging.LogInfo("[SurvivalTools.Harmony] PreWork_AutoEquip AI StartJob prefix applied (exact).");
                    Log.Message("[PreWork] StartJob prefix: OK");
                    return;
                }

                // Fallback: scan for any StartJob-like method whose first arg is Job
                var t = typeof(Pawn_JobTracker);
                var mis = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo candidate = null;
                for (int i = 0; i < mis.Length; i++)
                {
                    var m = mis[i];
                    if (!m.Name.Contains("StartJob")) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 0) continue;
                    if (ps[0].ParameterType != typeof(Job)) continue;
                    candidate = m; break;
                }
                if (candidate != null)
                {
                    h.Patch(candidate, prefix: prefix);
                    ST_Logging.LogInfo("[SurvivalTools.Harmony] PreWork_AutoEquip AI StartJob prefix applied (fallback).");
                    Log.Message("[PreWork] StartJob prefix: OK (fallback)");
                }
                else
                {
                    ST_Logging.LogDebug("[SurvivalTools.PreWork] StartJob hook not applied — no suitable StartJob found.", "StartJobHook.Missing");
                }
            }
            catch (Exception ex)
            {
                ST_Logging.LogWarning("[SurvivalTools.PreWork] StartJob hook application failed: " + ex);
            }
        }

        // Central unified logic for both ordered & AI job starts
        // Ordered central handler (real signature path)
        internal static bool TryPreWorkAutoEquip(Pawn_JobTracker tracker, Job job, JobTag? tag, bool requestQueueing)
        {
            if (tracker == null || job == null) return true;
            Pawn pawn = null; try { pawn = (Pawn)PawnField.GetValue(tracker); } catch { }
            // STC authority: block any attempt to start SurvivalTools FellTree jobs
            try
            {
                if (job.def == ST_JobDefOf.FellTree || job.def == ST_JobDefOf.FellTreeDesignated)
                {
                    if (Helpers.TreeSystemArbiterActiveHelper.IsSTCAuthorityActive())
                    {
                        if (Prefs.DevMode && SurvivalToolsMod.Settings?.debugLogging == true)
                        {
                            int now = Find.TickManager?.TicksGame ?? 0;
                            const int cd = 120; // 2s cooldown
                            if (!_nmLogCooldown.TryGetValue(pawn?.thingIDNumber ?? -1, out var nxt) || now >= nxt)
                            {
                                _nmLogCooldown[pawn?.thingIDNumber ?? -1] = now + cd;
                                Log.Message("[ST×STC] Suppressed FellTree TryTakeOrderedJob while STC active");
                            }
                        }
                        return false;
                    }
                }
            }
            catch { }
            // Hard gate: only player humanlikes & tool-using jobs
            if (!SurvivalTools.Helpers.PawnEligibility.IsEligibleColonistHuman(pawn) || !JobUsesTools(pawn, job)) return true;
            if (!EnterPreworkGate(pawn, job)) return true;
            try
            {
                if (Prefs.DevMode)
                    Log.Message($"[PreWork.Enter] pawn={pawn?.LabelShort ?? "?"} job={job?.def?.defName ?? "?"} forced={job?.playerForced} queuedReq={requestQueueing}");
            }
            catch { }
            // EARLY ORDERED FENCE (Nightmare) – run before any upgrade logic to avoid temporary dual-carry window.
            try
            {
                var settings = SurvivalToolsMod.Settings;
                if (settings?.extraHardcoreMode == true && pawn != null && job.playerForced)
                {
                    int allowed = AssignmentSearch.GetEffectiveCarryLimit(pawn, settings);
                    int carried = NightmareCarryEnforcer.CountCarried(pawn);
                    if (carried > allowed)
                    {
                        // Enforce immediately (no keeper hint – we'll re-evaluate after drops before upgrade)
                        int enq = NightmareCarryEnforcer.EnforceNow(pawn, keeperOrNull: null, allowed: allowed, reason: "ordered-early");
                        bool ok = NightmareCarryEnforcer.IsCompliant(pawn, null, allowed);
                        ST_Logging.LogDebug($"[NightmareCarry][ordered-early] pawn={pawn.LabelShort} job={job.def?.defName} carried={carried} allowed={allowed} enq={enq} ok={ok}",
                            $"NightmareOrderedEarly|{pawn.thingIDNumber}");
                        if (!ok)
                        {
                            // Requeue original job so it resumes after drops resolve
                            try { var clone = JobUtils.CloneJobForQueue(job); tracker.jobQueue?.EnqueueFirst(clone, JobTag.Misc); } catch { }
                            return false; // block until compliant
                        }
                    }
                }
            }
            catch (Exception eoEx) { ST_Logging.LogWarning("[SurvivalTools.PreWork] Early ordered fence exception: " + eoEx.Message); }
            bool ret;
            try { ret = TryPreWorkAutoEquip(tracker, job, aiPath: false); }
            finally { ExitPreworkGate(pawn); }
            if (ret)
            {
                // Only log the hook once (first successful invocation) for visibility
                if (!_loggedOrderedHook)
                {
                    _loggedOrderedHook = true;
                    Log.Message("[PreWork] TryTakeOrderedJob prefix: OK");
                }
            }
            return ret;
        }

        private static bool _loggedOrderedHook = false;

        internal static bool TryPreWorkAutoEquip(Pawn_JobTracker tracker, Job job, bool aiPath, JobTag? tag = null, bool draftForced = false, bool hasDraftForced = false)
        {
            if (tracker == null || job?.def == null) return true;
            var pawn = (Pawn)PawnField.GetValue(tracker);
            if (pawn == null) return true;
            if (!SurvivalTools.Helpers.PawnEligibility.IsEligibleColonistHuman(pawn) || !JobUsesTools(pawn, job)) return true; // early skip
            if (pawn.CurJobDef == JobDefOf.Ingest) return true; // do not manage while eating

            // Cache settings once at method entry for performance
            var settings = SurvivalToolsMod.Settings;

            // AI path logging already covered elsewhere; ordered overloads log before delegating
            // Unified path: determine relevant stat (may be null – still enforce carry), attempt upgrade, then strict carry enforcement.
            var workStat = GetRelevantWorkStat(job); // may be null for non‑supported jobs (still enforce carry limit)

            // Phase 12: Battery auto-swap check (before upgrade attempts)
            if (workStat != null && settings?.autoSwapBatteries == true && GetEnableAssignments(settings))
            {
                bool swapped = TryAutoSwapBattery(pawn, workStat, settings);
                if (swapped)
                {
                    // Battery swap queued, requeue original job and exit
                    try
                    {
                        if (JobUtils.IsJobStillValid(job, pawn))
                        {
                            var cloned = JobUtils.CloneJobForQueue(job);
                            pawn.jobs?.jobQueue?.EnqueueFirst(cloned, JobTag.Misc);
                        }
                    }
                    catch (Exception e) { LogError($"[SurvivalTools.PreWork] Battery swap requeue exception: {e}"); }
                    return false;
                }
            }

            // Attempt upgrade only if job has a relevant stat and assignments enabled
            Thing pendingEquip = null;
            bool upgraded = false;
            if (workStat != null && GetEnableAssignments(settings) && pawn.IsColonist && pawn.Awake() && !JobUtils.IsToolManagementJob(job))
            {
                if (pawn.CurJobDef == JobDefOf.Ingest) return true; // ingest protection
                upgraded = TryUpgradeForWork(pawn, workStat, job, settings, AssignmentSearch.QueuePriority.Front);
                if (upgraded)
                {
                    // the queued equip job's TargetA will be the tool; we resolve keeper below after equip completes, for now treat none
                    try { pendingEquip = job.targetA.Thing; } catch { pendingEquip = null; }
                }
            }

            // EARLY GATING ENFORCEMENT (before carry enforcement): if core work stat and not upgraded and pawn lacks required tool, cancel.
            // Phase 11.13: Expanded to include all non-optional work stats (research, mining, harvesting, medical, maintenance).
            // Optional stats (CleaningSpeed) deliberately excluded - only gated in Extra Hardcore mode via separate logic.
            // THROTTLED to prevent micro-stutter from repeated log spam.
            try
            {
                if (!upgraded && workStat != null && settings != null && IsGateableWorkStat(workStat))
                {
                    bool shouldGate = StatGatingHelper.ShouldBlockJobForStat(workStat, settings, pawn);
                    if (shouldGate && !pawn.HasSurvivalToolFor(workStat))
                    {
                        // Cache tick access for hot path performance
                        int now = Find.TickManager?.TicksGame ?? 0;
                        int throttleKey = HashThrottleKey(pawn, job, workStat);

                        // Check if rescue is already queued (reuse existing logic)
                        bool rescueAlreadyQueued = false;
                        try
                        {
                            var curJob = pawn?.jobs?.curJob;
                            var curDef = curJob?.def;
                            rescueAlreadyQueued = (curDef == JobDefOf.Equip || curDef == JobDefOf.TakeInventory);
                        }
                        catch { }

                        // Bypass throttle for player-forced jobs (right-click immediate) or when rescue is queued
                        bool bypassThrottle = job.playerForced || rescueAlreadyQueued;

                        if (!bypassThrottle && IsOnCancelCooldown(throttleKey, now))
                        {
                            // Fast-fail: still block the job, but skip heavy work and logging
                            Gating.GatingEnforcer.CancelCurrentJob(pawn, job, Gating.ST_CancelReason.ST_Gate_MissingToolStat);
                            return false;
                        }

                        // Arm cooldown for next window (only when we're about to do heavy work/logging)
                        if (!bypassThrottle)
                        {
                            ArmCancelCooldown(throttleKey, now, PREWORK_CANCEL_COOLDOWN_TICKS);
                        }

                        // Log only when arming cooldown (at most once per window) AND in DevMode + debugLogging
                        if (Prefs.DevMode && settings.debugLogging && !bypassThrottle)
                        {
                            string kind = GetWorkKindLabel(workStat, job);
                            Log.Message($"[PreWork] Cancel {kind}: missing {workStat.defName} tool and no rescue queued (pawn={pawn.LabelShort})");
                        }

                        Gating.GatingEnforcer.CancelCurrentJob(pawn, job, Gating.ST_CancelReason.ST_Gate_MissingToolStat);
                        return false;
                    }
                }
            }
            catch { }

            // Nightmare strict carry enforcement (blocks work until physically compliant)
            if (!EnforceCarryOrBlock(tracker, job, pawn, workStat, pendingEquip)) return false;

            // If we queued an upgrade, we requeue original job and exit so equip executes first
            if (upgraded)
            {
                try
                {
                    if (JobUtils.IsJobStillValid(job, pawn))
                    {
                        var cloned = JobUtils.CloneJobForQueue(job);
                        pawn.jobs?.jobQueue?.EnqueueFirst(cloned, JobTag.Misc);
                    }
                }
                catch (Exception e) { LogError($"[SurvivalTools.PreWork] Requeue exception: {e}"); }
                return false;
            }

            return true;
        }

        // Single consolidated Nightmare enforcement helper (strict) used by both ordered and AI job starts
        private static bool EnforceCarryOrBlock(Pawn_JobTracker jt, Job newJob, Pawn pawn, StatDef focusStat, Thing pendingEquipOrNull)
        {
            try
            {
                var st = SurvivalToolsMod.Settings;
                if (st == null) return true;
                if (!st.extraHardcoreMode) return true; // not in Nightmare
                if (pawn.CurJobDef == JobDefOf.Ingest) return true; // skip during ingest
                // Toolbelt exemption placeholder intentionally omitted (stub returns false elsewhere)
                if (JobUtils.IsToolManagementJob(newJob)) return true; // don't block management jobs

                int allowed = AssignmentSearch.GetEffectiveCarryLimit(pawn, st);
                // Unified keeper selection via enforcer helper (resolver-aligned)
                Thing keeper = pendingEquipOrNull ?? NightmareCarryEnforcer.SelectKeeperForJob(pawn, focusStat);
                int enq = NightmareCarryEnforcer.EnforceNow(pawn, keeper, allowed, "pre-work");
                bool ok = NightmareCarryEnforcer.IsCompliant(pawn, keeper, allowed);
                int carriedNow = NightmareCarryEnforcer.CountCarried(pawn);
                // Local 200‑tick instrumentation cooldown (cache tick access)
                int nowTick = Find.TickManager?.TicksGame ?? 0;
                int pid = pawn.thingIDNumber;
                if (!_nmLogCooldown.TryGetValue(pid, out var until) || nowTick >= until)
                {
                    _nmLogCooldown[pid] = nowTick + 200;
                    if (Prefs.DevMode)
                    {
                        LogInfo($"[PreWork] pawn={pawn.LabelShort} job={newJob?.def?.defName} carried={carriedNow} allowed={allowed} enq={enq} compliant={ok} keeper={(keeper != null ? keeper.LabelShort : "(auto)")} (playerForced={newJob?.playerForced == true})");
                    }
                }
                if (!ok)
                {
                    RequeueOriginalJobFirst(jt, newJob);
                    // Extra one-off log when blocking a forced job (no cooldown); still cheap
                    if (newJob?.playerForced == true)
                    {
                        if (Prefs.DevMode)
                            LogInfo($"[PreWork] BLOCK (Nightmare) forced job for {pawn.LabelShort} until drops resolve");
                    }
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                LogWarning($"[SurvivalTools.Nightmare] EnforceCarryOrBlock exception: {ex}");
                return true; // fail open
            }
        }

        private static void RequeueOriginalJobFirst(Pawn_JobTracker jt, Job job)
        {
            try
            {
                if (jt == null || job == null) return;
                var pawn = (Pawn)PawnField.GetValue(jt);
                if (pawn == null) return;
                if (!JobUtils.IsJobStillValid(job, pawn)) return;
                var cloned = JobUtils.CloneJobForQueue(job);
                jt.jobQueue?.EnqueueFirst(cloned, JobTag.Misc);
            }
            catch { }
        }

        // Postfix on successfully completed Equip jobs to enforce Nightmare (and general) carry limit immediately
        [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.EndJobWith))]
        [HarmonyPostfix]
        private static void JobDriver_EndJobWith_Postfix(JobDriver __instance, JobCondition condition)
        {
            try
            {
                if (condition != JobCondition.Succeeded) return;
                var job = __instance?.job; var pawn = __instance?.pawn;
                if (job == null || pawn == null) return;
                if (job.def == JobDefOf.Equip || job.def == JobDefOf.TakeInventory)
                {
                    // Protect the just equipped thing (targetA) from immediate drop; pass backing thing
                    var tool = job.targetA.Thing; // may be null
                    // Nightmare re-enforcement: ensure invariant immediately after an equip/take inventory completes
                    try
                    {
                        var settings = SurvivalToolsMod.Settings;
                        if (settings != null)
                        {
                            int allowed = AssignmentSearch.GetEffectiveCarryLimit(pawn, settings);
                            NightmareCarryEnforcer.EnforceNow(pawn, tool, allowed);
                        }
                        else
                        {
                            // Fallback path still routes through enforcer with default allowed
                            NightmareCarryEnforcer.EnforceNow(pawn, tool, AssignmentSearch.GetEffectiveCarryLimit(pawn, SurvivalToolsMod.Settings));
                        }
                    }
                    catch { /* defensive */ }
                }
            }
            catch { /* best-effort */ }
        }

        /// <summary>
        /// Patch WorkGiver_Scanner.JobOnThing - this catches work assignments before they become jobs
        /// </summary>
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.JobOnThing))]
        [HarmonyPrefix]
        public static bool WorkGiver_JobOnThing_Prefix(WorkGiver_Scanner __instance, Pawn pawn, Thing t)
        {
            try
            {
                if (__instance == null || pawn == null)
                    return true;

                // EARLY FILTER: Colonists only, avoid noise
                if (!pawn.IsColonist || !pawn.Awake())
                    return true;

                // Determine relevant stat for this work type; store for postfix to act on
                var workTypeDef = __instance.def?.workType;
                if (workTypeDef == null)
                    return true;

                // If the pawn cannot perform this work type OR it's not active for this pawn, and this isn't a forced job, skip rescue entirely
                bool isForced = pawn.CurJob != null && pawn.CurJob.playerForced;
                var ws = pawn.workSettings;
                if (!isForced && (pawn.WorkTypeIsDisabled(workTypeDef) || (ws != null && !ws.WorkIsActive(workTypeDef))))
                {
                    LogDebug($"[SurvivalTools.PreWork] Skipping WG rescue: {pawn.LabelShort} inactive/disabled work type {workTypeDef.defName}", $"PreWork.WG.SkipInactive|{pawn.ThingID}|{workTypeDef.defName}");
                    return true;
                }

                StatDef relevantStat = null;
                if (workTypeDef == WorkTypeDefOf.Mining)
                    relevantStat = ST_StatDefOf.DiggingSpeed;
                else if (workTypeDef == WorkTypeDefOf.PlantCutting)
                    relevantStat = ST_StatDefOf.TreeFellingSpeed;
                else if (workTypeDef == WorkTypeDefOf.Growing)
                {
                    // Distinguish sow vs harvest: WorkGiver defName containing "Sow" gets SowingSpeed, otherwise harvesting.
                    var wgdName = __instance.def?.defName ?? string.Empty;
                    if (wgdName.IndexOf("Sow", StringComparison.OrdinalIgnoreCase) >= 0)
                        relevantStat = ST_StatDefOf.SowingSpeed;
                    //else // We don't want PlantHarvestingSpeed to count for sowing jobs.
                    //    relevantStat = ST_StatDefOf.PlantHarvestingSpeed;
                }
                else if (workTypeDef == WorkTypeDefOf.Construction)
                    relevantStat = StatDefOf.ConstructionSpeed;
                else if (workTypeDef == WorkTypeDefOf.Research)
                    relevantStat = CompatAPI.GetResearchSpeedStat() ?? ST_StatDefOf.ResearchSpeed;

                if (relevantStat != null)
                {
                    // Exempt pure delivery WorkGivers (resource hauling only) from rescue/upgrade churn.
                    if (JobGate.IsPureDeliveryWorkGiver(__instance.def))
                    {
                        return true; // do not attempt upgrades for pure deliveries
                    }
                    _wgPendingStat[pawn.thingIDNumber] = relevantStat;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError($"Exception in PreWork_AutoEquip.WorkGiver_JobOnThing_Prefix: {ex}");
                return true;
            }
        }

        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.JobOnThing))]
        [HarmonyPostfix]
        public static void WorkGiver_JobOnThing_Postfix(WorkGiver_Scanner __instance, Pawn pawn, Thing t, ref Job __result)
        {
            try
            {
                if (__instance == null || pawn == null)
                    return;

                // Nothing to do if no job was produced
                if (__result == null)
                {
                    _wgPendingStat.Remove(pawn.thingIDNumber);
                    return;
                }

                // Only for colonists with pending stat from prefix
                if (!pawn.IsColonist || !pawn.Awake())
                {
                    _wgPendingStat.Remove(pawn.thingIDNumber);
                    return;
                }

                if (!_wgPendingStat.TryGetValue(pawn.thingIDNumber, out var relevantStat) || relevantStat == null)
                {
                    return;
                }

                // Skip upgrade attempts for pure delivery WorkGivers (resource hauling only)
                if (JobGate.IsPureDeliveryWorkGiver(__instance.def))
                {
                    _wgPendingStat.Remove(pawn.thingIDNumber);
                    return;
                }

                var settings = SurvivalToolsMod.Settings;
                if (!GetEnableAssignments(settings))
                {
                    _wgPendingStat.Remove(pawn.thingIDNumber);
                    return;
                }

                // Avoid loops: don't intervene for tool management jobs
                if (JobUtils.IsToolManagementJob(__result))
                {
                    _wgPendingStat.Remove(pawn.thingIDNumber);
                    return;
                }

                // Determine thresholds (reuse defaults similar to TryUpgradeForWork)
                float minGainPct = settings != null ? settings.assignMinGainPct : 0.1f;
                float searchRadius = settings != null ? settings.assignSearchRadius : 25f;
                int pathCostBudget = settings != null ? settings.assignPathCostBudget : 500;

                // Opportunistically queue an upgrade but DO NOT block WorkGiver jobs
                // Blocking here can cause the selected AI job to disappear. Let it proceed.
                bool upgraded = AssignmentSearch.TryUpgradeFor(pawn, relevantStat, minGainPct, searchRadius, pathCostBudget, AssignmentSearch.QueuePriority.Front, $"WorkGiver.JobOnThing({__instance?.def?.defName})");
                if (upgraded)
                {
                    Log.Warning($"[SurvivalTools.PreWork] WorkGiver: queued tool upgrade for {pawn.LabelShort} (job={__result?.def?.defName}), not blocking");
                }
            }
            catch (Exception ex)
            {
                LogError($"Exception in PreWork_AutoEquip.WorkGiver_JobOnThing_Postfix: {ex}");
            }
            finally
            {
                // Cleanup per-call state
                if (pawn != null) _wgPendingStat.Remove(pawn.thingIDNumber);
            }
        }

        /// <summary>
        /// Determine the primary work stat for a job, if any.
        /// </summary>
        private static StatDef GetRelevantWorkStat(Job job)
        {
            if (job?.def == null)
                return null;

            // Map common job types to their primary work stats
            var jobDef = job.def;

            // SAFETY: Skip complex jobs that shouldn't be interrupted by tool assignment
            if (jobDef == JobDefOf.DoBill ||
                jobDef.defName.IndexOf("Bill", StringComparison.Ordinal) >= 0 ||
                jobDef.defName.IndexOf("Craft", StringComparison.Ordinal) >= 0 ||
                jobDef.defName.IndexOf("Cook", StringComparison.Ordinal) >= 0)
            {
                LogDebug($"Skipping complex job {jobDef.defName} - not suitable for tool assignment", "PreWork.SkipComplexJob");
                return null; // Don't interfere with crafting/cooking jobs
            }

            // Tree cutting/harvesting
            if (jobDef == JobDefOf.CutPlant ||
                string.Equals(jobDef.defName, "FellTree", StringComparison.Ordinal) ||
                string.Equals(jobDef.defName, "HarvestTree", StringComparison.Ordinal))
            {
                return ST_StatDefOf.TreeFellingSpeed;
            }

            // Sowing (planting)
            if (jobDef == JobDefOf.Sow || string.Equals(jobDef.defName, "Sow", StringComparison.Ordinal))
            {
                return ST_StatDefOf.SowingSpeed;
            }

            // Plant harvesting
            if (jobDef == JobDefOf.Harvest ||
                string.Equals(jobDef.defName, "HarvestDesignated", StringComparison.Ordinal))
            {
                return ST_StatDefOf.PlantHarvestingSpeed;
            }

            // Mining
            if (jobDef == JobDefOf.Mine)
            {
                return ST_StatDefOf.DiggingSpeed;
            }

            // Research (bench)
            if (jobDef == JobDefOf.Research || string.Equals(jobDef.defName, "Research", StringComparison.Ordinal))
            {
                try { return CompatAPI.GetResearchSpeedStat() ?? ST_StatDefOf.ResearchSpeed; } catch { return ST_StatDefOf.ResearchSpeed; }
            }

            // Construction
            if (jobDef == JobDefOf.FinishFrame)
            {
                try
                {
                    // Deep inspect frame to detect any building (covers fences & modded building frames)
                    var frameThing = job.targetA.Thing as Frame;
                    var b = frameThing?.def?.entityDefToBuild as ThingDef;
                    if (b?.building != null)
                        return StatDefOf.ConstructionSpeed;
                }
                catch { }
                return StatDefOf.ConstructionSpeed; // Fallback still requires hammer
            }
            if (jobDef == JobDefOf.Repair)
            {
                return StatDefOf.ConstructionSpeed;
            }

            // Deconstruction
            if (jobDef == JobDefOf.Deconstruct)
            {
                return StatDefOf.ConstructionSpeed; // Same tools as construction
            }

            // Smoothing (if enabled)
            if (jobDef == JobDefOf.SmoothFloor ||
                jobDef == JobDefOf.SmoothWall)
            {
                // Gate on ConstructionSpeed so a hammer is required; SmoothingSpeed (if present) remains an optional bonus for scoring.
                // TODO[SMOOTHING_TOOL_PURPOSE]: if dedicated smoothing tools appear, evaluate using SmoothingSpeed as primary when tool carries both.
                return StatDefOf.ConstructionSpeed;
            }

            return null; // No relevant work stat found
        }

        /// <summary>
        /// Try to upgrade pawn's tool for the given work stat.
        /// Returns true if upgrade was queued (original job should be blocked).
        /// </summary>
        private static bool TryUpgradeForWork(Pawn pawn, StatDef workStat, Job originalJob, SurvivalToolsSettings settings, AssignmentSearch.QueuePriority priority)
        {
            // Hot path: don't log routine entry parameters

            // Get assignment parameters from settings (with defaults)
            float minGainPct = GetMinGainPct(settings);
            float searchRadius = GetSearchRadius(settings);
            int pathCostBudget = GetPathCostBudget(settings);

            // Hot path: don't log routine parameters

            // Check if gating would block this job (simplified check using current tool score)
            bool wouldBeGated = IsLikelyGated(pawn, workStat);

            // Hot path: don't log routine gating checks

            if (wouldBeGated && GetAssignRescueOnGate(settings))
            {
                // Gating rescue mode: any improvement is acceptable
                minGainPct = 0.001f; // Minimal threshold for any improvement
                LogDebug($"[SurvivalTools.PreWork] GATING RESCUE MODE: lowering threshold to {minGainPct:P1}", $"PreWork.RescueMode|{pawn.ThingID}|{workStat.defName}");
            }
            else if (!wouldBeGated)
            {
                // Normal assignment mode: require meaningful gain
                // Use configured minimum gain percentage
                // Hot path: don't log routine normal mode operation
            }
            else
            {
                // Gating would block but rescue is disabled
                // Hot path: don't log routine gate-no-rescue skips
                return false;
            }

            // Delegate to AssignmentSearch
            // Hot path: don't log routine delegation calls
            string caller = originalJob != null ? $"PreWork.TryTakeOrderedJob({originalJob.def?.defName})" : "PreWork.StartJob";
            bool result = AssignmentSearch.TryUpgradeFor(pawn, workStat, minGainPct, searchRadius, pathCostBudget, priority, caller);
            // Hot path: don't log routine results (AssignmentSearch logs important events)
            return result;
        }

        /// <summary>
        /// Get minimum gain percentage from settings with difficulty scaling.
        /// </summary>
        private static float GetMinGainPct(SurvivalToolsSettings settings)
        {
            if (settings == null)
                return 0.1f; // Default 10%

            // Use configured value with difficulty scaling
            float baseGainPct = settings.assignMinGainPct;

            // Scale by difficulty
            if (settings.extraHardcoreMode)
                return baseGainPct * 1.5f; // Nightmare: higher threshold
            if (settings.hardcoreMode)
                return baseGainPct * 1.25f; // Hardcore: moderate increase

            return baseGainPct; // Normal: as configured
        }

        /// <summary>
        /// Get search radius from settings with difficulty scaling.
        /// </summary>
        private static float GetSearchRadius(SurvivalToolsSettings settings)
        {
            if (settings == null)
                return 25f; // Default radius

            float baseRadius = settings.assignSearchRadius;

            // Scale by difficulty  
            if (settings.extraHardcoreMode)
                return baseRadius * 0.5f; // Nightmare: half radius
            if (settings.hardcoreMode)
                return baseRadius * 0.75f; // Hardcore: reduced radius

            return baseRadius; // Normal: full radius
        }

        /// <summary>
        /// Get path cost budget from settings with difficulty scaling.
        /// </summary>
        private static int GetPathCostBudget(SurvivalToolsSettings settings)
        {
            if (settings == null)
                return 500; // Default budget

            int baseBudget = settings.assignPathCostBudget;

            // Scale by difficulty
            if (settings.extraHardcoreMode)
                return baseBudget / 2; // Nightmare: half budget
            if (settings.hardcoreMode)
                return (baseBudget * 3) / 4; // Hardcore: 75% budget

            return baseBudget; // Normal: full budget
        }

        /// <summary>
        /// Get rescue on gate setting.
        /// </summary>
        private static bool GetAssignRescueOnGate(SurvivalToolsSettings settings)
        {
            if (settings == null)
                return true; // Default enabled

            return settings.assignRescueOnGate;
        }

        /// <summary>
        /// Get assignment enabled setting.
        /// </summary>
        private static bool GetEnableAssignments(SurvivalToolsSettings settings)
        {
            if (settings == null)
                return true; // Default enabled

            return settings.enableAssignments;
        }

        /// <summary>
        /// Check if a pawn would likely be gated for this work stat.
        /// Simplified check based on current tool availability.
        /// </summary>
        private static bool IsLikelyGated(Pawn pawn, StatDef workStat)
        {
            if (pawn == null || workStat == null)
                return false;

            // Get current best tool and score
            var currentTool = ToolScoring.GetBestTool(pawn, workStat, out float currentScore);
            float baseline = SurvivalToolUtility.GetNoToolBaseline(workStat);

            // If current score is at or below baseline, likely gated
            return currentScore <= baseline + 0.001f;
        }

        /// <summary>
        /// Phase 11.13: Determines if a work stat should trigger early gating enforcement (tool-seeking behavior).
        /// Includes only stats where tools are REQUIRED for the work, not optional bonuses.
        /// </summary>
        private static bool IsGateableWorkStat(StatDef stat)
        {
            if (stat == null) return false;

            // Core work stats that require tools (pawns cannot perform work without them)
            if (stat == ST_StatDefOf.SowingSpeed) return true;              // Sowing requires tool
            if (stat == ST_StatDefOf.TreeFellingSpeed) return true;         // Tree felling requires tool
            if (stat == StatDefOf.ConstructionSpeed) return true;           // Construction requires tool
            if (stat == ST_StatDefOf.DiggingSpeed) return true;             // Mining requires tool
            if (stat == ST_StatDefOf.PlantHarvestingSpeed) return true;     // Harvesting requires tool
            if (stat == ST_StatDefOf.ResearchSpeed) return true;            // Research requires tool
            if (stat == ST_StatDefOf.MaintenanceSpeed) return true;         // Maintenance requires tool
            if (stat == ST_StatDefOf.DeconstructionSpeed) return true;      // Deconstruction requires tool

            // Optional stats (provide bonuses but work can be done without tools)
            // - CleaningSpeed: Optional bonus, only gated in Extra Hardcore mode
            // - MedicalOperationSpeed: Optional bonus (surgery can be done without tools, just slower)
            // - MedicalSurgerySuccessChance: Optional bonus (affects quality, not ability)
            // - ButcheryFleshSpeed: Optional bonus (butchering can be done without tools, just slower)
            // - ButcheryFleshEfficiency: Optional bonus (affects yield, not ability)

            return false;
        }

        /// <summary>
        /// Phase 11.13: Gets a friendly label for the work kind based on stat (for logging).
        /// </summary>
        private static string GetWorkKindLabel(StatDef workStat, Job job)
        {
            if (workStat == ST_StatDefOf.SowingSpeed) return "Sow";
            if (workStat == ST_StatDefOf.TreeFellingSpeed) return "CutPlant";
            if (workStat == StatDefOf.ConstructionSpeed) return "Construct";
            if (workStat == ST_StatDefOf.DiggingSpeed) return "Mine";
            if (workStat == ST_StatDefOf.PlantHarvestingSpeed) return "Harvest";
            if (workStat == ST_StatDefOf.ResearchSpeed) return "Research";
            if (workStat == ST_StatDefOf.MaintenanceSpeed) return "Maintain";
            if (workStat == ST_StatDefOf.DeconstructionSpeed) return "Deconstruct";
            if (workStat == ST_StatDefOf.MedicalOperationSpeed) return "Medical";
            if (workStat == ST_StatDefOf.MedicalSurgerySuccessChance) return "Surgery";
            if (workStat == ST_StatDefOf.ButcheryFleshSpeed) return "Butcher";
            if (workStat == ST_StatDefOf.ButcheryFleshEfficiency) return "Butcher";
            if (workStat == ST_StatDefOf.CleaningSpeed) return "Clean";
            return job?.def?.defName ?? "Work";
        }

        /// <summary>
        /// Phase 12: Try to auto-swap battery in current tool if charge is low.
        /// Returns true if battery swap was queued.
        /// </summary>
        private static bool TryAutoSwapBattery(Pawn pawn, StatDef workStat, SurvivalToolsSettings settings)
        {
            if (pawn == null || workStat == null || settings == null)
                return false;

            if (!settings.autoSwapBatteries || !settings.enablePoweredTools)
                return false;

            // Find current best tool for this stat
            var currentTool = ToolScoring.GetBestTool(pawn, workStat, out float _);
            if (currentTool == null)
                return false;

            // Check if it's a powered tool
            var powerComp = currentTool.TryGetComp<CompPowerTool>();
            if (powerComp == null)
                return false;

            // Check if charge is below threshold
            float chargePct = powerComp.ChargePct;
            if (chargePct > settings.autoSwapThreshold)
                return false; // Still has enough charge

            // Search for a better battery in inventory and nearby
            Thing bestBattery = FindBestBattery(pawn, powerComp);
            if (bestBattery == null)
                return false; // No better battery available

            // Queue battery swap job
            Job swapJob = JobMaker.MakeJob(ST_JobDefOf.ST_SwapBattery, currentTool, bestBattery);
            pawn.jobs?.jobQueue?.EnqueueFirst(swapJob, JobTag.Misc);

            if (Prefs.DevMode && settings.debugLogging)
            {
                LogDebug($"[PreWork] Auto-swap battery queued for {pawn.LabelShort}: {currentTool.LabelShort} ({chargePct:P0} < {settings.autoSwapThreshold:P0})",
                    $"PreWork.AutoSwap|{pawn.ThingID}");
            }

            return true;
        }

        /// <summary>
        /// Phase 12: Find the best available battery for a powered tool.
        /// Searches inventory first, then nearby stockpiles.
        /// </summary>
        private static Thing FindBestBattery(Pawn pawn, CompPowerTool powerComp)
        {
            if (pawn == null || powerComp == null)
                return null;

            Thing bestBattery = null;
            float bestCharge = powerComp.ChargePct;

            // Search inventory first
            if (pawn.inventory?.innerContainer != null)
            {
                for (int i = 0; i < pawn.inventory.innerContainer.Count; i++)
                {
                    var item = pawn.inventory.innerContainer[i];
                    if (item == null)
                        continue;

                    var batteryComp = item.TryGetComp<CompBatteryCell>();
                    if (batteryComp == null)
                        continue;

                    // Check if this battery has more charge than current
                    float itemCharge = batteryComp.ChargePct;
                    if (itemCharge > bestCharge)
                    {
                        bestBattery = item;
                        bestCharge = itemCharge;
                    }
                }
            }

            // If we found a good battery in inventory, return it
            if (bestBattery != null)
                return bestBattery;

            // Search nearby stockpiles (within assignment search radius)
            var settings = SurvivalToolsMod.Settings;
            float searchRadius = settings?.assignSearchRadius ?? 25f;

            if (!pawn.Spawned || pawn.Map == null)
                return null;

            // Find all batteries in range
            List<Thing> nearbyBatteries = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            if (nearbyBatteries == null)
                return null;

            for (int i = 0; i < nearbyBatteries.Count; i++)
            {
                var item = nearbyBatteries[i];
                if (item == null || item.Position.DistanceTo(pawn.Position) > searchRadius)
                    continue;

                var batteryComp = item.TryGetComp<CompBatteryCell>();
                if (batteryComp == null)
                    continue;

                // Check if this battery has more charge than current
                float itemCharge = batteryComp.ChargePct;
                if (itemCharge > bestCharge && pawn.CanReach(item, PathEndMode.ClosestTouch, Danger.Deadly))
                {
                    bestBattery = item;
                    bestCharge = itemCharge;
                }
            }

            return bestBattery;
        }

        /// <summary>
        /// Phase 12: Clear transient state to prevent Job reference warnings on save.
        /// Called from GameComponent during save operation.
        /// Static dictionaries can hold Job references which cause "Object with load ID Job_XXXXX 
        /// is referenced but is not deep-saved" warnings during save.
        /// </summary>
        public static void ClearTransientState()
        {
            try
            {
                _wgPendingStat?.Clear();
                _nmToken?.Clear();
                _nmLogCooldown?.Clear();
                _preworkCancelCD?.Clear();

                LogDebug("PreWork_AutoEquip transient state cleared for save", "PreWork.ClearState");
            }
            catch (Exception ex)
            {
                Log.Warning($"[SurvivalTools.PreWork_AutoEquip] Error clearing transient state: {ex}");
            }
        }
    }
}