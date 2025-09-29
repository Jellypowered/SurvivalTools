// RimWorld 1.6 / C# 7.3
// Source/Compatibility/TDEnhancementPack/TDEnhancementPackDebug.cs
// Optional diagnostics contributor (Phase 10 style).

using System.Collections.Generic;

namespace SurvivalTools.Compatibility.TDEnhancementPack
{
    internal static class TDEnhancementPackDebug
    {
        internal static void Contribute(Dictionary<string, string> kv)
        {
            if (kv == null) return;
            kv["TDEnhancementPack.Active"] = TDEnhancementPackHelpers.Active.ToString();
        }
    }
}
