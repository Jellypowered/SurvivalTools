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
/* // ----- IL Helpers -----

private static bool IsStoreLocal(OpCode op)
{
    return op == OpCodes.Stloc || op == OpCodes.Stloc_S
        || op == OpCodes.Stloc_0 || op == OpCodes.Stloc_1
        || op == OpCodes.Stloc_2 || op == OpCodes.Stloc_3;
}

private static bool TryGetStoredLocalIndex(CodeInstruction ins, out int index)
{
    index = -1;
    var op = ins.opcode;

    if (op == OpCodes.Stloc_0) { index = 0; return true; }
    if (op == OpCodes.Stloc_1) { index = 1; return true; }
    if (op == OpCodes.Stloc_2) { index = 2; return true; }
    if (op == OpCodes.Stloc_3) { index = 3; return true; }

    if (op == OpCodes.Stloc || op == OpCodes.Stloc_S)
    {
        // operand can be LocalBuilder or a raw index (byte/int)
        if (ins.operand is LocalBuilder lb)
        {
            index = lb.LocalIndex;
            return true;
        }
        if (ins.operand is byte b)
        {
            index = b;
            return true;
        }
        if (ins.operand is int i)
        {
            index = i;
            return true;
        }
    }

    return false;
}

private static CodeInstruction LoadLocal(int index)
{
    switch (index)
    {
        case 0: return new CodeInstruction(OpCodes.Ldloc_0);
        case 1: return new CodeInstruction(OpCodes.Ldloc_1);
        case 2: return new CodeInstruction(OpCodes.Ldloc_2);
        case 3: return new CodeInstruction(OpCodes.Ldloc_3);
        default: return new CodeInstruction(OpCodes.Ldloc_S, (byte)index);
    }
}

private static CodeInstruction StoreLocal(int index)
{
    switch (index)
    {
        case 0: return new CodeInstruction(OpCodes.Stloc_0);
        case 1: return new CodeInstruction(OpCodes.Stloc_1);
        case 2: return new CodeInstruction(OpCodes.Stloc_2);
        case 3: return new CodeInstruction(OpCodes.Stloc_3);
        default: return new CodeInstruction(OpCodes.Stloc_S, (byte)index);
    }
}
}
}
*/