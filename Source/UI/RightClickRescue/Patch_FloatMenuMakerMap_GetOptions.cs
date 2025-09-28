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
        static void Postfix(List<Pawn> selectedPawns, Vector3 clickPos, ref FloatMenuContext context, ref List<FloatMenuOption> __result)
        {
            try
            {
                if (__result == null) { if (Prefs.DevMode) Log.Message("[ST.RightClick] Fallback: no base options list."); return; }
                // Avoid duplicates if provider already added rescue entries (detect via marker substring)
                for (int i = 0; i < __result.Count; i++)
                {
                    var lab = __result[i]?.Label;
                    if (!string.IsNullOrEmpty(lab) && lab.IndexOf("(will fetch", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    { if (Prefs.DevMode) Log.Message("[ST.RightClick] Fallback: provider already supplied rescue option."); return; }
                }

                context = context ?? new FloatMenuContext(selectedPawns, clickPos, Find.CurrentMap);
                if (context == null) return;
                var pawn = context.FirstSelectedPawn;
                if (pawn == null) return;
                var s = SurvivalToolsMod.Settings; if (s == null) return;
                if (!s.enableRightClickRescue) { if (Prefs.DevMode) Log.Message("[ST.RightClick] Fallback: feature disabled."); return; }
                if (!(s.hardcoreMode || s.extraHardcoreMode)) { if (Prefs.DevMode) Log.Message("[ST.RightClick] Fallback: not hardcore/nightmare."); return; }

                var extra = new List<FloatMenuOption>(4);
                RightClickRescue.RightClickRescueBuilder.TryAddRescueOptions(pawn, context, extra);
                if (extra.Count == 0) { if (Prefs.DevMode) Log.Message("[ST.RightClick] Fallback: no rescue options built."); return; }

                if (Prefs.DevMode)
                    Log.Message($"[ST.RightClick] Fallback appended {extra.Count} rescue option(s) at {context.ClickedCell}.");
                __result.AddRange(extra);
            }
            catch (System.Exception ex)
            {
                Log.Warning("[ST.RightClick] Fallback postfix exception: " + ex);
            }
        }
    }
}
