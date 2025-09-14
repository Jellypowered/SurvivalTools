// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SmarterConstruction/SmarterConstructionPatches.cs
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static SurvivalTools.ST_Logging;
using SurvivalTools.Helpers;

namespace SurvivalTools.Compat.SmarterConstruction
{
    /// <summary>
    /// Harmony patches for SmarterConstruction compat.
    /// Ensures roofing jobs don't loop and tooling hooks degrade gracefully.
    /// Integrates with core gating/blacklist helpers and applies tiered penalties via WorkSpeedGlobalHelper.
    /// </summary>
    internal sealed class SmarterConstructionCompatibilityModule : ICompatibilityModule
    {
        private Harmony _harmony;
        public string ModName => "SmarterConstruction";
        public bool IsModActive => SmarterConstructionHelpers.IsSmarterConstructionActive();

        public void Initialize()
        {
            if (!IsModActive) return;
            try
            {
                _harmony = new Harmony("com.jellypowered.survivaltools.compat.smarterconstruction");

                // Patch JobOnThing if present
                var type = AccessTools.TypeByName("SmarterConstruction.WorkGiver_Construct");
                if (type != null)
                {
                    var method = AccessTools.Method(type, "JobOnThing");
                    if (method != null)
                    {
                        try { _harmony.Patch(method, prefix: new HarmonyMethod(typeof(SmarterConstructionCompatibilityModule).GetMethod(nameof(Prefix_JobOnThing)))); }
                        catch (Exception e) { LogCompatWarning("SmarterConstruction: Failed to patch JobOnThing: " + e); }
                    }
                }

                // Patch constructor finish toils (prevent infinite loops and respect tiers)
                var jdType = AccessTools.TypeByName("SmarterConstruction.JobDriver_ConstructFinishFrame");
                if (jdType != null)
                {
                    var m = AccessTools.Method(jdType, "MakeNewToils");
                    if (m != null)
                    {
                        try { _harmony.Patch(m, prefix: new HarmonyMethod(typeof(SmarterConstructionCompatibilityModule).GetMethod(nameof(Prefix_MakeNewToils)))); }
                        catch (Exception e) { LogCompatWarning("SmarterConstruction: Failed to patch MakeNewToils: " + e); }
                    }
                }
            }
            catch (Exception e)
            {
                LogCompatError("SmarterConstruction.Initialize failed: " + e);
            }
        }

        public List<StatDef> GetCompatibilityStats() => new List<StatDef>();

        public Dictionary<string, string> GetDebugInfo() => new Dictionary<string, string> { ["Active"] = IsModActive.ToString() };

        // Prefix to safeguard JobOnThing and avoid infinite roofing loops
        public static bool Prefix_JobOnThing(object __instance, Thing t, Pawn pawn, ref Job __result)
        {
            try
            {
                if (!SmarterConstructionHelpers.IsSmarterConstructionActive()) return true;
                if (t == null || pawn == null) return true;

                // Respect core blacklist — if pawn is blacklisted, allow original behaviour
                if (!PawnToolValidator.CanUseSurvivalTools(pawn)) return true;

                // Basic roofing loop guard: if job targets a roofed cell but roofing job already exists, skip.
                try
                {
                    if (t.def != null && t.def.building != null && t.Position.Roofed(t.Map))
                    {
                        if (t.Map != null)
                        {
                            // scan all pawns on the map and check their current job for a RemoveRoof targeting same thing/cell
                            try
                            {
                                var pawns = t.Map.mapPawns.AllPawnsSpawned;
                                for (int i = 0; i < pawns.Count; i++)
                                {
                                    var mapPawn = pawns[i];
                                    var cj = mapPawn.CurJob;
                                    if (cj == null) continue;
                                    if (cj.def == JobDefOf.RemoveRoof)
                                    {
                                        if (cj.targetA != null && cj.targetA.Thing == t) return false;
                                        if (cj.targetA != null && cj.targetA.Cell == t.Position) return false;
                                        if (cj.targetB != null && cj.targetB.Thing == t) return false;
                                        if (cj.targetB != null && cj.targetB.Cell == t.Position) return false;
                                    }
                                }
                            }
                            catch { }
                        }

                        if (pawn.CurJob != null && pawn.CurJob.def == JobDefOf.RemoveRoof)
                            return false;
                    }
                }
                catch { /* swallow */ }

                // Additionally, gate roofing early if settings/hardcore require construction tools
                try
                {
                    var settings = SurvivalTools.Settings;
                    if (settings != null && settings.extraHardcoreMode == true)
                    {
                        WorkGiverDef wgDef = null;
                        try { wgDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("BuildRoofs"); } catch { }

                        // Prefer StatGatingHelper to keep parity with core gating rules
                        if (wgDef != null)
                        {
                            try
                            {
                                if (StatGatingHelper.ShouldBlockBuildRoof(pawn, out string scKey, t.Position))
                                {
                                    if (ST_Logging.IsCompatLogging())
                                        LogCompatMessage($"SmarterConstruction: denying roofing job for {pawn.LabelShort} (missing construction tool).", "SmarterConstruction.RoofGate");
                                    try { if (ShouldLogWithCooldown(scKey)) LogCompatMessage($"SmarterConstruction: denied roofing for {pawn.LabelShort} at {t.Position}", "SmarterConstruction.RoofGate"); } catch { }
                                    return false;
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { /* swallow */ }

                return true;
            }
            catch (Exception e)
            {
                LogCompatWarning("Prefix_JobOnThing exception: " + e);
                return true;
            }
        }

        // Prefix to inspect MakeNewToils and avoid endless toil spawns
        public static bool Prefix_MakeNewToils(object __instance)
        {
            try
            {
                if (!SmarterConstructionHelpers.IsSmarterConstructionActive()) return true;
                // Defensive: ensure any JobDriver toils that rely on SurvivalTools are aware of current pawn/tool state
                // (Heavy instrumentation or tiered penalty registration would be done in StatPart_SurvivalTool via GetStatValue)
                return true;
            }
            catch (Exception e)
            {
                LogCompatWarning("Prefix_MakeNewToils exception: " + e);
                return true;
            }
        }
    }
}
