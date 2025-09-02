// RimWorld 1.6 / C# 7.3
// Patch_SymbolResolver_AncientRuins_Resolve.cs
using System;
using System.Collections.Generic;
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
        // Favor cheaper stuff somewhat, but never zero weight.
        private static readonly SimpleCurve StuffMarketValueRemainderToCommonalityCurve = new SimpleCurve
        {
            new CurvePoint(0f,   SurvivalToolUtility.MapGenToolMaxStuffMarketValue * 0.1f),
            new CurvePoint(SurvivalToolUtility.MapGenToolMaxStuffMarketValue,
                           SurvivalToolUtility.MapGenToolMaxStuffMarketValue)
        };

        // Cache reflection once; fallback if unavailable.
        private static readonly System.Reflection.MethodInfo MI_GenerateFromGaussian =
            AccessTools.Method(typeof(QualityUtility), "GenerateFromGaussian");

        public static void Prefix(ResolveParams rp)
        {
            if (!SurvivalToolUtility.IsToolMapGenEnabled)
                return;

            var setMaker = ST_ThingSetMakerDefOf.MapGen_AncientRuinsSurvivalTools?.root;
            if (setMaker == null)
                return;

            List<Thing> things;
            try
            {
                things = setMaker.Generate();
            }
            catch (Exception e)
            {
                Log.Warning($"[SurvivalTools] AncientRuins tool set generation failed: {e}");
                return;
            }

            if (things == null || things.Count == 0)
                return;

            for (int i = 0; i < things.Count; i++)
            {
                var thing = things[i];
                if (thing == null) continue;

                try
                {
                    // --- Quality ---
                    var qComp = thing.TryGetComp<CompQuality>();
                    if (qComp != null)
                    {
                        QualityCategory qc;
                        if (MI_GenerateFromGaussian != null)
                        {
                            object res = MI_GenerateFromGaussian.Invoke(
                                null, new object[] { 1f, QualityCategory.Normal, QualityCategory.Poor, QualityCategory.Awful });
                            qc = res is QualityCategory q ? q : QualityUtility.GenerateQualityRandomEqualChance();
                        }
                        else
                        {
                            qc = QualityUtility.GenerateQualityRandomEqualChance();
                        }
                        qComp.SetQuality(qc, ArtGenerationContext.Outsider);
                    }

                    // --- Stuff (if applicable and not already assigned) ---
                    if (thing.def != null && thing.def.MadeFromStuff && thing.Stuff == null)
                    {
                        var allowedEnum = GenStuff.AllowedStuffsFor(thing.def);
                        var allowed = allowedEnum != null ? allowedEnum.ToList() : null;

                        if (allowed != null && allowed.Count > 0)
                        {
                            var maxMV = SurvivalToolUtility.MapGenToolMaxStuffMarketValue;
                            var valid = allowed.Where(t =>
                                t != null &&
                                t.IsStuff &&
                                !t.smallVolume &&
                                t.BaseMarketValue <= maxMV
                            ).ToList();

                            if (valid.Count > 0)
                            {
                                var chosen = valid.RandomElementByWeight(tdef =>
                                {
                                    float remainder = Mathf.Max(0f, maxMV - tdef.BaseMarketValue);
                                    float baseW = StuffMarketValueRemainderToCommonalityCurve.Evaluate(remainder);
                                    float common = Mathf.Max(0.0001f, tdef.stuffProps?.commonality ?? 1f);
                                    return Mathf.Max(0.0001f, baseW * common);
                                });

                                if (chosen != null)
                                    thing.SetStuffDirect(chosen);
                            }
                        }
                    }

                    // --- HitPoints ---
                    if (thing.def != null && thing.def.useHitPoints)
                    {
                        int hp = Mathf.RoundToInt(thing.MaxHitPoints *
                                                  SurvivalToolUtility.MapGenToolHitPointsRange.RandomInRange);
                        thing.HitPoints = Mathf.Clamp(hp, 1, thing.MaxHitPoints);
                    }

                    // --- Enqueue spawn ---
                    var rpForThing = rp; // struct copy
                    rpForThing.singleThingToSpawn = thing;
                    BaseGen.symbolStack.Push("thing", rpForThing);
                }
                catch (Exception e)
                {
                    Log.Warning($"[SurvivalTools] Skipped ancient-ruins tool '{thing?.def?.defName ?? "null"}' due to error: {e}");
                }
            }
        }
    }
}
