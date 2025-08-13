using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SurvivalTools
{
    public class StatWorker_EstimatedLifespan : StatWorker
    {
        // Once per hour of continuous work, or ~40 mins with hardcore enabled.
        public static int BaseWearInterval =>
            Mathf.RoundToInt(GenDate.TicksPerHour * ((SurvivalTools.Settings != null && SurvivalTools.Settings.hardcoreMode) ? 0.67f : 1f));

        public override bool ShouldShowFor(StatRequest req)
        {
            // Only show for buildables that are survival tools, and when degradation is on.
            var bdef = req.Def as BuildableDef;
            if (bdef == null || !bdef.IsSurvivalTool()) return false;

            var s = SurvivalTools.Settings;
            return s != null && s.toolDegradationFactor > 0.001f;
        }

        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            var tool = req.Thing as SurvivalTool;
            var bdef = req.Def as BuildableDef;
            return GetBaseEstimatedLifespan(tool, bdef);
        }

        public override string GetExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense)
        {
            var tool = req.Thing as SurvivalTool;
            var bdef = req.Def as BuildableDef;
            return $"{"StatsReport_BaseValue".Translate()}: {GetBaseEstimatedLifespan(tool, bdef).ToString("F1")}";
        }

        private float GetBaseEstimatedLifespan(SurvivalTool tool, BuildableDef def)
        {
            var thingDef = def as ThingDef;
            if (thingDef == null || !thingDef.useHitPoints)
                return float.PositiveInfinity;

            // For def (no specific instance)
            if (tool == null)
            {
                var props = SurvivalToolProperties.For(thingDef);
                var maxHP = def.GetStatValueAbstract(StatDefOf.MaxHitPoints);
                var wear = props.toolWearFactor <= 0f ? 1f : props.toolWearFactor;
                return GenDate.TicksToDays(Mathf.RoundToInt((BaseWearInterval * maxHP) / wear));
            }

            // For specific instance (stuff + HP)
            var stuffProps = StuffPropsTool.For(tool.Stuff);
            var tProps = SurvivalToolProperties.For(tool.def);
            var wearFactor =
                (tProps.toolWearFactor <= 0f ? 1f : tProps.toolWearFactor) *
                (stuffProps.wearFactorMultiplier <= 0f ? 1f : stuffProps.wearFactorMultiplier);

            return GenDate.TicksToDays(Mathf.RoundToInt((BaseWearInterval * tool.MaxHitPoints) / wearFactor));
        }

        public override void FinalizeValue(StatRequest req, ref float val, bool applyPostProcess)
        {
            var s = SurvivalTools.Settings;
            var factor = (s != null && s.toolDegradationFactor > 0.001f) ? s.toolDegradationFactor : 1f;
            if (factor != 1f) val /= factor;
            base.FinalizeValue(req, ref val, applyPostProcess);
        }

        public override string GetExplanationFinalizePart(StatRequest req, ToStringNumberSense numberSense, float finalVal)
        {
            var s = SurvivalTools.Settings;
            var factor = (s != null && s.toolDegradationFactor > 0.001f) ? s.toolDegradationFactor : 1f;

            var sb = new StringBuilder();
            sb.AppendLine($"{"Settings_ToolDegradationRate".Translate()}: {(1f / factor).ToStringByStyle(ToStringStyle.FloatTwo, ToStringNumberSense.Factor)}");
            sb.AppendLine();
            sb.AppendLine(base.GetExplanationFinalizePart(req, numberSense, finalVal));
            return sb.ToString();
        }
    }
}
