using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(ITab_Pawn_Gear), "DrawThingRow")]
    public static class Patch_ITab_Pawn_Gear_DrawThingRow
    {
        private static readonly MethodInfo MI_Widgets_Label =
            AccessTools.Method(typeof(Widgets), nameof(Widgets.Label), new[] { typeof(Rect), typeof(string) });

        private static readonly MethodInfo MI_SelPawnForGear =
            AccessTools.PropertyGetter(typeof(ITab_Pawn_Gear), "SelPawnForGear");

        private static readonly MethodInfo MI_Adjust =
            AccessTools.Method(typeof(Patch_ITab_Pawn_Gear_DrawThingRow), nameof(AdjustDisplayedLabel));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var didPatch = false;

            for (int i = 0; i < list.Count; i++)
            {
                var ins = list[i];

                // When we reach the call to Widgets.Label(Rect, string), the stack is: ..., rect, label
                if (!didPatch && ins.Calls(MI_Widgets_Label))
                {
                    // Inject BEFORE the call so we transform the label argument:
                    // Stack: ..., rect, label
                    //   ldarg.3                  // Thing thing
                    //   ldarg.0
                    //   call     get_SelPawnForGear -> Pawn
                    //   call     AdjustDisplayedLabel(string, Thing, Pawn) -> string
                    // Result: ..., rect, adjustedLabel
                    yield return new CodeInstruction(OpCodes.Ldarg_3);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, MI_SelPawnForGear);
                    yield return new CodeInstruction(OpCodes.Call, MI_Adjust);

                    didPatch = true;
                }

                yield return ins;
            }

            if (!didPatch)
            {
                if (SurvivalTools.Settings != null && SurvivalTools.Settings.debugLogging)
                {
                    Log.Warning("[SurvivalTools] Failed to patch ITab_Pawn_Gear.DrawThingRow: Widgets.Label call not found.");
                }
            }
        }

        // unchanged logic—only called at the draw site now, so it can't be overwritten later
        public static string AdjustDisplayedLabel(string originalLabel, Thing thing, Pawn pawn)
        {
            try
            {
                if (thing is SurvivalTool tool)
                {
                    // Forced tag (unchanged)
                    var tracker = pawn?.GetComp<Pawn_SurvivalToolAssignmentTracker>();
                    if (tracker != null && tracker.forcedHandler.IsForced(tool))
                    {
                        originalLabel += $", {"ApparelForcedLower".Translate()}";
                    }

                    // Active use check + info for Dev
                    if (TryGetActiveUseInfo(tool, pawn, out var jobDef, out var activeStat, out var statFactor))
                    {
                        originalLabel += $", {"ToolInUse".Translate()}";

                        // Dev/Debug readout
                        if (SurvivalTools.Settings != null && SurvivalTools.Settings.debugLogging)
                        {
                            string factorStr = statFactor.ToStringByStyle(ToStringStyle.Integer, ToStringNumberSense.Factor); // e.g., x125%
                            originalLabel += $"  [job={(jobDef?.defName ?? "null")} • stat={(activeStat?.defName ?? "null")} • factor={factorStr}]";
                        }
                    }
                    else if (SurvivalTools.Settings != null && SurvivalTools.Settings.debugLogging)
                    {
                        Log.Message($"[SurvivalTools.GearTab] Not in use: pawn='{pawn?.LabelShort ?? "null"}' " +
                                    $"tool='{tool.LabelCap}' job='{pawn?.jobs?.curJob?.def?.defName ?? "null"}'.");
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Warning($"[SurvivalTools] AdjustDisplayedLabel error: {e}");
            }

            return originalLabel;
        }

        /// <summary>
        /// Returns true if 'tool' is actively used for pawn's CURRENT job, and outputs the job, the deciding stat, and the tool's factor.
        /// </summary>
        private static bool TryGetActiveUseInfo(SurvivalTool tool, Pawn pawn, out JobDef jobDef, out StatDef decidingStat, out float factor)
        {
            jobDef = null;
            decidingStat = null;
            factor = 0f;

            if (tool == null || pawn == null) return false;
            if (!pawn.Spawned || pawn.Dead || pawn.Downed) return false;
            if (pawn.Drafted || pawn.InMentalState) return false;
            if (!pawn.CanUseSurvivalTools()) return false;

            var job = pawn.jobs?.curJob;
            var wg = job?.workGiverDef;
            if (wg == null) return false;

            var reqStats = wg.GetModExtension<WorkGiverExtension>()?.requiredStats;
            if (reqStats == null || reqStats.Count == 0) return false;

            // If this tool is the pawn's best for ANY required stat (and better than toolless), treat as "in use".
            for (int i = 0; i < reqStats.Count; i++)
            {
                var stat = reqStats[i];
                if (stat == null) continue;

                SurvivalTool best;
                float bestFactor;
                if (pawn.HasSurvivalToolFor(stat, out best, out bestFactor))
                {
                    if (best == tool && tool.BetterThanWorkingToollessFor(stat))
                    {
                        jobDef = job.def;
                        decidingStat = stat;
                        factor = bestFactor; // already the factor from your utility (respects Hardcore no-tool baseline)
                        return true;
                    }
                }
            }

            return false;
        }

    }
}