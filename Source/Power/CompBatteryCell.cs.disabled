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
    /// Stores charge state, capacity, and wear/degradation tracking.
    /// Phase 12.2: Added charge cycle tracking and battery wear.
    /// </summary>
    public class CompBatteryCell : ThingComp
    {
        private float charge;
        private float capacity;
        private BatteryTier tier;
        private int chargeCycles; // Phase 12.2: Track number of full charge cycles
        private bool wasFullyCharged; // Track if battery was at 100% to detect charge cycles

        private CompProperties_BatteryCell Props => (CompProperties_BatteryCell)props;

        public float Charge => charge;
        public float Capacity => capacity;
        public BatteryTier Tier => tier;
        public float ChargePct => capacity <= 0 ? 0f : charge / capacity;
        public int ChargeCycles => chargeCycles;
        public int MaxChargeCycles => Props.maxChargeCycles;
        public float WearPct => MaxChargeCycles <= 0 ? 0f : (float)chargeCycles / MaxChargeCycles;
        public bool IsWornOut => chargeCycles >= MaxChargeCycles;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref charge, "charge", 0f);
            Scribe_Values.Look(ref capacity, "capacity", 6000f);
            Scribe_Values.Look(ref tier, "tier", BatteryTier.Basic);
            Scribe_Values.Look(ref chargeCycles, "chargeCycles", 0);
            Scribe_Values.Look(ref wasFullyCharged, "wasFullyCharged", false);
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
        /// Add charge to the battery (for charger system).
        /// Phase 12.2: Tracks charge cycles for battery wear.
        /// </summary>
        public void AddCharge(float amount)
        {
            if (amount <= 0f)
                return;

            float oldCharge = charge;
            charge = UnityEngine.Mathf.Clamp(charge + amount, 0f, capacity);

            // Phase 12.2: Track charge cycles for wear
            // Nuclear batteries don't wear out
            if (tier != BatteryTier.Nuclear && MaxChargeCycles > 0)
            {
                // Check if we just reached full charge from not-full
                bool nowFullyCharged = charge >= capacity;
                if (nowFullyCharged && !wasFullyCharged)
                {
                    chargeCycles++;

                    // Log wear milestone (only at 25%, 50%, 75%, 90%, 100%)
                    if (Prefs.DevMode)
                    {
                        float wearPct = WearPct;
                        if (wearPct >= 0.25f && (wearPct - 0.01f) < 0.25f ||
                            wearPct >= 0.50f && (wearPct - 0.01f) < 0.50f ||
                            wearPct >= 0.75f && (wearPct - 0.01f) < 0.75f ||
                            wearPct >= 0.90f && (wearPct - 0.01f) < 0.90f ||
                            IsWornOut)
                        {
                            Log.Message($"[SurvivalTools] {parent.LabelShort}: {chargeCycles}/{MaxChargeCycles} cycles ({wearPct.ToStringPercent()} wear)");
                        }
                    }
                }
                wasFullyCharged = nowFullyCharged;
            }
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

            string result = $"Charge: {ChargePct.ToStringPercent()} ({charge:F0} / {capacity:F0})";

            // Phase 12.2: Show battery wear info (excluding nuclear)
            if (tier != BatteryTier.Nuclear && MaxChargeCycles > 0)
            {
                result += $"\nCycles: {chargeCycles} / {MaxChargeCycles}";

                if (IsWornOut)
                {
                    result += " (worn out)";
                }
                else if (WearPct >= 0.75f)
                {
                    result += " (aging)";
                }
            }

            return result;
        }
    }

    /// <summary>
    /// CompProperties for battery cells.
    /// Phase 12.2: Added maxChargeCycles for battery wear tracking.
    /// </summary>
    public class CompProperties_BatteryCell : CompProperties
    {
        public float capacity = 6000f;
        public BatteryTier tier = BatteryTier.Basic;
        public int maxChargeCycles = 150; // Phase 12.2: Battery lifespan (0 = infinite)

        public CompProperties_BatteryCell()
        {
            compClass = typeof(CompBatteryCell);
        }
    }
}
