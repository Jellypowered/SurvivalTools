using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI;
using RimWorld;
using RimWorld.Planet;
using System.Diagnostics;

namespace SurvivalTools
{
    public static class SurvivalToolUtility
    {
        public static readonly FloatRange MapGenToolHitPointsRange = new FloatRange(0.3f, 0.7f);
        public const float MapGenToolMaxStuffMarketValue = 3f;

        public static List<StatDef> SurvivalToolStats { get; } =
            DefDatabase<StatDef>.AllDefsListForReading.Where(s => s.RequiresSurvivalTool()).ToList();

        public static List<WorkGiverDef> SurvivalToolWorkGivers { get; } =
            DefDatabase<WorkGiverDef>.AllDefsListForReading.Where(w => w.HasModExtension<WorkGiverExtension>()).ToList();

        // ---------------- New shared helpers: WG/Job -> required stats ----------------

        /// <summary>
        /// Returns the list of stats relevant to this work giver / job.
        /// Priority: WorkGiverExtension.requiredStats -> job-based mapping.
        /// </summary>
        public static List<StatDef> RelevantStatsFor(WorkGiverDef wg, Job job)
        {
            var fromWg = wg?.GetModExtension<WorkGiverExtension>()?.requiredStats;
            if (fromWg != null && fromWg.Count > 0)
                return fromWg.Where(s => s != null).Distinct().ToList();

            var fallback = StatsForJob(job);
            if ((SurvivalTools.Settings?.debugLogging ?? false) && job?.def != null)
            {
                Log.Message($"[SurvivalTools] Using job fallback stats for WGD='{wg?.defName ?? "null"}' Job='{job.def.defName}'.");
            }
            return fallback;
        }

        public static List<StatDef> RelevantStatsFor(WorkGiverDef wg, JobDef jobDef)
        {
            var fromWg = wg?.GetModExtension<WorkGiverExtension>()?.requiredStats;
            if (fromWg != null && fromWg.Count > 0)
                return fromWg.Where(s => s != null).Distinct().ToList();

            var fallback = StatsForJob(jobDef);
            if ((SurvivalTools.Settings?.debugLogging ?? false) && jobDef != null)
            {
                Log.Message($"[SurvivalTools] Using job fallback stats for WGD='{wg?.defName ?? "null"}' Job='{jobDef.defName}'.");
            }
            return fallback;
        }

        /// <summary>
        /// Minimal job->stat mapping used when WG doesn't declare requiredStats.
        /// Extend as you add new tool-gated workflows (e.g., StonecuttingSpeed).
        /// </summary>
        public static List<StatDef> StatsForJob(Job job) => StatsForJob(job?.def);

        public static List<StatDef> StatsForJob(JobDef jobDef)
        {
            var list = new List<StatDef>(2);
            if (jobDef == null) return list;

            // Strong exact matches first
            if (jobDef == JobDefOf.Mine)
            {
                // Use ST stats so pickaxes qualify
                list.Add(ST_StatDefOf.DiggingSpeed);
                // If you want mining yield to influence selection too, uncomment:
                // list.Add(ST_StatDefOf.MiningYieldDigging);
                return list;
            }

            // String-based heuristics (safe fallback)
            var j = jobDef.defName ?? string.Empty;
            var s = j.ToLowerInvariant();

            // Construction-ish (use vanilla ConstructionSpeed unless you add an ST stat for it)
            if (s.Contains("construct") || s.Contains("build") || s.Contains("frame") ||
                s.Contains("repair") || s.Contains("smooth") || s.Contains("install") ||
                s.Contains("uninstall") || s.Contains("deconstruct") || s.Contains("buildroof") ||
                s.Contains("removeroof"))
            {
                list.Add(StatDefOf.ConstructionSpeed);
                return list;
            }

            // Plants (use ST PlantHarvestingSpeed so hatchets/sickles matter)
            if (s.Contains("plant") || s.Contains("sow") || s.Contains("harvest") || s.Contains("cut"))
            {
                list.Add(ST_StatDefOf.PlantHarvestingSpeed);
                return list;
            }

            // Tree felling (your custom jobs)
            if (jobDef == ST_JobDefOf.FellTree || jobDef == ST_JobDefOf.FellTreeDesignated
                || jobDef == ST_JobDefOf.HarvestTree || jobDef == ST_JobDefOf.HarvestTreeDesignated)
            {
                list.Add(ST_StatDefOf.TreeFellingSpeed);
                return list;
            }

            // Example for future: stonecutting tools
            // if (s.Contains("stonecut") || s.Contains("makestoneblocks"))
            // {
            //     list.Add(ST_StatDefOf.StonecuttingSpeed);
            //     return list;
            // }

            return list;
        }

        // ------------------------------------------------------------------------------

        public static bool RequiresSurvivalTool(this StatDef stat)
        {
            if (stat?.parts == null) return false;
            foreach (var part in stat.parts)
                if (part?.GetType() == typeof(StatPart_SurvivalTool))
                    return true;
            return false;
        }

        public static bool IsSurvivalTool(this BuildableDef def, out SurvivalToolProperties toolProps)
        {
            toolProps = def?.GetModExtension<SurvivalToolProperties>();
            return def.IsSurvivalTool();
        }

        public static bool IsSurvivalTool(this BuildableDef def) =>
            def is ThingDef tDef && tDef.thingClass == typeof(SurvivalTool) && tDef.HasModExtension<SurvivalToolProperties>();

        public static bool CanUseSurvivalTools(this Pawn pawn) =>
            pawn.RaceProps.intelligence >= Intelligence.ToolUser &&
            pawn.Faction == Faction.OfPlayer &&
            (pawn.equipment != null || pawn.inventory != null) &&
            pawn.TraderKind == null;

        public static bool IsUnderSurvivalToolCarryLimitFor(this int count, Pawn pawn) =>
            !SurvivalTools.Settings.toolLimit || count < pawn.GetStatValue(ST_StatDefOf.SurvivalToolCarryCapacity);

        public static IEnumerable<Thing> GetHeldSurvivalTools(this Pawn pawn) =>
            (pawn.inventory?.innerContainer != null)
                ? pawn.inventory.innerContainer.Where(t => t.def.IsSurvivalTool())
                : Enumerable.Empty<Thing>();

        public static IEnumerable<Thing> GetUsableHeldSurvivalTools(this Pawn pawn)
        {
            var held = pawn.GetHeldSurvivalTools().ToList(); // snapshot
            return held.Where(t => held.IndexOf(t).IsUnderSurvivalToolCarryLimitFor(pawn));
        }

        public static IEnumerable<Thing> GetAllUsableSurvivalTools(this Pawn pawn)
        {
            var eqTools = (pawn.equipment != null)
                ? pawn.equipment.GetDirectlyHeldThings().Where(t => t.def.IsSurvivalTool())
                : Enumerable.Empty<Thing>();

            return eqTools.Concat(pawn.GetUsableHeldSurvivalTools());
        }

        public static bool CanUseSurvivalTool(this Pawn pawn, ThingDef def)
        {
            var props = def?.GetModExtension<SurvivalToolProperties>();
            if (props == null)
            {
                if (SurvivalTools.Settings != null && SurvivalTools.Settings.debugLogging)
                {
                    Log.Error($"Tried to check if {def} is a usable tool but has null tool properties");
                }
                return false;
            }
            if (props.baseWorkStatFactors == null) return false;

            foreach (var modifier in props.baseWorkStatFactors)
                if (modifier?.stat?.Worker?.IsDisabledFor(pawn) == false)
                    return true;
            return false;
        }

        public static IEnumerable<SurvivalTool> BestSurvivalToolsFor(Pawn pawn)
        {
            foreach (var stat in SurvivalToolStats)
            {
                var tool = pawn.GetBestSurvivalTool(stat);
                if (tool != null) yield return tool;
            }
        }

        public static bool HasSurvivalTool(this Pawn pawn, ThingDef tool) =>
            pawn.GetHeldSurvivalTools().Any(t => t.def == tool);

        public static bool HasSurvivalToolFor(this Pawn pawn, StatDef stat)
        {
            if (stat == null || stat.GetStatPart<StatPart_SurvivalTool>() == null)
                return false;
            return pawn.GetBestSurvivalTool(stat) != null;
        }

        public static bool HasSurvivalToolFor(this Pawn pawn, StatDef stat, out SurvivalTool tool, out float statFactor)
        {
            tool = pawn.GetBestSurvivalTool(stat);
            statFactor = tool?.WorkStatFactors.ToList().GetStatFactorFromList(stat) ?? -1f;
            return tool != null;
        }

        public static SurvivalTool GetBestSurvivalTool(this Pawn pawn, StatDef stat)
        {
            if (!pawn.CanUseSurvivalTools() || stat == null) return null;

            var statPart = stat.GetStatPart<StatPart_SurvivalTool>();
            if (statPart == null) return null; // not a survival-tool-gated stat

            SurvivalTool best = null;
            float bestFactor = statPart.NoToolStatFactor;

            var usable = pawn.GetAllUsableSurvivalTools().ToList();
            foreach (SurvivalTool cur in usable)
            {
                if (cur == null) continue;
                foreach (var mod in cur.WorkStatFactors)
                {
                    if (mod?.stat == stat && mod.value > bestFactor)
                    {
                        best = cur;
                        bestFactor = mod.value;
                    }
                }
            }

            if (best != null)
                LessonAutoActivator.TeachOpportunity(ST_ConceptDefOf.UsingSurvivalTools, OpportunityType.Important);

            return best;
        }

        public static string GetSurvivalToolOverrideReportText(SurvivalTool tool, StatDef stat)
        {
            var statFactorList = tool.WorkStatFactors.ToList();
            var stuffPropsTool = tool.Stuff?.GetModExtension<StuffPropsTool>();

            var builder = new StringBuilder();
            builder.AppendLine(stat.description);

            builder.AppendLine();
            builder.AppendLine(tool.def.LabelCap + ": " +
                tool.def.GetModExtension<SurvivalToolProperties>().baseWorkStatFactors.GetStatFactorFromList(stat)
                    .ToStringByStyle(ToStringStyle.Integer, ToStringNumberSense.Factor));

            builder.AppendLine();
            builder.AppendLine(ST_StatDefOf.ToolEffectivenessFactor.LabelCap + ": " +
                tool.GetStatValue(ST_StatDefOf.ToolEffectivenessFactor)
                    .ToStringByStyle(ToStringStyle.Integer, ToStringNumberSense.Factor));

            if (stuffPropsTool != null && stuffPropsTool.toolStatFactors.GetStatFactorFromList(stat) != 1f)
            {
                builder.AppendLine();
                builder.AppendLine("StatsReport_Material".Translate() + " (" + tool.Stuff.LabelCap + "): " +
                    stuffPropsTool.toolStatFactors.GetStatFactorFromList(stat)
                        .ToStringByStyle(ToStringStyle.Integer, ToStringNumberSense.Factor));
            }

            builder.AppendLine();
            builder.AppendLine("StatsReport_FinalValue".Translate() + ": " +
                statFactorList.GetStatFactorFromList(stat)
                    .ToStringByStyle(ToStringStyle.Integer, ToStringNumberSense.Factor));

            return builder.ToString();
        }

        public static void TryDegradeTool(Pawn pawn, StatDef stat)
        {
            var tool = pawn.GetBestSurvivalTool(stat);
            if (tool != null && tool.def.useHitPoints && SurvivalTools.Settings.ToolDegradationEnabled)
            {
                LessonAutoActivator.TeachOpportunity(ST_ConceptDefOf.SurvivalToolDegradation, OpportunityType.GoodToKnow);
                tool.workTicksDone++;
                if (tool.workTicksDone >= tool.WorkTicksToDegrade)
                {
                    tool.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, 1));
                    tool.workTicksDone = 0;
                }
            }
        }

        public static IEnumerable<Thing> GetHeldSurvivalTools(this ThingOwner container) =>
            container != null ? container.Where(t => t.def.IsSurvivalTool()) : Enumerable.Empty<Thing>();

        public static int HeldSurvivalToolCount(this Pawn pawn) =>
            pawn.inventory?.innerContainer?.GetHeldSurvivalTools().Count() ?? 0;

        public static bool CanCarryAnyMoreSurvivalTools(this Pawn pawn, int heldToolOffset = 0) =>
            (pawn.RaceProps.Humanlike && (pawn.HeldSurvivalToolCount() + heldToolOffset).IsUnderSurvivalToolCarryLimitFor(pawn))
            || pawn.IsFormingCaravan() || pawn.IsCaravanMember();

        public static bool MeetsWorkGiverStatRequirements(this Pawn pawn, List<StatDef> requiredStats)
        {
            if (requiredStats == null || requiredStats.Count == 0)
                return true;

            var s = SurvivalTools.Settings;

            if (s != null && s.hardcoreMode)
            {
                // Only tool-gated stats matter for Hardcore tool checks
                var toolStats = requiredStats.Where(st => st != null && st.RequiresSurvivalTool()).ToList();
                if (toolStats.Count == 0) return true;

                foreach (var stat in toolStats)
                {
                    if (!pawn.HasSurvivalToolFor(stat))
                    {
                        // If AutoTool is allowed, don't hard-fail here — let the finder try.
                        if (s.autoTool)
                        {
                            if (s.debugLogging)
                                Log.Message($"[SurvivalTools] {pawn.LabelShort} missing required tool for stat {stat.defName}, but AutoTool will attempt acquisition.");
                            continue;
                        }

                        // AutoTool disabled: this pawn is blocked.
                        if (s.debugLogging)
                            Log.Message($"[SurvivalTools] {pawn.LabelShort} cannot start job: missing required tool for stat {stat.defName}");
                        return false;
                    }
                }
                return true;
            }

            // Non-hardcore: original behavior (stat must be > 0)
            foreach (var stat in requiredStats)
                if (stat != null && pawn.GetStatValue(stat) <= 0f)
                    return false;

            return true;
        }


        public static IEnumerable<WorkGiver> AssignedToolRelevantWorkGivers(this Pawn pawn)
        {
            var workSettings = pawn.workSettings;
            if (workSettings == null)
            {
                if (SurvivalTools.Settings != null && SurvivalTools.Settings.debugLogging)
                {
                    Log.ErrorOnce(
                        $"Tried to get tool-relevant work givers for {pawn} but has null workSettings",
                        11227
                    );
                }
                yield break;
            }

            foreach (var giver in workSettings.WorkGiversInOrderNormal)
            {
                var ext = giver.def.GetModExtension<WorkGiverExtension>();
                if (ext?.requiredStats == null) continue;

                foreach (var stat in ext.requiredStats)
                {
                    if (stat.RequiresSurvivalTool())
                    {
                        yield return giver;
                        break;
                    }
                }
            }
        }

        public static List<StatDef> AssignedToolRelevantWorkGiversStatDefs(this Pawn pawn)
        {
            var result = new List<StatDef>();
            foreach (var giver in pawn.AssignedToolRelevantWorkGivers())
            {
                var req = giver.def.GetModExtension<WorkGiverExtension>()?.requiredStats;
                if (req == null) continue;

                foreach (var stat in req)
                    if (stat != null && !result.Contains(stat))
                        result.Add(stat);
            }
            return result;
        }

        public static bool NeedsSurvivalTool(this Pawn pawn, SurvivalTool tool)
        {
            foreach (var stat in pawn.AssignedToolRelevantWorkGiversStatDefs())
                if (StatUtility.StatListContains(tool.WorkStatFactors.ToList(), stat))
                    return true;
            return false;
        }

        public static bool BetterThanWorkingToollessFor(this SurvivalTool tool, StatDef stat)
        {
            var statPart = stat.GetStatPart<StatPart_SurvivalTool>();
            if (statPart == null)
            {
                if (SurvivalTools.Settings != null && SurvivalTools.Settings.debugLogging)
                {
                    Log.ErrorOnce($"Tried to check if {tool} is better than working toolless for {stat} which has no StatPart_SurvivalTool", 8120196);
                }
                return false;
            }
            return StatUtility.GetStatValueFromList(tool.WorkStatFactors.ToList(), stat, 0f) > statPart.NoToolStatFactor;
        }

        public static Job DequipAndTryStoreSurvivalTool(this Pawn pawn, Thing tool, bool enqueueCurrent = true)
        {
            if (pawn.CurJob != null && enqueueCurrent)
                pawn.jobs.jobQueue.EnqueueFirst(pawn.CurJob);

            var zoneManager = pawn.MapHeld?.zoneManager;
            var pawnPosStockpile = zoneManager?.ZoneAt(pawn.PositionHeld) as Zone_Stockpile;

            if ((pawnPosStockpile == null || !pawnPosStockpile.settings.filter.Allows(tool))
                && StoreUtility.TryFindBestBetterStoreCellFor(tool, pawn, pawn.MapHeld, StoreUtility.CurrentStoragePriorityOf(tool), pawn.Faction, out IntVec3 c))
            {
                var haulJob = new Job(JobDefOf.HaulToCell, tool, c) { count = 1 };
                pawn.jobs.jobQueue.EnqueueFirst(haulJob);
            }

            return new Job(ST_JobDefOf.DropSurvivalTool, tool);
        }

        public static bool CanRemoveExcessSurvivalTools(this Pawn pawn) =>
            !pawn.Drafted && !pawn.IsWashing() && !pawn.IsFormingCaravan() && !pawn.IsCaravanMember()
            && pawn.CurJobDef?.casualInterruptible != false
            && !pawn.IsBurning() && !(pawn.carryTracker?.CarriedThing is SurvivalTool);

        private static bool IsWashing(this Pawn pawn)
        {
            if (!ModCompatibilityCheck.DubsBadHygiene)
                return false;
            return pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("Washing"));
        }
    }
}
