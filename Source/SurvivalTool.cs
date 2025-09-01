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

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workTicksDone, "workTicksDone", 0);
        }

        #endregion
    }
}
