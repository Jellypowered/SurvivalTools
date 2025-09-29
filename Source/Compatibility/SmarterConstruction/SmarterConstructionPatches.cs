// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SmarterConstruction/SmarterConstructionPatches.cs
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;
using SurvivalTools.Helpers;

namespace SurvivalTools.Compatibility.SmarterConstruction
{
    internal static class SmarterConstructionPatches
    {
        internal static void Initialize(Harmony H)
        {
            if (!SmarterConstructionHelpers.Active) return;
            // No patches required currently.
        }
    }
}
