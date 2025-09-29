// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SmarterDeconstruction/SmarterDeconstructionDebug.cs
using System.Collections.Generic;

namespace SurvivalTools.Compatibility.SmarterDeconstruction
{
    internal static class SmarterDeconstructionDebug
    {
        internal static void Contribute(Dictionary<string, string> kv)
        {
            if (kv == null) return;
            kv["SmarterDeconstruction.Active"] = SmarterDeconstructionHelpers.Active.ToString();
        }
    }
}
