// RimWorld 1.6 / C# 7.3
// Source/Compatibility/TDEnhancementPack/TDEnhancementPackPatches.cs
// Phase 10: Minimal stub (no Harmony patches required currently).

using HarmonyLib;

namespace SurvivalTools.Compatibility.TDEnhancementPack
{
    internal static class TDEnhancementPackPatches
    {
        internal static void Initialize(Harmony H)
        {
            if (!TDEnhancementPackHelpers.Active) return;
            // No patches currently required (roof gating now handled by core gating system).
        }
    }
}
