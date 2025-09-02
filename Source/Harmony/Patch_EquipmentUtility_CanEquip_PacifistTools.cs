// RimWorld 1.6, C# 7.3
// Patch_EquipmentUtility_CanEquip_PacifistTools.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SurvivalTools.HarmonyStuff
{
    /// <summary>
    /// Part 1: Let pacifists equip Survival Tools that happen to be IsWeapon.
    /// We do NOT change IsWeapon or other vanilla blocks (forbidden, biocode, etc.).
    /// </summary>
    [HarmonyPatch]
    public static class Patch_EquipmentUtility_CanEquip_PacifistTools
    {
        // Patch all overloads named CanEquip that look like: bool CanEquip(Thing, Pawn, out string, bool, ...)
        static IEnumerable<MethodBase> TargetMethods()
        {
            return AccessTools.GetDeclaredMethods(typeof(EquipmentUtility))
                .Where(m => m.Name == "CanEquip" && m.ReturnType == typeof(bool))
                .Where(m =>
                {
                    var p = m.GetParameters();
                    return p.Length >= 4
                        && typeof(Thing).IsAssignableFrom(p[0].ParameterType)
                        && p[1].ParameterType == typeof(Pawn)
                        && p[2].ParameterType == typeof(string).MakeByRefType()
                        && p[3].ParameterType == typeof(bool);
                });
        }

        // Postfix uses generic object parameters because we patch multiple overloads.
        static void Postfix(ref bool __result, object __0, object __1)
        {
            // Fast exits
            if (!SurvivalTools.Settings?.allowPacifistEquip == true) return;
            if (__result) return;

            var thing = __0 as Thing;
            var pawn = __1 as Pawn;
            if (thing == null || pawn == null) return;

            // Only care for things that vanilla considers weapons
            var def = thing.def;
            if (def == null || !def.IsWeapon) return;

            // Only affect pacifists (WorkTags.Violent disabled)
            try
            {
                if (!pawn.WorkTagIsDisabled(WorkTags.Violent)) return;
            }
            catch
            {
                // Unexpected pawn state — be conservative
                return;
            }

            // Only our survival tools (via mod extension)
            var ext = SurvivalToolProperties.For(def);
            if (ext == null || ext == SurvivalToolProperties.defaultValues) return;

            // Respect normal vanilla restrictions
            if (thing.IsForbidden(pawn)) return;
            if (IsBiocodedForDifferentPawn(thing, pawn)) return;

            // Passed all checks — allow equip for pacifist
            if (SurvivalToolUtility.IsDebugLoggingEnabled)
            {
                string key = $"AllowPacifistEquip_{pawn.ThingID}_{thing.ThingID}";
                if (SurvivalToolUtility.ShouldLogWithCooldown(key))
                    Log.Message($"[SurvivalTools] Allowing pacifist {pawn.LabelShort} to equip survival tool: {thing.LabelShort}");
            }

            __result = true;
        }

        // Royalty-safe biocode check.
        private static bool IsBiocodedForDifferentPawn(Thing t, Pawn pawn)
        {
            if (t == null || pawn == null) return false;
            if (!ModsConfig.RoyaltyActive) return false;

            try
            {
                var bio = t.TryGetComp<CompBiocodable>();
                return bio != null && bio.Biocoded && bio.CodedPawn != pawn;
            }
            catch
            {
                // If something odd happens with a modded comp, be conservative
                return true;
            }
        }
    }

    // --------------------------------------------------------------------
    // Part 2: Float menu providers (1.6 pipeline) for UNDRAFTED and DRAFTED
    // --------------------------------------------------------------------

    /// <summary>Shared logic for both providers.</summary>
    public abstract class FloatMenuOptionProvider_PacifistEquipToolsBase : FloatMenuOptionProvider
    {
        // In 1.6 these are abstract and PROTECTED on the base class.
        protected override bool Drafted => DraftedValue;
        protected override bool Undrafted => !DraftedValue;
        protected override bool Multiselect => true; // allow multi-select contexts

        protected abstract bool DraftedValue { get; }

        public override bool Applies(FloatMenuContext context)
        {
            if (!SurvivalTools.Settings?.allowPacifistEquip == true) return false;

            // At least one humanlike pacifist in this drafted state
            return context.ValidSelectedPawns.Any(p =>
                   p?.RaceProps?.Humanlike == true &&
                   p.WorkTagIsDisabled(WorkTags.Violent) &&
                   ((p.Drafted && DraftedValue) || (!p.Drafted && !DraftedValue)));
        }

        public override bool TargetThingValid(Thing t, FloatMenuContext context)
        {
            var def = t?.def as ThingDef;
            if (def == null || !def.IsWeapon) return false;

            var ext = SurvivalToolProperties.For(def);
            return ext != null && ext != SurvivalToolProperties.defaultValues;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing t, FloatMenuContext context)
        {
            if (t == null) yield break;

            Pawn pawn = context.ValidSelectedPawns.FirstOrDefault(p =>
                p?.RaceProps?.Humanlike == true &&
                p.WorkTagIsDisabled(WorkTags.Violent) &&
                ((p.Drafted && DraftedValue) || (!p.Drafted && !DraftedValue)) &&
                p.CanReach(t, PathEndMode.ClosestTouch, Danger.Deadly) &&
                p.CanReserve(t) &&
                !t.IsForbidden(p));

            if (pawn == null) yield break;

            // --- Equip option ---
            {
                string label = "Equip".Translate(t.LabelShort);
                Action act = () =>
                {
                    if (pawn.DestroyedOrNull() || t.DestroyedOrNull()) return;
                    var job = JobMaker.MakeJob(JobDefOf.Equip, t);
                    job.count = 1;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                };
                yield return FloatMenuUtility.DecoratePrioritizedTask(
                    new FloatMenuOption(label, act, MenuOptionPriority.High, null, t),
                    pawn, t.Position);
            }

            // --- Take to inventory option (let pacifists carry without equipping) ---
            if (pawn.inventory != null)
            {
                // Prefer existing translation key if present; fallback to a simple composite string
                string invLabel;
                try
                {
                    invLabel = "TakeToInventory".Translate(t.LabelShort);
                }
                catch
                {
                    invLabel = "Take to inventory".TranslateSimple() + ": " + t.LabelShort;
                }

                Action actInv = () =>
                {
                    if (pawn.DestroyedOrNull() || t.DestroyedOrNull()) return;
                    var job = JobMaker.MakeJob(JobDefOf.TakeInventory, t);
                    job.count = 1;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                };

                yield return FloatMenuUtility.DecoratePrioritizedTask(
                    new FloatMenuOption(invLabel, actInv, MenuOptionPriority.Default, null, t),
                    pawn, t.Position);
            }
        }
    }

    /// <summary>Provider shown while pawns are UNDRAFTED.</summary>
    public sealed class FloatMenuOptionProvider_PacifistEquipTools_Undrafted
        : FloatMenuOptionProvider_PacifistEquipToolsBase
    {
        protected override bool DraftedValue => false;
    }

    /// <summary>Provider shown while pawns are DRAFTED.</summary>
    public sealed class FloatMenuOptionProvider_PacifistEquipTools_Drafted
        : FloatMenuOptionProvider_PacifistEquipToolsBase
    {
        protected override bool DraftedValue => true;
    }
}
