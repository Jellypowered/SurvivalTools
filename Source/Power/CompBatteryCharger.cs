// RimWorld 1.6 / C# 7.3
// Source/Power/CompBatteryCharger.cs
// Phase 12.2: Battery charging station component

using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace SurvivalTools
{
    /// <summary>
    /// Component for battery charger buildings.
    /// Allows pawns to insert batteries for recharging using building's power connection.
    /// </summary>
    public class CompBatteryCharger : ThingComp
    {
        private List<Thing> chargingBatteries = new List<Thing>();
        private float chargeRate;
        private int maxBatteries;

        private CompProperties_BatteryCharger Props => (CompProperties_BatteryCharger)props;
        private CompPowerTrader PowerComp => parent.GetComp<CompPowerTrader>();
        private CompFlickable FlickComp => parent.GetComp<CompFlickable>();

        public List<Thing> ChargingBatteries => chargingBatteries;
        public int MaxBatteries => maxBatteries;
        public bool HasRoom => chargingBatteries.Count < maxBatteries;
        public bool IsPowered => PowerComp?.PowerOn ?? false;
        public bool IsActive => IsPowered && (FlickComp?.SwitchIsOn ?? true);

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref chargingBatteries, "chargingBatteries", LookMode.Reference);
            Scribe_Values.Look(ref chargeRate, "chargeRate", 50f);
            Scribe_Values.Look(ref maxBatteries, "maxBatteries", 4);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                chargingBatteries?.RemoveAll(b => b == null || b.Destroyed);
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (!respawningAfterLoad)
            {
                chargeRate = Props.chargeRate;
                maxBatteries = Props.maxBatteries;
            }

            if (chargingBatteries == null)
                chargingBatteries = new List<Thing>();
        }

        public override void CompTick()
        {
            base.CompTick();

            if (!IsActive)
                return;

            // Charge all inserted batteries
            for (int i = chargingBatteries.Count - 1; i >= 0; i--)
            {
                var battery = chargingBatteries[i];
                if (battery == null || battery.Destroyed)
                {
                    chargingBatteries.RemoveAt(i);
                    continue;
                }

                var batteryComp = battery.TryGetComp<CompBatteryCell>();
                if (batteryComp == null)
                {
                    chargingBatteries.RemoveAt(i);
                    continue;
                }

                // Don't charge nuclear batteries
                if (batteryComp.Tier == BatteryTier.Nuclear)
                    continue;

                // If battery is full, no need to charge
                if (batteryComp.ChargePct >= 1f)
                    continue;

                // Add charge
                batteryComp.AddCharge(chargeRate);
            }
        }

        /// <summary>
        /// Try to insert a battery into the charger.
        /// </summary>
        public bool TryInsertBattery(Thing battery)
        {
            if (battery == null)
                return false;

            if (chargingBatteries.Count >= maxBatteries)
            {
                Messages.Message("ST_BatteryCharger_Full".Translate(), parent, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            var batteryComp = battery.TryGetComp<CompBatteryCell>();
            if (batteryComp == null)
            {
                Messages.Message("ST_BatteryCharger_NotABattery".Translate(), parent, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            // Nuclear batteries can't be charged
            if (batteryComp.Tier == BatteryTier.Nuclear)
            {
                Messages.Message("ST_BatteryCharger_CannotChargeNuclear".Translate(), parent, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            // Already charging this battery?
            if (chargingBatteries.Contains(battery))
            {
                Messages.Message("ST_BatteryCharger_AlreadyCharging".Translate(), parent, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            // Already full?
            if (batteryComp.ChargePct >= 1f)
            {
                Messages.Message("ST_BatteryCharger_AlreadyFull".Translate(), parent, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            // Remove from map and add to charger
            if (battery.Spawned)
            {
                battery.DeSpawn();
            }

            chargingBatteries.Add(battery);
            return true;
        }

        /// <summary>
        /// Eject a battery from the charger.
        /// </summary>
        public Thing EjectBattery(Thing battery)
        {
            if (battery == null || !chargingBatteries.Contains(battery))
                return null;

            chargingBatteries.Remove(battery);
            return battery;
        }

        /// <summary>
        /// Eject battery at index.
        /// </summary>
        public Thing EjectBatteryAt(int index)
        {
            if (index < 0 || index >= chargingBatteries.Count)
                return null;

            var battery = chargingBatteries[index];
            chargingBatteries.RemoveAt(index);
            return battery;
        }

        /// <summary>
        /// Eject all batteries.
        /// </summary>
        public List<Thing> EjectAllBatteries()
        {
            var ejected = new List<Thing>(chargingBatteries);
            chargingBatteries.Clear();
            return ejected;
        }

        public override string CompInspectStringExtra()
        {
            var settings = SurvivalToolsMod.Settings;
            if (settings?.enablePoweredTools != true)
                return null;

            var sb = new StringBuilder();

            sb.AppendLine($"Charging: {chargingBatteries.Count} / {maxBatteries}");

            if (!IsPowered)
            {
                sb.AppendLine("No power");
            }
            else if (FlickComp != null && !FlickComp.SwitchIsOn)
            {
                sb.AppendLine("Switched off");
            }
            else
            {
                sb.AppendLine($"Charge rate: {chargeRate:F0} / tick");
            }

            if (chargingBatteries.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Batteries:");
                foreach (var battery in chargingBatteries)
                {
                    if (battery == null) continue;
                    var batteryComp = battery.TryGetComp<CompBatteryCell>();
                    if (batteryComp != null)
                    {
                        sb.AppendLine($"  {battery.LabelShort}: {batteryComp.ChargePct.ToStringPercent()}");
                    }
                }
            }

            return sb.ToString().TrimEndNewlines();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            var settings = SurvivalToolsMod.Settings;
            if (settings?.enablePoweredTools != true)
                yield break;

            // Insert battery gizmo
            if (HasRoom)
            {
                yield return new Command_Action
                {
                    defaultLabel = "ST_BatteryCharger_Insert".Translate(),
                    defaultDesc = "ST_BatteryCharger_InsertDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/InsertBattery", true),
                    action = () => ShowInsertBatteryMenu()
                };
            }

            // Eject all gizmo
            if (chargingBatteries.Any())
            {
                yield return new Command_Action
                {
                    defaultLabel = "ST_BatteryCharger_EjectAll".Translate(),
                    defaultDesc = "ST_BatteryCharger_EjectAllDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/EjectBattery", true),
                    action = () => EjectAllBatteriesAndDrop()
                };
            }
        }

        private void ShowInsertBatteryMenu()
        {
            var options = new List<FloatMenuOption>();

            // Find nearby batteries
            if (parent?.Map != null)
            {
                var nearbyBatteries = parent.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
                    .Where(t => t != null &&
                                t.TryGetComp<CompBatteryCell>() != null &&
                                t.Position.DistanceTo(parent.Position) <= 15f &&
                                !t.IsForbidden(Faction.OfPlayer))
                    .OrderBy(t => t.Position.DistanceTo(parent.Position))
                    .ToList();

                foreach (var battery in nearbyBatteries)
                {
                    var batteryComp = battery.TryGetComp<CompBatteryCell>();
                    if (batteryComp == null) continue;

                    float distance = battery.Position.DistanceTo(parent.Position);
                    string label = $"{battery.LabelShort} ({batteryComp.ChargePct.ToStringPercent()}) - {distance:F1} tiles";

                    // Can't charge nuclear
                    if (batteryComp.Tier == BatteryTier.Nuclear)
                    {
                        label += " (cannot charge nuclear)";
                        options.Add(new FloatMenuOption(label, null));
                        continue;
                    }

                    // Already full
                    if (batteryComp.ChargePct >= 1f)
                    {
                        label += " (already full)";
                        options.Add(new FloatMenuOption(label, null));
                        continue;
                    }

                    Thing capturedBattery = battery;
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        // Queue haul job to bring battery to charger
                        var pawn = FindBestPawnForJob();
                        if (pawn != null)
                        {
                            var job = JobMaker.MakeJob(ST_JobDefOf.ST_ChargeBattery, parent, capturedBattery);
                            pawn.jobs?.TryTakeOrderedJob(job, JobTag.Misc);
                        }
                        else
                        {
                            Messages.Message("ST_BatteryCharger_NoPawnAvailable".Translate(), parent, MessageTypeDefOf.RejectInput, false);
                        }
                    }));
                }
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("ST_BatteryCharger_NoBatteriesAvailable".Translate(), null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void EjectAllBatteriesAndDrop()
        {
            var ejected = EjectAllBatteries();
            foreach (var battery in ejected)
            {
                if (battery == null) continue;
                GenPlace.TryPlaceThing(battery, parent.Position, parent.Map, ThingPlaceMode.Near);
            }

            if (ejected.Count > 0)
            {
                Messages.Message($"Ejected {ejected.Count} {(ejected.Count == 1 ? "battery" : "batteries")} from {parent.LabelShort}",
                    parent, MessageTypeDefOf.TaskCompletion, false);
            }
        }

        private Pawn FindBestPawnForJob()
        {
            if (parent?.Map == null)
                return null;

            return parent.Map.mapPawns.FreeColonistsSpawned
                .Where(p => p != null && !p.Downed && !p.Dead && p.workSettings?.WorkIsActive(WorkTypeDefOf.Hauling) == true)
                .OrderBy(p => p.Position.DistanceTo(parent.Position))
                .FirstOrDefault();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);

            // Drop all batteries when destroyed
            if (previousMap != null && chargingBatteries != null)
            {
                foreach (var battery in chargingBatteries)
                {
                    if (battery == null) continue;
                    GenPlace.TryPlaceThing(battery, parent.Position, previousMap, ThingPlaceMode.Near);
                }
                chargingBatteries.Clear();
            }
        }
    }

    /// <summary>
    /// CompProperties for battery charger.
    /// </summary>
    public class CompProperties_BatteryCharger : CompProperties
    {
        public float chargeRate = 50f;
        public int maxBatteries = 4;

        public CompProperties_BatteryCharger()
        {
            compClass = typeof(CompBatteryCharger);
        }
    }
}
