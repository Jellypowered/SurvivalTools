﻿// Rimworld 1.6 / C# 7.3
// Patch_Mineable_Notify_TookMiningDamage.cs
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
        private static readonly FieldInfo FI_MiningYield =
            AccessTools.Field(typeof(StatDefOf), nameof(StatDefOf.MiningYield));

        private static readonly FieldInfo FI_DiggingYield =
            AccessTools.Field(typeof(ST_StatDefOf), nameof(ST_StatDefOf.MiningYieldDigging));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // No IL to work with — nothing to emit
            if (instructions == null)
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                {
                    const string key = "Mineable_TookMiningDamage_NullIL";
                    if (SurvivalToolUtility.ShouldLogWithCooldown(key))
                        Log.Warning("[SurvivalTools] Skipping Mineable.Notify_TookMiningDamage transpiler: null IL stream.");
                }
                yield break;
            }

            // If fields can’t be resolved, pass through original IL unchanged
            if (FI_MiningYield == null || FI_DiggingYield == null)
            {
                if (SurvivalToolUtility.IsDebugLoggingEnabled)
                {
                    const string key = "Mineable_TookMiningDamage_MissingFI";
                    if (SurvivalToolUtility.ShouldLogWithCooldown(key))
                        Log.Warning("[SurvivalTools] Skipping MiningYield→DiggingYield swap: FieldInfo not found.");
                }
                foreach (var ins in instructions) yield return ins;
                yield break;
            }

            bool patchedAny = false;

            foreach (var ins in instructions)
            {
                if (ins != null && ins.opcode == OpCodes.Ldsfld && IsSameField(ins.operand, FI_MiningYield))
                {
                    var repl = new CodeInstruction(OpCodes.Ldsfld, FI_DiggingYield)
                    {
                        labels = ins.labels,
                        blocks = ins.blocks
                    };
                    yield return repl;
                    patchedAny = true;
                }
                else
                {
                    yield return ins;
                }
            }

            if (!patchedAny && SurvivalToolUtility.IsDebugLoggingEnabled)
            {
                const string key = "Mineable_TookMiningDamage_NoSwap";
                if (SurvivalToolUtility.ShouldLogWithCooldown(key))
                    Log.Warning("[SurvivalTools] Mineable.Notify_TookMiningDamage transpiler: no MiningYield field load found to replace.");
            }
        }

        private static bool IsSameField(object operand, FieldInfo target)
        {
            var fi = operand as FieldInfo;
            if (fi == null || target == null) return false;
            return ReferenceEquals(fi, target)
                   || (fi.Name == target.Name && fi.DeclaringType == target.DeclaringType);
        }
    }
}
