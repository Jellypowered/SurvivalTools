// Rimworld 1.6 / C# 7.3
// Source/Harmony/Patch_ITab_Pawn_Gear_DrawThingRow.cs
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
                    yield return new CodeInstruction(OpCodes.Ldarg_0); // this (ITab_Pawn_Gear)
                    yield return new CodeInstruction(OpCodes.Call, MI_SelPawnForGear); // pawn
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

        public static string AdjustDisplayedLabel(string originalLabel, Thing thing, Pawn pawn)
        {
            if (string.IsNullOrEmpty(originalLabel)) originalLabel = string.Empty;
            if (thing == null) return originalLabel;

            var tool = thing as SurvivalTool;
            bool isToolStuff = thing.def?.IsToolStuff() == true;

            // Forced handler suffix
            try
            {
                var tracker = pawn?.GetComp<Pawn_SurvivalToolAssignmentTracker>();
                if (tracker != null)
                {
                    Thing physicalForForced =
                        (tool is VirtualSurvivalTool v && v.SourceThing != null) ? v.SourceThing : thing;

                    if (tracker.forcedHandler?.IsForced(physicalForForced) == true)
                        originalLabel += $", {"ApparelForcedLower".Translate()}";
                }
            }
            catch { }

            bool inUse = false;
            try
            {
                var job = pawn?.jobs?.curJob;
                if (job != null)
                {
                    var required = SurvivalToolUtility.RelevantStatsFor(job.workGiverDef, job);
                    if (!required.NullOrEmpty())
                    {
                        if (isToolStuff)
                        {
                            // Wrap the stack in a virtual tool and check if it’s marked as in-use
                            var vtool = VirtualSurvivalTool.FromThing(thing);
                            if (vtool != null && SurvivalToolUtility.IsToolInUse(vtool))
                                inUse = true;
                        }
                        else
                        {
                            var best = pawn.GetBestSurvivalTool(required);
                            if (best != null)
                            {
                                var bestBacking = SurvivalToolUtility.BackingThing(best, pawn);
                                if (ReferenceEquals(bestBacking, thing) || ReferenceEquals(best, thing))
                                    inUse = true;
                            }
                        }
                    }
                }
            }
            catch
            {
                inUse = false;
            }

            if (inUse)
                originalLabel += $", {"ToolInUse".Translate()}";

            return originalLabel;
        }
    }
}
