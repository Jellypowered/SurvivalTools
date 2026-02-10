// RimWorld 1.6 / C# 7.3
// Source/Power/CompProperties_PowerTool.cs
// Phase 12: Battery system for powered tools - comp properties

using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SurvivalTools
{
    public class CompProperties_PowerTool : CompProperties
    {
        // Battery capacity (typical range: 1000-10000)
        public float capacity = 6000f;

        // Charge consumed per work tick
        public float dischargePerWorkTick = 1f;

        // Stat-specific powered multipliers (defName → factor)
        // Applied when charge > 0; otherwise tool functions at unpowered baseline
        public List<PoweredStatMultiplier> poweredMultipliers = new List<PoweredStatMultiplier>();

        public CompProperties_PowerTool()
        {
            compClass = typeof(CompPowerTool);
        }
    }

    // Helper class for XML serialization of stat multipliers
    public class PoweredStatMultiplier
    {
        public string stat;
        public float factor = 1f;
    }
}
