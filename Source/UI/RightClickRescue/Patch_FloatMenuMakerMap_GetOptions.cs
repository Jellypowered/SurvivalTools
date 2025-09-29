// RimWorld 1.6 / C# 7.3
// Source/UI/RightClickRescue/Patch_FloatMenuMakerMap_GetOptions.cs
// Fallback Harmony postfix: if provider system fails or other mods strip providers, ensure rescue options still appear.

using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SurvivalTools.UI.RightClickRescue
{
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    public static class Patch_FloatMenuMakerMap_GetOptions
    {
        static void Prefix(List<Pawn> selectedPawns, Vector3 clickPos, ref FloatMenuContext context)
        {
            try
            {
                var pawn = (selectedPawns != null && selectedPawns.Count > 0) ? selectedPawns[0] : null;
                if (pawn != null)
                    Provider_STPrioritizeWithRescue.BeginClick(pawn, new IntVec3((int)clickPos.x, 0, (int)clickPos.z));
            }
            catch { }
        }

        static void Postfix(List<Pawn> selectedPawns, Vector3 clickPos, ref FloatMenuContext context, ref List<FloatMenuOption> __result)
        {
            try
            {
                if (__result == null) return;
                // Avoid duplicates if provider already satisfied
                for (int i = 0; i < __result.Count; i++)
                {
                    var lab = __result[i]?.Label;
                    if (!string.IsNullOrEmpty(lab) && lab.IndexOf("(will fetch", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return;
                }

                context = context ?? new FloatMenuContext(selectedPawns, clickPos, Find.CurrentMap);
                if (context == null) return;
                var pawn = context.FirstSelectedPawn;
                if (pawn == null) return;
                var s = SurvivalToolsMod.Settings; if (s == null) return;
                if (!s.enableRightClickRescue) return;
                if (!(s.hardcoreMode || s.extraHardcoreMode)) return;
                if (Provider_STPrioritizeWithRescue.AlreadySatisfiedThisClick()) return;

                var extra = new List<FloatMenuOption>(4);
                RightClickRescue.RightClickRescueBuilder.TryAddRescueOptions(pawn, context, extra);
                if (extra.Count == 0) return;
                Provider_STPrioritizeWithRescue.NotifyOptionAdded();
                __result.AddRange(extra);
            }
            catch (System.Exception ex)
            {
                Log.Warning("[ST.RightClick] Fallback postfix exception: " + ex);
            }
            finally
            {
                Provider_STPrioritizeWithRescue.EndClick();
            }
        }
    }
}
