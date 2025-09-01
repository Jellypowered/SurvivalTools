using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(Mineable), nameof(Mineable.Notify_TookMiningDamage))]
    public static class Patch_Mineable_Notify_TookMiningDamage
    {
        private static readonly FieldInfo FI_MiningYield = AccessTools.Field(typeof(StatDefOf), nameof(StatDefOf.MiningYield));
        private static readonly FieldInfo FI_DiggingYield = AccessTools.Field(typeof(ST_StatDefOf), nameof(ST_StatDefOf.MiningYieldDigging));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            bool patched = false;

            for (int i = 0; i < codes.Count; i++)
            {
                var instruction = codes[i];

                if (instruction.opcode == OpCodes.Ldsfld && instruction.operand.Equals(FI_MiningYield))
                {
                    yield return new CodeInstruction(OpCodes.Ldsfld, FI_DiggingYield);
                    patched = true;
                }
                else
                {
                    yield return instruction;
                }
            }

            if (!patched && SurvivalToolUtility.IsDebugLoggingEnabled)
            {
                Log.Warning("[SurvivalTools] Failed to patch Mineable.Notify_TookMiningDamage: ldsfld for MiningYield not found.");
            }
        }
    }
}
