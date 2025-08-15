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
                Log.Warning("[SurvivalTools] Failed to patch ITab_Pawn_Gear.DrawThingRow: Widgets.Label call not found.");
            }
        }

        // unchanged logic—only called at the draw site now, so it can't be overwritten later
        public static string AdjustDisplayedLabel(string originalLabel, Thing thing, Pawn pawn)
        {
            try
            {
                if (thing is SurvivalTool tool)
                {
                    // Forced
                    var tracker = pawn.GetComp<Pawn_SurvivalToolAssignmentTracker>();
                    if (tracker != null && tracker.forcedHandler.IsForced(tool))
                    {
                        originalLabel += $", {"ApparelForcedLower".Translate()}";
                    }

                    // In use
                    if (tool.InUse)
                    {
                        originalLabel += $", {"ToolInUse".Translate()}";
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Warning($"[SurvivalTools] AdjustDisplayedLabel error: {e}");
            }

            return originalLabel;
        }
    }
}