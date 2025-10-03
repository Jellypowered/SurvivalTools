// RimWorld 1.6 / C# 7.3
// Source/Power/CompBatteryCell.cs
// Phase 12: Battery system v2 - battery item component

using System;
using RimWorld;
using Verse;

namespace SurvivalTools
{
    public enum BatteryTier
    {
        Basic,
        Industrial,
        Nuclear
    }

    /// <summary>
    /// Component for battery items that can be inserted into powered tools.
    /// Stores charge state and capacity for the battery.
    /// </summary>
    public class CompBatteryCell : ThingComp
    {
        private float charge;
        private float capacity;
        private BatteryTier tier;

        private CompProperties_BatteryCell Props => (CompProperties_BatteryCell)props;

        public float Charge => charge;
        public float Capacity => capacity;
        public BatteryTier Tier => tier;
        public float ChargePct => capacity <= 0 ? 0f : charge / capacity;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref charge, "charge", 0f);
            Scribe_Values.Look(ref capacity, "capacity", 6000f);
            Scribe_Values.Look(ref tier, "tier", BatteryTier.Basic);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            // Initialize from props if not loaded
            if (!respawningAfterLoad || capacity <= 0f)
            {
                capacity = Props.capacity;
                tier = Props.tier;

                // Initialize with full charge for new batteries
                if (!respawningAfterLoad)
                {
                    charge = capacity;
                }
            }
        }

        /// <summary>
        /// Add charge to the battery (for charger system)
        /// </summary>
        public void AddCharge(float amount)
        {
            if (amount <= 0f)
                return;

            charge = UnityEngine.Mathf.Clamp(charge + amount, 0f, capacity);
        }

        /// <summary>
        /// Remove charge from the battery
        /// </summary>
        public void ConsumeCharge(float amount)
        {
            if (amount <= 0f)
                return;

            charge = UnityEngine.Mathf.Clamp(charge - amount, 0f, capacity);
        }

        /// <summary>
        /// Set charge directly (for dev tools)
        /// </summary>
        public void SetCharge(float amount)
        {
            charge = UnityEngine.Mathf.Clamp(amount, 0f, capacity);
        }

        public override string CompInspectStringExtra()
        {
            var settings = SurvivalToolsMod.Settings;
            if (settings?.enablePoweredTools != true)
                return null;

            if (capacity <= 0)
                return null;

            return $"Charge: {ChargePct.ToStringPercent()} ({charge:F0} / {capacity:F0})";
        }
    }

    /// <summary>
    /// CompProperties for battery cells
    /// </summary>
    public class CompProperties_BatteryCell : CompProperties
    {
        public float capacity = 6000f;
        public BatteryTier tier = BatteryTier.Basic;

        public CompProperties_BatteryCell()
        {
            compClass = typeof(CompBatteryCell);
        }
    }
}
