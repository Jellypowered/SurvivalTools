// RimWorld 1.6 / C# 7.3
// Source/Power/CompPowerTool.cs
// Phase 12: Battery system for powered tools - comp implementation

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SurvivalTools
{
    public class CompPowerTool : ThingComp
    {
        private float charge;
        private float capacity;
        private float dischargePerWorkTick;
        private Dictionary<string, float> poweredMultipliers;
        private int chargeBucket = -1; // 0..20 for 5% steps, -1 = uninitialized

        // Phase 12: Battery v2 - inserted battery item
        private Thing batteryItem;

        private CompProperties_PowerTool Props => (CompProperties_PowerTool)props;

        public float Charge => batteryItem != null ? GetBatteryCharge() : charge;
        public float Capacity => batteryItem != null ? GetBatteryCapacity() : capacity;
        public bool HasCharge => Charge > 0f;
        public float ChargePct => Capacity <= 0 ? 0f : Charge / Capacity;
        public Thing BatteryItem => batteryItem;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref charge, "charge", 0f);
            Scribe_Values.Look(ref capacity, "capacity", 6000f);
            Scribe_Values.Look(ref dischargePerWorkTick, "dischargePerWorkTick", 1f);
            Scribe_Values.Look(ref chargeBucket, "chargeBucket", -1);
            Scribe_References.Look(ref batteryItem, "batteryItem");

            // Don't save poweredMultipliers - rebuild from props
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            // Initialize from props if not loaded
            if (!respawningAfterLoad || capacity <= 0f)
            {
                capacity = Props.capacity;
                dischargePerWorkTick = Props.dischargePerWorkTick;

                // Initialize with full charge for new items
                if (!respawningAfterLoad)
                {
                    charge = capacity;
                }
            }

            // Always rebuild multipliers from props (for compatibility)
            RebuildMultipliers();

            // Update bucket
            RecalculateBucket();
        }

        private void RebuildMultipliers()
        {
            poweredMultipliers = new Dictionary<string, float>();

            if (Props.poweredMultipliers != null)
            {
                foreach (var mult in Props.poweredMultipliers)
                {
                    if (!string.IsNullOrEmpty(mult.stat))
                    {
                        poweredMultipliers[mult.stat] = mult.factor;
                    }
                }
            }
        }

        // Phase 12: Battery v2 helper methods

        private float GetBatteryCharge()
        {
            if (batteryItem == null) return 0f;
            var batteryComp = batteryItem.TryGetComp<CompBatteryCell>();
            return batteryComp?.Charge ?? 0f;
        }

        private float GetBatteryCapacity()
        {
            if (batteryItem == null) return capacity;
            var batteryComp = batteryItem.TryGetComp<CompBatteryCell>();
            return batteryComp?.Capacity ?? capacity;
        }

        /// <summary>
        /// Check if a battery can be inserted into this tool
        /// </summary>
        public bool CanAcceptBattery(Thing battery)
        {
            if (battery == null) return false;
            if (batteryItem != null) return false; // Already has a battery

            var batteryComp = battery.TryGetComp<CompBatteryCell>();
            if (batteryComp == null) return false;

            // For now, accept any battery (tier validation could be added here)
            return true;
        }

        /// <summary>
        /// Try to insert a battery into this tool
        /// Returns true if successful
        /// </summary>
        public bool TryInsertBattery(Thing battery)
        {
            if (!CanAcceptBattery(battery))
                return false;

            // Remove from current container
            if (battery.holdingOwner != null)
            {
                battery.holdingOwner.TryDrop(battery, ThingPlaceMode.Direct, out Thing _);
            }

            batteryItem = battery;
            battery.DeSpawn();
            RecalculateBucket();

            if (Prefs.DevMode)
            {
                ST_Logging.LogDebug($"[Power] Inserted battery into {parent.LabelShort}: {battery.LabelShort} ({batteryItem.TryGetComp<CompBatteryCell>()?.ChargePct.ToStringPercent()})");
            }

            return true;
        }

        /// <summary>
        /// Eject the current battery from this tool
        /// Returns the ejected battery or null
        /// </summary>
        public Thing EjectBattery()
        {
            if (batteryItem == null)
                return null;

            Thing ejected = batteryItem;
            batteryItem = null;
            RecalculateBucket();

            // Spawn the battery at the tool's location
            if (parent.Spawned && parent.Map != null)
            {
                GenPlace.TryPlaceThing(ejected, parent.Position, parent.Map, ThingPlaceMode.Near);
            }

            if (Prefs.DevMode)
            {
                ST_Logging.LogDebug($"[Power] Ejected battery from {parent.LabelShort}: {ejected.LabelShort}");
            }

            return ejected;
        }

        /// <summary>
        /// Get charge bucket in 5% increments (0..20)
        /// Used for cache keys and UI throttling
        /// </summary>
        public int GetChargeBucket5()
        {
            if (chargeBucket < 0)
            {
                RecalculateBucket();
            }
            return chargeBucket;
        }

        private void RecalculateBucket()
        {
            int oldBucket = chargeBucket;
            chargeBucket = capacity <= 0 ? 0 : UnityEngine.Mathf.Clamp((int)(ChargePct * 20f), 0, 20);

            // Dirty map mesh if bucket changed (for iTab refresh)
            if (oldBucket >= 0 && oldBucket != chargeBucket && parent.Spawned && parent.Map != null)
            {
                parent.DirtyMapMesh(parent.Map);
            }
        }

        /// <summary>
        /// Get the powered multiplier for a specific stat
        /// Returns 1.0 if stat not in dictionary or tool has no charge
        /// </summary>
        public float GetPoweredMultiplier(StatDef stat)
        {
            if (stat == null || !HasCharge)
                return 1f;

            if (poweredMultipliers != null && poweredMultipliers.TryGetValue(stat.defName, out float factor))
            {
                return factor;
            }

            return 1f;
        }

        /// <summary>
        /// Check if this tool provides powered benefit for the given stat
        /// </summary>
        public bool IsPoweredStat(StatDef stat)
        {
            if (stat == null || poweredMultipliers == null)
                return false;

            return poweredMultipliers.ContainsKey(stat.defName);
        }

        /// <summary>
        /// Called during work tick to discharge battery
        /// Only call when work actually happens (follow wear patterns)
        /// </summary>
        public void NotifyWorkTick(StatDef stat)
        {
            // Early out if disabled or no charge
            var settings = SurvivalToolsMod.Settings;
            if (settings?.enablePoweredTools != true || !HasCharge)
                return;

            // Only discharge if this stat benefits from power
            if (!IsPoweredStat(stat))
                return;

            // Discharge (from battery if present, otherwise internal charge)
            if (batteryItem != null)
            {
                var batteryComp = batteryItem.TryGetComp<CompBatteryCell>();
                if (batteryComp != null)
                {
                    batteryComp.ConsumeCharge(dischargePerWorkTick);
                }
            }
            else
            {
                charge = UnityEngine.Mathf.Clamp(charge - dischargePerWorkTick, 0f, capacity);
            }

            // Recalc bucket (will dirty mesh if changed)
            RecalculateBucket();

            // Dev logging (throttled)
            if (Prefs.DevMode && UnityEngine.Random.value < 0.001f) // 0.1% sample rate
            {
                ST_Logging.LogDebug($"[Power] {parent.LabelShort} discharged to {Charge:F1}/{Capacity:F1} ({ChargePct:P0}) for {stat.defName}");
            }
        }

        /// <summary>
        /// Add charge (for future charger system)
        /// </summary>
        public void AddCharge(float amount)
        {
            if (amount <= 0f)
                return;

            if (batteryItem != null)
            {
                var batteryComp = batteryItem.TryGetComp<CompBatteryCell>();
                if (batteryComp != null)
                {
                    batteryComp.AddCharge(amount);
                }
            }
            else
            {
                charge = UnityEngine.Mathf.Clamp(charge + amount, 0f, capacity);
            }

            RecalculateBucket();
        }

        /// <summary>
        /// Set charge directly (for dev tools)
        /// </summary>
        public void SetCharge(float amount)
        {
            if (batteryItem != null)
            {
                var batteryComp = batteryItem.TryGetComp<CompBatteryCell>();
                if (batteryComp != null)
                {
                    batteryComp.SetCharge(amount);
                }
            }
            else
            {
                charge = UnityEngine.Mathf.Clamp(amount, 0f, capacity);
            }

            RecalculateBucket();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);

            // If tool is being destroyed and has a battery, drop the battery
            if (batteryItem != null && previousMap != null)
            {
                GenPlace.TryPlaceThing(batteryItem, parent.Position, previousMap, ThingPlaceMode.Near);
                batteryItem = null;
            }

            CheckForDestruction();
        }

        public void CheckForDestruction()
        {
            // Call this from PostDeSpawn if parent is destroyed
            // Nuclear hazard (if enabled and destroyed)
            var settings = SurvivalToolsMod.Settings;
            if (settings?.enableNuclearHazards == true &&
                parent?.def?.defName == "ST_Battery_Nuclear" &&
                parent.Destroyed && parent.Map != null)
            {
                TryTriggerNuclearHazard(parent.Map);
            }
        }

        private void TryTriggerNuclearHazard(Map map)
        {
            try
            {
                if (parent?.Position != null && map != null)
                {
                    // Small explosion with heat
                    GenExplosion.DoExplosion(
                        parent.Position,
                        map,
                        2.9f, // Small radius
                        DamageDefOf.Flame,
                        parent,
                        postExplosionSpawnThingDef: ThingDefOf.Filth_Ash,
                        postExplosionSpawnChance: 0.5f
                    );

                    if (Prefs.DevMode)
                    {
                        ST_Logging.LogDebug($"[Power] Nuclear battery hazard triggered at {parent.Position}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (Prefs.DevMode)
                {
                    ST_Logging.LogError($"[Power] Nuclear hazard failed: {ex.Message}");
                }
            }
        }

        public override string CompInspectStringExtra()
        {
            var settings = SurvivalToolsMod.Settings;
            if (settings?.enablePoweredTools != true)
                return null;

            if (capacity <= 0)
                return null;

            string status = HasCharge ? "ST_Power_Charged" : "ST_Power_Empty";
            return status.Translate(ChargePct.ToStringPercent());
        }
    }
}
