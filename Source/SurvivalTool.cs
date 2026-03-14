using System.Collections.Generic;
using System.Linq; // <- for Contains / Any
using UnityEngine;
using Verse;
using RimWorld;

namespace SurvivalTools
{
    public class SurvivalTool : ThingWithComps
    {
        public int workTicksDone = 0;

        #region Properties

        public Pawn HoldingPawn
        {
            get
            {
                if (ParentHolder is Pawn_EquipmentTracker eq) return eq.pawn;
                if (ParentHolder is Pawn_InventoryTracker inv) return inv.pawn;
                return null;
            }
        }

        public bool InUse => SurvivalToolUtility.IsToolInUse(this);

        public int WorkTicksToDegrade
        {
            get
            {
                float lifespanDays = this.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan);
                int hp = Mathf.Max(1, MaxHitPoints);
                return Mathf.FloorToInt((lifespanDays * GenDate.TicksPerDay) / hp);
            }
        }

        public IEnumerable<StatModifier> WorkStatFactors =>
            SurvivalToolUtility.CalculateWorkStatFactors(this);

        #endregion

        #region Methods

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            foreach (var modifier in WorkStatFactors)
            {
                yield return new StatDrawEntry(
                    ST_StatCategoryDefOf.SurvivalTool,
                    modifier.stat.LabelCap,
                    modifier.value.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Factor),
                    SurvivalToolUtility.GetSurvivalToolOverrideReportText(this, modifier.stat),
                    0
                );
            }
        }

        public override string GetInspectString()
        {
            var baseString = base.GetInspectString();

            // Add "in use" status to tooltips (especially important for Nice Inventory Tab compatibility)
            if (InUse)
            {
                var inUseText = "ToolInUse".Translate();

                // Add debug info if enabled
                if (SurvivalToolUtility.IsDebugLoggingEnabled && HoldingPawn?.jobs?.curJob != null)
                {
                    var job = HoldingPawn.jobs.curJob;
                    inUseText += $" ({job.def.defName})";
                }

                if (!string.IsNullOrEmpty(baseString))
                {
                    return baseString + "\n" + inUseText;
                }
                return inUseText;
            }

            return baseString;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workTicksDone, "workTicksDone", 0);
        }

        #endregion
    }
}
