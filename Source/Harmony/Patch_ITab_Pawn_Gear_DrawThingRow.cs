//rimworld 1.6 / C# 7.3
//Patch_ITab_Pawn_Gear_DrawThingRow.cs
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using static SurvivalTools.ST_Logging;

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

        /// <summary>
        /// Transpiler: replace the string argument passed to Widgets.Label with the result of
        /// AdjustDisplayedLabel(originalLabel, thing, pawn).
        /// 
        /// The strategy:
        ///  - At the call site for Widgets.Label(Rect, string) the evaluation stack contains:
        ///      ... Rect, string
        ///  - We inject: push thing, push pawn -> call Adjust(originalString, thing, pawn)
        ///  - Adjust will pop (string, thing, pawn) and push a new string -> stack becomes: ... Rect, newString
        ///  - The original Widgets.Label call then executes with Rect, newString
        /// 
        /// This mirrors the existing approach but includes defensive checks and reduced logging.
        /// </summary>
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var didPatch = false;

            for (int i = 0; i < list.Count; i++)
            {
                var ins = list[i];

                // We're looking for the call to Widgets.Label(Rect, string)
                if (!didPatch && ins.Calls(MI_Widgets_Label))
                {
                    // Inject: ..., Rect, originalString, thing, pawn -> Call Adjust -> yields ..., Rect, adjustedString
                    // push the "thing" argument (the original method had it as arg index 3) and the pawn via SelPawnForGear
                    yield return new CodeInstruction(OpCodes.Ldarg_3); // thing
                    yield return new CodeInstruction(OpCodes.Ldarg_0); // this (ITab_Pawn_Gear)
                    // call property getter to get pawn (may be null, Adjust handles that)
                    yield return new CodeInstruction(OpCodes.Call, MI_SelPawnForGear); // pawn

                    // call our adjust method: pops (string, thing, pawn), returns string
                    yield return new CodeInstruction(OpCodes.Call, MI_Adjust);

                    didPatch = true;
                }

                yield return ins;
            }

            if (!didPatch && IsDebugLoggingEnabled)
            {
                const string key = "Transpile_ITab_Pawn_Gear_DrawThingRow_MissingLabel";
                if (ShouldLogWithCooldown(key))
                    Log.Warning("[SurvivalTools] Failed to patch ITab_Pawn_Gear.DrawThingRow: Widgets.Label call not found.");
            }
        }

        /// <summary>
        /// Adjust the displayed label for survival tools:
        /// - Append 'forced' if the pawn has forced the item.
        /// - Append 'in use' if the tool is currently used by the pawn.
        /// Be careful with nulls and minimize debug spam.
        /// </summary>
        public static string AdjustDisplayedLabel(string originalLabel, Thing thing, Pawn pawn)
        {
            if (string.IsNullOrEmpty(originalLabel)) originalLabel = string.Empty;

            if (thing == null) return originalLabel;

            // We only care about survival tools (includes VirtualSurvivalTool since it inherits SurvivalTool)
            if (!(thing is SurvivalTool tool))
                return originalLabel;

            // Ensure pawn-safe checks
            Pawn_SurvivalToolAssignmentTracker tracker = null;
            try
            {
                tracker = pawn?.GetComp<Pawn_SurvivalToolAssignmentTracker>();
            }
            catch
            {
                // Defensive: in very odd cases GetComp could throw; just return original label
                return originalLabel;
            }

            if (tracker != null)
            {
                try
                {
                    if (tracker.forcedHandler?.IsForced(tool) == true)
                    {
                        originalLabel += $", {"ApparelForcedLower".Translate()}";
                    }
                }
                catch
                {
                    // swallow — forcedHandler should be safe but be defensive about mod interactions
                }
            }

            // Tool in-use indicator (be careful reading pawn.jobs)
            bool inUse = false;
            try
            {
                inUse = tool.InUse;
            }
            catch
            {
                inUse = false;
            }

            if (inUse)
            {
                originalLabel += $", {"ToolInUse".Translate()}";

                // Optional extra debug context (very throttled)
                if (IsDebugLoggingEnabled && pawn != null && pawn.jobs?.curJob != null)
                {
                    var job = pawn.jobs.curJob;
                    string key = $"ITab_Gear_ToolLabelDebug_{pawn.ThingID}_{tool.ThingID}_{job.def?.defName}";
                    if (ShouldLogWithCooldown(key))
                    {
                        List<StatDef> relevantStats = new List<StatDef>();
                        try
                        {
                            relevantStats = SurvivalToolUtility.RelevantStatsFor(job.workGiverDef, job)?.ToList() ?? new List<StatDef>();
                        }
                        catch
                        {
                            relevantStats = new List<StatDef>();
                        }

                        SurvivalTool bestTool = null;
                        try
                        {
                            bestTool = pawn.GetBestSurvivalTool(relevantStats);
                        }
                        catch { bestTool = null; }

                        // Only append small debug fragment to label when bestTool matches to avoid confusion and keep label short.
                        if (bestTool == tool)
                        {
                            originalLabel += $" [{job.def?.defName ?? "job?"}]";
                        }
                    }
                }
            }

            return originalLabel;
        }
    }
}
