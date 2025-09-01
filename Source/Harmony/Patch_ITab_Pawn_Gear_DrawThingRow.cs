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

                if (!didPatch && ins.Calls(MI_Widgets_Label))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_3); // thing
                    yield return new CodeInstruction(OpCodes.Ldarg_0); // this
                    yield return new CodeInstruction(OpCodes.Call, MI_SelPawnForGear); // SelPawnForGear
                    yield return new CodeInstruction(OpCodes.Call, MI_Adjust); // AdjustDisplayedLabel(original, thing, pawn)
                    didPatch = true;
                }

                yield return ins;
            }

            if (!didPatch && SurvivalToolUtility.IsDebugLoggingEnabled)
            {
                Log.Warning("[SurvivalTools] Failed to patch ITab_Pawn_Gear.DrawThingRow: Widgets.Label call not found.");
            }
        }

        public static string AdjustDisplayedLabel(string originalLabel, Thing thing, Pawn pawn)
        {
            if (!(thing is SurvivalTool tool))
                return originalLabel;

            var tracker = pawn?.GetComp<Pawn_SurvivalToolAssignmentTracker>();
            if (tracker != null && tracker.forcedHandler.IsForced(tool))
            {
                originalLabel += $", {"ApparelForcedLower".Translate()}";
            }

            if (tool.InUse)
            {
                originalLabel += $", {"ToolInUse".Translate()}";

                if (SurvivalToolUtility.IsDebugLoggingEnabled && pawn?.jobs?.curJob != null)
                {
                    var job = pawn.jobs.curJob;
                    var relevantStats = SurvivalToolUtility.RelevantStatsFor(job.workGiverDef, job);
                    var bestTool = pawn.GetBestSurvivalTool(relevantStats);

                    if (bestTool == tool)
                    {
                        originalLabel += $" [job={job.def.defName}]";
                    }
                }
            }

            return originalLabel;
        }
    }
}