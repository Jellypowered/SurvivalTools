// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SeparateTreeChopping/STCPatches_Phase10.cs
// Phase 10: Minimal stub - currently no Harmony patches required for STC helper.

using HarmonyLib;

namespace SurvivalTools.Compatibility.SeparateTreeChopping
{
    internal static class STCPatches
    {
        internal static void Initialize(Harmony H)
        {
            if (!STCHelpers.Active) return;
            // No patches currently required.
        }
    }
}
