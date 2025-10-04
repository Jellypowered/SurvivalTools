// RimWorld 1.6 / C# 7.3
// Source/UI/ITab_BatteryCharger.cs
// Phase 12.2: Inspector tab for battery charger

using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace SurvivalTools
{
    /// <summary>
    /// Inspector tab for battery charger that shows charging batteries.
    /// </summary>
    public class ITab_BatteryCharger : ITab
    {
        private Vector2 scrollPosition;
        private const float RowHeight = 30f;
        private const float ButtonHeight = 24f;
        private const float Margin = 10f;

        public ITab_BatteryCharger()
        {
            size = new Vector2(400f, 300f);
            labelKey = "ST_BatteryCharger_Tab";
        }

        private CompBatteryCharger ChargerComp
        {
            get
            {
                var thing = SelThing;
                return thing?.TryGetComp<CompBatteryCharger>();
            }
        }

        protected override void FillTab()
        {
            var comp = ChargerComp;
            if (comp == null)
                return;

            var settings = SurvivalToolsMod.Settings;
            if (settings?.enablePoweredTools != true)
            {
                Rect disabledRect = new Rect(Margin, Margin, size.x - Margin * 2f, size.y - Margin * 2f);
                Widgets.Label(disabledRect, "Powered tools feature is disabled in settings.");
                return;
            }

            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(Margin);

            // Header
            Text.Font = GameFont.Medium;
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, 40f);
            Widgets.Label(headerRect, $"Battery Charger ({comp.ChargingBatteries.Count}/{comp.MaxBatteries})");
            Text.Font = GameFont.Small;

            // Status
            Rect statusRect = new Rect(rect.x, headerRect.yMax, rect.width, 30f);
            string status = comp.IsActive ? "Charging..." : comp.IsPowered ? "No power" : "Powered off";
            Color statusColor = comp.IsActive ? Color.green : Color.gray;
            GUI.color = statusColor;
            Widgets.Label(statusRect, $"Status: {status}");
            GUI.color = Color.white;

            float yPos = statusRect.yMax + 10f;

            // Batteries list
            if (!comp.ChargingBatteries.Any())
            {
                Rect noBatteriesRect = new Rect(rect.x, yPos, rect.width, 30f);
                Widgets.Label(noBatteriesRect, "No batteries charging.");
            }
            else
            {
                Rect outRect = new Rect(rect.x, yPos, rect.width, rect.height - yPos);
                float viewHeight = comp.ChargingBatteries.Count * (RowHeight + 5f);
                Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, viewHeight);

                Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

                float curY = 0f;
                for (int i = 0; i < comp.ChargingBatteries.Count; i++)
                {
                    var battery = comp.ChargingBatteries[i];
                    if (battery == null)
                        continue;

                    var batteryComp = battery.TryGetComp<CompBatteryCell>();
                    if (batteryComp == null)
                        continue;

                    Rect rowRect = new Rect(0f, curY, viewRect.width, RowHeight);

                    // Background
                    if (i % 2 == 0)
                        Widgets.DrawLightHighlight(rowRect);

                    // Battery label
                    Rect labelRect = new Rect(rowRect.x + 5f, rowRect.y + 3f, rowRect.width * 0.35f, RowHeight);
                    string labelText = battery.LabelShortCap;

                    // Phase 12.2: Show wear indicator
                    if (batteryComp.Tier != BatteryTier.Nuclear && batteryComp.MaxChargeCycles > 0)
                    {
                        if (batteryComp.IsWornOut)
                        {
                            labelText += " ⚠";
                        }
                        else if (batteryComp.WearPct >= 0.75f)
                        {
                            labelText += " !";
                        }
                    }

                    Widgets.Label(labelRect, labelText);

                    // Charge bar
                    Rect barRect = new Rect(labelRect.xMax + 5f, rowRect.y + 5f, rowRect.width * 0.25f, 20f);
                    float pct = batteryComp.ChargePct;

                    // Bar background
                    Widgets.DrawBoxSolid(barRect, new Color(0.1f, 0.1f, 0.1f, 0.8f));

                    // Bar fill
                    if (pct > 0f)
                    {
                        Rect fillRect = barRect.ContractedBy(2f);
                        fillRect.width *= pct;
                        Color fillColor = pct >= 1f ? Color.green : new Color(0.2f, 0.8f, 0.9f); // Cyan while charging
                        Widgets.DrawBoxSolid(fillRect, fillColor);
                    }

                    // Bar outline
                    Widgets.DrawBox(barRect, 1);

                    // Percentage
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(barRect, pct.ToStringPercent());
                    Text.Anchor = TextAnchor.UpperLeft;

                    // Phase 12.2: Wear indicator
                    if (batteryComp.Tier != BatteryTier.Nuclear && batteryComp.MaxChargeCycles > 0)
                    {
                        Rect wearRect = new Rect(barRect.xMax + 3f, rowRect.y + 8f, 40f, 14f);
                        float wearPct = batteryComp.WearPct;
                        string wearText = wearPct.ToStringPercent();
                        Color wearColor = Color.white;

                        if (batteryComp.IsWornOut)
                        {
                            wearColor = Color.red;
                            wearText = "WORN";
                        }
                        else if (wearPct >= 0.75f)
                        {
                            wearColor = Color.yellow;
                        }
                        else if (wearPct >= 0.50f)
                        {
                            wearColor = new Color(1f, 0.8f, 0.4f); // Orange
                        }

                        GUI.color = wearColor;
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(wearRect, wearText);
                        Text.Font = GameFont.Small;
                        GUI.color = Color.white;
                    }

                    // Eject button
                    Rect buttonRect = new Rect(rowRect.width - 65f, rowRect.y + 3f, 60f, ButtonHeight);
                    if (Widgets.ButtonText(buttonRect, "Eject"))
                    {
                        EjectBatteryAt(comp, i);
                    }

                    // Phase 12.2: Tooltip with wear details
                    if (Mouse.IsOver(rowRect))
                    {
                        string tooltip = $"{battery.LabelCap}\n";
                        tooltip += $"Charge: {batteryComp.ChargePct.ToStringPercent()}\n";

                        if (batteryComp.Tier != BatteryTier.Nuclear && batteryComp.MaxChargeCycles > 0)
                        {
                            tooltip += $"Cycles: {batteryComp.ChargeCycles} / {batteryComp.MaxChargeCycles}\n";
                            tooltip += $"Wear: {batteryComp.WearPct.ToStringPercent()}";

                            if (batteryComp.IsWornOut)
                            {
                                tooltip += "\n\n<color=red>This battery is worn out and should be replaced.</color>";
                            }
                            else if (batteryComp.WearPct >= 0.75f)
                            {
                                int remaining = batteryComp.MaxChargeCycles - batteryComp.ChargeCycles;
                                tooltip += $"\n\n<color=yellow>This battery is aging. {remaining} cycles remaining.</color>";
                            }
                        }

                        TooltipHandler.TipRegion(rowRect, tooltip);
                    }

                    curY += RowHeight + 5f;
                }

                Widgets.EndScrollView();
            }
        }

        private void EjectBatteryAt(CompBatteryCharger comp, int index)
        {
            var battery = comp.EjectBatteryAt(index);
            if (battery != null)
            {
                GenPlace.TryPlaceThing(battery, comp.parent.Position, comp.parent.Map, ThingPlaceMode.Near);
                Messages.Message($"Ejected {battery.LabelShort} from battery charger",
                    comp.parent, MessageTypeDefOf.TaskCompletion, false);
            }
        }
    }
}
