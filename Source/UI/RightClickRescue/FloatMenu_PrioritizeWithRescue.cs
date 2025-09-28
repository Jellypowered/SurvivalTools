// RimWorld 1.6 / C# 7.3
// Source/UI/RightClickRescue/FloatMenu_PrioritizeWithRescue.cs
// Postfix on FloatMenuMakerMap.GetOptions to inject enabled "Prioritize <job> (will fetch <tool>)" rescue options
// when vanilla would show a disabled prioritized job due to tool gating in Hardcore/Nightmare.

using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using SurvivalTools.Assign;
using SurvivalTools.Gating;
using SurvivalTools.Helpers;
using SurvivalTools.Scoring;

namespace SurvivalTools.UI.RightClickRescue
{
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    internal static class FloatMenu_PrioritizeWithRescue
    {
        // Postfix signature (1.6) – context passed by ref
        static void Postfix(List<Pawn> selectedPawns, Vector3 clickPos, ref FloatMenuContext context, ref List<FloatMenuOption> __result)
        {
            try
            {
                var s = SurvivalToolsMod.Settings;
                if (context == null || __result == null || selectedPawns == null || selectedPawns.Count == 0) return;
                if (s == null || !s.enableRightClickRescue) return;
                if (!(s.hardcoreMode || s.extraHardcoreMode)) return; // Hardcore/Nightmare only
                if (context.IsMultiselect) return; // keep v1 simple

                // Duplicate guard: if provider (or another pass) already added a rescue option, skip.
                for (int i = 0; i < __result.Count; i++)
                {
                    var lab = __result[i]?.Label;
                    if (!string.IsNullOrEmpty(lab) && lab.IndexOf("(will fetch", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return;
                }

                var pawn = context.FirstSelectedPawn;
                if (pawn == null || pawn.Map != Find.CurrentMap || pawn.Downed || !pawn.CanTakeOrder) return;
                if (!pawn.RaceProps?.Humanlike ?? true) return;

                RightClickRescueBuilder.TryAddRescueOptions(pawn, context, __result);
            }
            catch (Exception ex)
            {
                Log.Warning("[SurvivalTools.RightClickRescue] Postfix exception: " + ex);
            }
        }
    }
}
