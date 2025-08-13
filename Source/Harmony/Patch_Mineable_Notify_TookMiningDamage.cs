using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(Mineable))]
    [HarmonyPatch(nameof(Mineable.Notify_TookMiningDamage))]
    public static class Patch_Mineable_Notify_TookMiningDamage
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();

            // Cache FieldInfos once
            FieldInfo miningYieldField = AccessTools.Field(typeof(StatDefOf), nameof(StatDefOf.MiningYield));
            FieldInfo diggingYieldField = AccessTools.Field(typeof(ST_StatDefOf), nameof(ST_StatDefOf.MiningYieldDigging));

            for (int i = 0; i < list.Count; i++)
            {
                var ins = list[i];

                if (ins.opcode == OpCodes.Ldsfld && Equals(ins.operand, miningYieldField))
                {
                    ins.operand = diggingYieldField;
                }

                yield return ins;
            }
        }
    }
}
