// RimWorld 1.6 / C# 7.3
// Source/UI/Gizmo_PowerToolManager.cs
// Phase 12.1: Unified power tool management gizmo

using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;
using System.Collections.Generic;

namespace SurvivalTools
{
    /// <summary>
    /// Unified gizmo that displays charge level and provides battery management for powered tools.
    /// Shows when a pawn with a powered tool equipped is selected, or when the tool itself is selected.
    /// </summary>
    public class Gizmo_PowerToolManager : Gizmo
    {
        public CompPowerTool powerComp;
        public Thing tool;
        public Pawn pawn; // Optional - for battery operations

        private const float Width = 140f;
        private const float GizmoHeight = 105f;
        private const float BarHeight = 20f;
        private const float BarMargin = 8f;
        private const float ButtonHeight = 24f;
        private const float ButtonMargin = 4f;

        public Gizmo_PowerToolManager()
        {
            Order = -100f; // Show before other gizmos
        }

        public override float GetWidth(float maxWidth)
        {
            return Width;
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            if (powerComp == null || tool == null)
                return new GizmoResult(GizmoState.Clear);

            // Background
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), GizmoHeight);
            Widgets.DrawWindowBackground(rect);

            float yOffset = 3f;

            // Tool label
            Text.Anchor = TextAnchor.UpperCenter;
            Text.Font = GameFont.Tiny;
            Rect labelRect = new Rect(rect.x, rect.y + yOffset, rect.width, 18f);
            Widgets.Label(labelRect, tool.LabelShortCap);
            yOffset += 20f;

            // Charge bar
            float charge = powerComp.Charge;
            float capacity = powerComp.Capacity;
            float chargePct = powerComp.ChargePct;

            Rect barRect = new Rect(rect.x + BarMargin, rect.y + yOffset, rect.width - (BarMargin * 2f), BarHeight);

            // Draw bar background
            Widgets.DrawBoxSolid(barRect, new Color(0.1f, 0.1f, 0.1f, 0.8f));

            // Draw charge fill
            if (chargePct > 0f)
            {
                Rect fillRect = barRect.ContractedBy(2f);
                fillRect.width *= chargePct;

                // Color based on charge level
                Color fillColor;
                if (chargePct > 0.5f)
                    fillColor = new Color(0.2f, 0.8f, 0.2f); // Green
                else if (chargePct > 0.25f)
                    fillColor = new Color(0.9f, 0.9f, 0.2f); // Yellow
                else
                    fillColor = new Color(0.9f, 0.2f, 0.2f); // Red

                Widgets.DrawBoxSolid(fillRect, fillColor);
            }

            // Draw bar outline
            Widgets.DrawBox(barRect, 1);

            // Charge text on bar
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            string chargeText = $"{charge:F0}/{capacity:F0}";
            Widgets.Label(barRect, chargeText);
            yOffset += BarHeight + 2f;

            // Percentage and battery info below bar
            Text.Anchor = TextAnchor.UpperCenter;
            Text.Font = GameFont.Tiny;
            Rect infoRect = new Rect(rect.x, rect.y + yOffset, rect.width, 15f);
            string infoText = chargePct.ToStringPercent();

            if (powerComp.BatteryItem != null)
            {
                infoText += $" ({powerComp.BatteryItem.LabelShort})";
            }
            else
            {
                infoText += " (Internal)";
            }

            Widgets.Label(infoRect, infoText);
            yOffset += 17f;

            // Action buttons
            Rect buttonAreaRect = new Rect(rect.x + ButtonMargin, rect.y + yOffset, rect.width - (ButtonMargin * 2f), ButtonHeight);

            bool hasBattery = powerComp.BatteryItem != null;

            if (hasBattery)
            {
                // Eject button
                Rect ejectRect = new Rect(buttonAreaRect.x, buttonAreaRect.y, buttonAreaRect.width, buttonAreaRect.height);

                if (Widgets.ButtonText(ejectRect, "Eject Battery", true, true, true))
                {
                    EjectBattery();
                }

                if (Mouse.IsOver(ejectRect))
                {
                    TooltipHandler.TipRegion(ejectRect, $"Eject the battery from {tool.LabelShort}");
                }
            }
            else
            {
                // Insert button
                Rect insertRect = new Rect(buttonAreaRect.x, buttonAreaRect.y, buttonAreaRect.width, buttonAreaRect.height);

                if (Widgets.ButtonText(insertRect, "Insert Battery", true, true, true))
                {
                    ShowInsertBatteryMenu();
                }

                if (Mouse.IsOver(insertRect))
                {
                    TooltipHandler.TipRegion(insertRect, $"Insert a battery into {tool.LabelShort}");
                }
            }

            Text.Anchor = TextAnchor.UpperLeft;

            // Main tooltip (on non-button areas)
            Rect tooltipRect = new Rect(rect.x, rect.y, rect.width, yOffset);
            if (Mouse.IsOver(tooltipRect) && !Mouse.IsOver(buttonAreaRect))
            {
                string tooltipText = BuildTooltip();
                TooltipHandler.TipRegion(tooltipRect, tooltipText);
            }

            return new GizmoResult(GizmoState.Clear);
        }

        private string BuildTooltip()
        {
            float charge = powerComp.Charge;
            float capacity = powerComp.Capacity;
            float chargePct = powerComp.ChargePct;

            string tooltipText = $"{tool.LabelCap}\n\n";
            tooltipText += $"Charge: {charge:F1} / {capacity:F1} ({chargePct.ToStringPercent()})\n";

            if (powerComp.BatteryItem != null)
            {
                var batteryComp = powerComp.BatteryItem.TryGetComp<CompBatteryCell>();
                tooltipText += $"Battery: {powerComp.BatteryItem.LabelShort}\n";
                if (batteryComp != null)
                {
                    tooltipText += $"Battery Capacity: {batteryComp.Capacity:F0}\n";
                }
            }
            else
            {
                tooltipText += "No battery installed (using internal charge)\n";
            }

            if (chargePct <= 0f)
            {
                tooltipText += "\n<color=red>Tool is out of power!</color>";
            }
            else if (chargePct < 0.25f)
            {
                tooltipText += "\n<color=yellow>Warning: Low battery</color>";
            }

            return tooltipText;
        }

        private void EjectBattery()
        {
            if (powerComp == null || powerComp.BatteryItem == null)
                return;

            Thing ejected = powerComp.EjectBattery();
            if (ejected == null)
                return;

            // Try to add to pawn inventory if available
            if (pawn?.inventory?.innerContainer != null)
            {
                if (pawn.inventory.innerContainer.TryAdd(ejected))
                {
                    Messages.Message($"{pawn.LabelShort} ejected {ejected.LabelShort} from {tool.LabelShort}",
                        MessageTypeDefOf.TaskCompletion, false);
                    return;
                }
            }

            // Otherwise drop at tool location (either pawn's position or tool's position)
            IntVec3 dropPos = pawn?.Position ?? tool.Position;
            Map map = pawn?.Map ?? tool.Map;

            if (map != null)
            {
                GenPlace.TryPlaceThing(ejected, dropPos, map, ThingPlaceMode.Near);
                Messages.Message($"Ejected {ejected.LabelShort} from {tool.LabelShort}",
                    MessageTypeDefOf.TaskCompletion, false);
            }
        }

        private void ShowInsertBatteryMenu()
        {
            if (powerComp == null)
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // Search pawn inventory if available
            if (pawn?.inventory?.innerContainer != null)
            {
                for (int i = 0; i < pawn.inventory.innerContainer.Count; i++)
                {
                    var item = pawn.inventory.innerContainer[i];
                    var batteryComp = item?.TryGetComp<CompBatteryCell>();
                    if (batteryComp != null)
                    {
                        Thing battery = item; // Capture for closure
                        string label = $"{battery.LabelShort} ({batteryComp.ChargePct.ToStringPercent()}) - Inventory";
                        options.Add(new FloatMenuOption(label, () =>
                        {
                            if (powerComp.TryInsertBattery(battery))
                            {
                                Messages.Message($"Inserted {battery.LabelShort} into {tool.LabelShort}",
                                    MessageTypeDefOf.TaskCompletion, false);
                            }
                        }));
                    }
                }
            }

            // Search nearby items (within 10 tiles)
            Map map = pawn?.Map ?? tool.Map;
            IntVec3 searchPos = pawn?.Position ?? tool.Position;

            if (map != null)
            {
                List<Thing> nearbyItems = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
                for (int i = 0; i < nearbyItems.Count; i++)
                {
                    var item = nearbyItems[i];
                    if (item == null || item.Position.DistanceTo(searchPos) > 10f)
                        continue;

                    var batteryComp = item?.TryGetComp<CompBatteryCell>();
                    if (batteryComp != null)
                    {
                        Thing battery = item; // Capture for closure
                        float distance = item.Position.DistanceTo(searchPos);
                        string label = $"{battery.LabelShort} ({batteryComp.ChargePct.ToStringPercent()}) - {distance:F1} tiles";

                        if (pawn != null)
                        {
                            // Pawn available - can queue job to get battery
                            options.Add(new FloatMenuOption(label, () =>
                            {
                                Job getJob = JobMaker.MakeJob(JobDefOf.TakeInventory, battery);
                                pawn.jobs?.TryTakeOrderedJob(getJob, JobTag.Misc);

                                // Queue swap after pickup
                                Job swapJob = JobMaker.MakeJob(ST_JobDefOf.ST_SwapBattery, tool, battery);
                                pawn.jobs?.jobQueue?.EnqueueLast(swapJob, JobTag.Misc);
                            }));
                        }
                        else
                        {
                            // No pawn - manual insertion only (tool selected directly)
                            options.Add(new FloatMenuOption(label + " (manual only)", () =>
                            {
                                Messages.Message("Select a pawn to retrieve and insert the battery",
                                    MessageTypeDefOf.RejectInput, false);
                            }));
                        }
                    }
                }
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("No batteries available nearby", null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
