// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SmarterDeconstruction/SmarterDeconstructionPatches.cs
using HarmonyLib;

namespace SurvivalTools.Compatibility.SmarterDeconstruction
{
    internal static class SmarterDeconstructionPatches
    {
        internal static void Initialize(Harmony H)
        {
            if (!SmarterDeconstructionHelpers.Active) return;
            // No patches needed currently.
        }
    }
}
