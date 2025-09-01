using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.BaseGen;
using UnityEngine;
using Verse;

namespace SurvivalTools.HarmonyStuff
{
    [HarmonyPatch(typeof(SymbolResolver_AncientRuins))]
    [HarmonyPatch(nameof(SymbolResolver_AncientRuins.Resolve))]
    public static class Patch_SymbolResolver_AncientRuins_Resolve
    {
        private static readonly SimpleCurve StuffMarketValueRemainderToCommonalityCurve = new SimpleCurve
        {
            new CurvePoint(0f, SurvivalToolUtility.MapGenToolMaxStuffMarketValue * 0.1f),
            new CurvePoint(SurvivalToolUtility.MapGenToolMaxStuffMarketValue, SurvivalToolUtility.MapGenToolMaxStuffMarketValue)
        };

        public static void Prefix(ResolveParams rp)
        {
            if (!SurvivalToolUtility.IsToolMapGenEnabled)
                return;

            var setMaker = ST_ThingSetMakerDefOf.MapGen_AncientRuinsSurvivalTools?.root;
            if (setMaker == null)
                return;

            var things = setMaker.Generate();
            foreach (var thing in things)
            {
                // Custom quality generator (keep reflection to match older/internal signatures)
                if (thing.TryGetComp<CompQuality>() is CompQuality qualityComp)
                {
                    var mi = AccessTools.Method(typeof(QualityUtility), "GenerateFromGaussian");
                    if (mi != null)
                    {
                        // Arguments chosen to bias around Normal, allowing Poor/Awful occasionally.
                        var qc = (QualityCategory)mi.Invoke(null, new object[] { 1f, QualityCategory.Normal, QualityCategory.Poor, QualityCategory.Awful });
                        qualityComp.SetQuality(qc, ArtGenerationContext.Outsider);
                    }
                }

                // Set stuff (only for stuff-made defs)
                if (thing.def.MadeFromStuff)
                {
                    // All allowed stuffs with market value <= cap and not small-volume
                    var allowed = GenStuff.AllowedStuffsFor(thing.def);
                    var validStuff = DefDatabase<ThingDef>.AllDefsListForReading.Where(
                        t => !t.smallVolume && t.IsStuff && allowed.Contains(t) && t.BaseMarketValue <= SurvivalToolUtility.MapGenToolMaxStuffMarketValue
                    ).ToList();

                    if (validStuff.Count > 0)
                    {
                        var chosen = validStuff.RandomElementByWeight(t =>
                            StuffMarketValueRemainderToCommonalityCurve.Evaluate(SurvivalToolUtility.MapGenToolMaxStuffMarketValue - t.BaseMarketValue) *
                            (t.stuffProps?.commonality ?? 1f));
                        thing.SetStuffDirect(chosen);
                    }
                }

                // Set hit points
                if (thing.def.useHitPoints)
                {
                    thing.HitPoints = Mathf.RoundToInt(thing.MaxHitPoints * SurvivalToolUtility.MapGenToolHitPointsRange.RandomInRange);
                }

                rp.singleThingToSpawn = thing;
                BaseGen.symbolStack.Push("thing", rp);
            }
        }
    }
}
