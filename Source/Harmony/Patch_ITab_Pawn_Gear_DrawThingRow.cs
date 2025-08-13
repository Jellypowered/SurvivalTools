using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(ITab_Pawn_Gear))]
    [HarmonyPatch("DrawThingRow")]
    public static class Patch_ITab_Pawn_Gear_DrawThingRow
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var accessor_SelPawnForGear = AccessTools.PropertyGetter(typeof(ITab_Pawn_Gear), "SelPawnForGear");

            bool injected = false;

            for (int i = 0; i < list.Count; i++)
            {
                var ins = list[i];

                // Always yield the original instruction
                yield return ins;

                if (injected)
                    continue;

                // We want the point where the label string local has just been stored (stloc.*)
                if (IsStoreLocal(ins.opcode))
                {
                    int localIndex;
                    if (!TryGetStoredLocalIndex(ins, out localIndex))
                        continue;

                    // After storing the original label, load it back, call our adjuster, and store it again.
                    // Stack: ... (after stloc)
                    //   ldloc     localIndex          // string label
                    //   ldarg.3                      // Thing thing (as in vanilla method)
                    //   ldarg.0                      // this
                    //   call      this.SelPawnForGear
                    //   call      AdjustDisplayedLabel(string, Thing, Pawn) -> string
                    //   stloc     localIndex

                    // ldloc localIndex
                    yield return LoadLocal(localIndex);

                    // ldarg.3 (thing)
                    yield return new CodeInstruction(OpCodes.Ldarg_3);

                    // this.SelPawnForGear
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, accessor_SelPawnForGear);

                    // call adjuster
                    yield return new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(typeof(Patch_ITab_Pawn_Gear_DrawThingRow), nameof(AdjustDisplayedLabel))
                    );

                    // stloc localIndex
                    yield return StoreLocal(localIndex);

                    injected = true;
                }
            }
        }

        // Helper returns a possibly-augmented label
        public static string AdjustDisplayedLabel(string originalLabel, Thing thing, Pawn pawn)
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

            return originalLabel;
        }

        // ----- IL Helpers -----

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
