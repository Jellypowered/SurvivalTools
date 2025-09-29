// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SmarterConstruction/SmarterConstructionDebug.cs
using System.Collections.Generic;

namespace SurvivalTools.Compatibility.SmarterConstruction
{
    internal static class SmarterConstructionDebug
    {
        internal static void Contribute(Dictionary<string, string> kv)
        {
            if (kv == null) return;
            kv["SC.Active"] = SmarterConstructionHelpers.Active.ToString();
        }
    }
}
