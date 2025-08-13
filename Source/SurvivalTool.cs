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

        public bool InUse =>
            HoldingPawn != null
            && HoldingPawn.CanUseSurvivalTools()
            && HoldingPawn.CanUseSurvivalTool(def)
            && SurvivalToolUtility.BestSurvivalToolsFor(HoldingPawn).Contains(this);

        public int WorkTicksToDegrade
        {
            get
            {
                float lifespanDays = this.GetStatValue(ST_StatDefOf.ToolEstimatedLifespan);
                int hp = Mathf.Max(1, MaxHitPoints);
                return Mathf.FloorToInt((lifespanDays * GenDate.TicksPerDay) / hp);
            }
        }

        public IEnumerable<StatModifier> WorkStatFactors
        {
            get
            {
                var tProps = SurvivalToolProperties.For(def);
                var sProps = StuffPropsTool.For(Stuff);
                float eff = this.GetStatValue(ST_StatDefOf.ToolEffectivenessFactor);

                if (tProps.baseWorkStatFactors != null)
                {
                    foreach (var modifier in tProps.baseWorkStatFactors)
                    {
                        if (modifier?.stat == null) continue;

                        float newFactor = modifier.value * eff;

                        var toolStatFactors = sProps.toolStatFactors;
                        if (toolStatFactors != null && toolStatFactors.Count > 0)
                        {
                            for (int i = 0; i < toolStatFactors.Count; i++)
                            {
                                var sMod = toolStatFactors[i];
                                if (sMod?.stat == modifier.stat)
                                {
                                    newFactor *= sMod.value;
                                }
                            }
                        }

                        yield return new StatModifier
                        {
                            stat = modifier.stat,
                            value = newFactor
                        };
                    }
                }
            }
        }

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
