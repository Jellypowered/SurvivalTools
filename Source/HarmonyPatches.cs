// RimWorld 1.6 / C# 7.3
// Source/HarmonyPatches.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using static SurvivalTools.ST_Logging;
using Verse.AI;
using SurvivalTools.Assign;

namespace SurvivalTools.HarmonyStuff
{
    // Smoke test: ensure provider type is discoverable in assembly (dev-mode logging only)
    [StaticConstructorOnStartup]
    static class ST_FloatMenuProviderSmoke
    {
        static ST_FloatMenuProviderSmoke()
        {
            try
            {
                var t = typeof(SurvivalTools.UI.RightClickRescue.Provider_STPrioritizeWithRescue);
                if (Prefs.DevMode) Log.Message($"[ST.RightClick] Smoke: provider type present = {t.FullName}");
            }
            catch { }
        }
    }
    internal static class HarmonyPatches
    {
        private static readonly Type patchType = typeof(HarmonyPatches);
        internal static HarmonyLib.Harmony H; // assigned in Init

        // Stat/Method lookups used in several transpilers (lazy initialized to avoid early DefOf access)
        private static FieldInfo _constructionSpeed;
        private static FieldInfo _maintenanceSpeedField;
        private static FieldInfo _deconstructionSpeedField;
        private static FieldInfo _sowingSpeedField;
        private static FieldInfo _researchSpeedField;
        private static MethodInfo _tryDegradeTool;
        private static FieldInfo _pawnField;
        private static FieldInfo _miningSpeedField;
        private static FieldInfo _diggingSpeedField;

        private static FieldInfo ConstructionSpeed => _constructionSpeed ?? (_constructionSpeed =
            AccessTools.Field(typeof(StatDefOf), nameof(StatDefOf.ConstructionSpeed)));

        private static FieldInfo MaintenanceSpeedField => _maintenanceSpeedField ?? (_maintenanceSpeedField =
            AccessTools.Field(typeof(ST_StatDefOf), nameof(ST_StatDefOf.MaintenanceSpeed)));

        private static FieldInfo DeconstructionSpeedField => _deconstructionSpeedField ?? (_deconstructionSpeedField =
            AccessTools.Field(typeof(ST_StatDefOf), nameof(ST_StatDefOf.DeconstructionSpeed)));

        private static FieldInfo SowingSpeedField => _sowingSpeedField ?? (_sowingSpeedField =
            AccessTools.Field(typeof(ST_StatDefOf), nameof(ST_StatDefOf.SowingSpeed)));

        private static FieldInfo ResearchSpeedField => _researchSpeedField ?? (_researchSpeedField =
            AccessTools.Field(typeof(ST_StatDefOf), nameof(ST_StatDefOf.ResearchSpeed)));

        private static MethodInfo TryDegradeTool => _tryDegradeTool ?? (_tryDegradeTool =
            AccessTools.Method(typeof(SurvivalToolUtility), nameof(SurvivalToolUtility.TryDegradeTool),
                new[] { typeof(Pawn), typeof(StatDef) }));

        private static FieldInfo PawnField => _pawnField ?? (_pawnField = AccessTools.Field(typeof(JobDriver), nameof(JobDriver.pawn)));
        private static FieldInfo MiningSpeedField => _miningSpeedField ?? (_miningSpeedField = AccessTools.Field(typeof(StatDefOf), nameof(StatDefOf.MiningSpeed)));
        private static FieldInfo DiggingSpeedField => _diggingSpeedField ?? (_diggingSpeedField = AccessTools.Field(typeof(ST_StatDefOf), nameof(ST_StatDefOf.DiggingSpeed)));

        #region Helpers

        private static void TryPatch(string label, MethodBase original,
            HarmonyMethod prefix = null, HarmonyMethod postfix = null,
            HarmonyMethod transpiler = null, HarmonyMethod finalizer = null)
        {
            if (original == null)
            {
                if (IsDebugLoggingEnabled)
                    LogWarning($"[SurvivalTools] Skipping patch '{label}' — method not found.");
                return;
            }
            try
            {
                H.Patch(original, prefix, postfix, transpiler, finalizer);
            }
            catch (Exception e)
            {
                LogError($"[SurvivalTools] Failed to patch '{label}': {e}");
            }
        }

        // Find the anonymous state machine method that implements MakeNewToils
        private static MethodInfo FindMakeNewToils(Type type)
        {
            return type
                .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(AccessTools.GetDeclaredMethods)
                .FirstOrDefault(m => m.ReturnType == typeof(void)
                                  && m.GetParameters().Length == 0
                                  && m.Name.Contains("MakeNewToils"));
        }

        // (kept around for potential future use)
        public static void TryDegradeTool_FromToil(Toil toil, StatDef stat)
        {
            var p = toil != null ? toil.actor : null;
            if (p != null) SurvivalToolUtility.TryDegradeTool(p, stat);
        }

        // helpers (C# 7.3-friendly)
        private static bool IsStloc(OpCode op)
        {
            return op == OpCodes.Stloc || op == OpCodes.Stloc_S
                || op == OpCodes.Stloc_0 || op == OpCodes.Stloc_1
                || op == OpCodes.Stloc_2 || op == OpCodes.Stloc_3;
        }

        private static CodeInstruction LdForSt(CodeInstruction st)
        {
            var op = st.opcode;
            if (op == OpCodes.Stloc_0) return new CodeInstruction(OpCodes.Ldloc_0);
            if (op == OpCodes.Stloc_1) return new CodeInstruction(OpCodes.Ldloc_1);
            if (op == OpCodes.Stloc_2) return new CodeInstruction(OpCodes.Ldloc_2);
            if (op == OpCodes.Stloc_3) return new CodeInstruction(OpCodes.Ldloc_3);
            // stloc.s or stloc (short/long) – reuse the same operand
            return new CodeInstruction(op == OpCodes.Stloc_S ? OpCodes.Ldloc_S : OpCodes.Ldloc, st.operand);
        }

        #endregion

        internal static void Init(HarmonyLib.Harmony harmony)
        {
            if (H != null) return; // already initialized
            H = harmony ?? throw new ArgumentNullException(nameof(harmony));
            // Attribute patches (if any)
            H.PatchAll(Assembly.GetExecutingAssembly());
            // Reflection-based AI StartJob hook (robust to signature drift)
            try { PreWork_AutoEquip.ApplyStartJobHook(H); } catch { }

            // Confirm presence of post-add enforcement hooks (optional diagnostics)
            try
            {
                var addMeth = AccessTools.Method(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.TryAddAndUnforbid), new Type[] { typeof(Thing) });
                var txMeth = AccessTools.Method(typeof(ThingOwner), "TryTransferToContainer", new Type[] { typeof(Thing), typeof(ThingOwner), typeof(int), typeof(Thing).MakeByRefType(), typeof(bool) });
                Log.Warning($"[SurvivalTools.Harmony] PostAddHooks targets present? TryAddAndUnforbid={(addMeth != null)} TryTransferToContainer={(txMeth != null)}");
            }
            catch { }

            // IMMEDIATE VERIFICATION - Check if our PreWork patch was applied
            try
            {
                var methods = AccessTools.GetDeclaredMethods(typeof(Verse.AI.Pawn_JobTracker)).Where(m => m.Name == "TryTakeOrderedJob").ToList();
                int totalPrefixes = 0;
                for (int mi = 0; mi < methods.Count; mi++)
                {
                    var m = methods[mi];
                    var info = HarmonyLib.Harmony.GetPatchInfo(m);
                    if (info == null) continue;
                    totalPrefixes += info.Prefixes.Count;
                }
                Log.Warning($"[SurvivalTools.Harmony] TryTakeOrderedJob overloads scanned={methods.Count} totalPrefixCount={totalPrefixes}");
                for (int mi = 0; mi < methods.Count; mi++)
                {
                    var m = methods[mi];
                    var info = HarmonyLib.Harmony.GetPatchInfo(m);
                    if (info == null) continue;
                    Log.Warning($"[SurvivalTools.Harmony] Overload {mi} ({m.GetParameters().Length} params) prefixes={info.Prefixes.Count}");
                    foreach (var p in info.Prefixes)
                    {
                        Log.Warning($"  - {p.owner}: {p.PatchMethod.DeclaringType?.Name}.{p.PatchMethod.Name} (Priority:{p.priority})");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[SurvivalTools.Harmony] Failed enumerating TryTakeOrderedJob prefixes: {ex}");
            }

            // -------- Vanilla --------
            // Mining: reset pick hit uses DiggingSpeed instead of MiningSpeed + degrade on entry
            TryPatch("JobDriver_Mine.ResetTicksToPickHit*",
                AccessTools.DeclaredMethod(typeof(JobDriver_Mine), "ResetTicksToPickHit"),
                transpiler: new HarmonyMethod(patchType, nameof(Transpile_JobDriver_Mine_ResetTicksToPickHit)));

            // Plants that obstruct construction zones
            var postfixHandleBlocking = new HarmonyMethod(patchType, nameof(Postfix_HandleBlockingThingJob));
            TryPatch("GenConstruct.HandleBlockingThingJob",
                AccessTools.Method(typeof(GenConstruct), nameof(GenConstruct.HandleBlockingThingJob)),
                postfix: postfixHandleBlocking);
            TryPatch("RoofUtility.HandleBlockingThingJob",
                AccessTools.Method(typeof(RoofUtility), nameof(RoofUtility.HandleBlockingThingJob)),
                postfix: postfixHandleBlocking);

            // PlantWork MakeNewToils
            TryPatch("JobDriver_PlantWork.MakeNewToils",
                AccessTools.DeclaredMethod(typeof(JobDriver_PlantWork), "MakeNewToils"),
                transpiler: new HarmonyMethod(patchType, nameof(Transpile_JobDriver_PlantWork_MakeNewToils)));

            // ConstructFinishFrame MakeNewToils
            TryPatch("JobDriver_ConstructFinishFrame.MakeNewToils",
                AccessTools.DeclaredMethod(typeof(JobDriver_ConstructFinishFrame), "MakeNewToils"),
                transpiler: new HarmonyMethod(patchType, nameof(Transpile_JobDriver_ConstructFinishFrame_MakeNewToils)));

            // Repair MakeNewToils
            TryPatch("JobDriver_Repair.MakeNewToils",
                AccessTools.DeclaredMethod(typeof(JobDriver_Repair), "MakeNewToils"),
                transpiler: new HarmonyMethod(patchType, nameof(Transpile_JobDriver_Repair_MakeNewToils)));

            // Deconstruct (tick)
            TryPatch("JobDriver_Deconstruct.TickActionInterval",
                AccessTools.Method(typeof(JobDriver_Deconstruct), "TickActionInterval"),
                prefix: new HarmonyMethod(patchType, nameof(Prefix_JobDriver_Deconstruct_TickActionInterval)));

            // Plant Sowing
            TryPatch("JobDriver_PlantSow.MakeNewToils",
                AccessTools.DeclaredMethod(typeof(JobDriver_PlantSow), "MakeNewToils"),
                transpiler: new HarmonyMethod(patchType, nameof(Transpile_JobDriver_PlantSow_MakeNewToils)));

            // Research
            var researchDriverType = GenTypes.GetTypeInAnyAssembly("RimWorld.JobDriver_Research");
            if (researchDriverType != null)
            {
                TryPatch("JobDriver_Research.MakeNewToils",
                    AccessTools.DeclaredMethod(researchDriverType, "MakeNewToils"),
                    transpiler: new HarmonyMethod(patchType, nameof(Transpile_JobDriver_Research_MakeNewToils)));
            }

            // AffectRoof tick (nested iterator method)
            MethodInfo FindAffectRoofTickMethod()
            {
                foreach (var t in typeof(JobDriver_AffectRoof).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                    {
                        var ps = m.GetParameters();
                        if (m.ReturnType == typeof(void) && ps.Length == 1 && ps[0].ParameterType == typeof(int) &&
                            m.Name.Contains("MakeNewToils"))
                            return m;
                    }
                return null;
            }

            TryPatch(
                "JobDriver_AffectRoof.MakeNewToils/tick",
                FindAffectRoofTickMethod(),
                transpiler: new HarmonyMethod(patchType, nameof(Transpile_AffectRoof_Tick))
            );

            // -------- Fluffy Breakdowns --------
            if (ModCompatibilityCheck.FluffyBreakdowns)
            {
                var maintenanceDriver = GenTypes.GetTypeInAnyAssembly("Fluffy_Breakdowns.JobDriver_Maintenance");
                if (maintenanceDriver != null && typeof(JobDriver).IsAssignableFrom(maintenanceDriver))
                {
                    TryPatch("Fluffy_Breakdowns.JobDriver_Maintenance.MakeNewToils*",
                        FindMakeNewToils(maintenanceDriver),
                        transpiler: new HarmonyMethod(patchType, nameof(Transpile_JobDriver_Maintenance_MakeNewToils)));
                }
                else LogError("Survival Tools - Could not find Fluffy_Breakdowns.JobDriver_Maintenance type to patch");
            }

            // -------- Quarry --------
            if (ModCompatibilityCheck.Quarry)
            {
                var quarryDriver = GenTypes.GetTypeInAnyAssembly("Quarry.JobDriver_MineQuarry");
                if (quarryDriver != null && typeof(JobDriver).IsAssignableFrom(quarryDriver))
                {
                    TryPatch("Quarry.JobDriver_MineQuarry.Mine",
                        AccessTools.Method(quarryDriver, "Mine"),
                        postfix: new HarmonyMethod(patchType, nameof(Postfix_JobDriver_MineQuarry_Mine)));
                    TryPatch("Quarry.JobDriver_MineQuarry.ResetTicksToPickHit",
                        AccessTools.Method(quarryDriver, "ResetTicksToPickHit"),
                        transpiler: new HarmonyMethod(patchType, nameof(Transpile_JobDriver_MineQuarry_ResetTicksToPickHit)));
                }
                else LogError("Survival Tools - Could not find Quarry.JobDriver_MineQuarry type to patch");
            }

            // -------- Turret Extensions --------
            if (ModCompatibilityCheck.TurretExtensions)
            {
                var upgradeDriver = GenTypes.GetTypeInAnyAssembly("TurretExtensions.JobDriver_UpgradeTurret");
                if (upgradeDriver != null && typeof(JobDriver).IsAssignableFrom(upgradeDriver))
                {
                    TryPatch("TurretExtensions.JobDriver_UpgradeTurret.Upgrade",
                        AccessTools.Method(upgradeDriver, "Upgrade"),
                        postfix: new HarmonyMethod(patchType, nameof(Postfix_JobDriver_UpgradeTurret_Upgrade)));
                }
                else LogError("Survival Tools - Could not find TurretExtensions.JobDriver_UpgradeTurret type to patch");
            }

            // -------- Combat Extended --------
            if (ModCompatibilityCheck.CombatExtended)
            {
                var holdTracker = GenTypes.GetTypeInAnyAssembly("CombatExtended.Utility_HoldTracker");
                if (holdTracker != null)
                {
                    TryPatch("CombatExtended.Utility_HoldTracker.GetExcessThing",
                        AccessTools.Method(holdTracker, "GetExcessThing"),
                        postfix: new HarmonyMethod(patchType, nameof(Postfix_CombatExtended_Utility_HoldTracker_GetExcessThing)));
                }
                else LogError("Survival Tools - Could not find CombatExtended.Utility_HoldTracker type to patch");

                var compInventory = GenTypes.GetTypeInAnyAssembly("CombatExtended.CompInventory");
                if (compInventory != null)
                {
                    // two overloads in CE 1.6: (Thing, ref int) and (ThingDef, ref int)
                    var mThing = AccessTools.GetDeclaredMethods(compInventory)
                        .FirstOrDefault(x => x.Name == "CanFitInInventory" && x.GetParameters().Length >= 1 && x.GetParameters()[0].ParameterType == typeof(Thing));
                    var mThingDef = AccessTools.GetDeclaredMethods(compInventory)
                        .FirstOrDefault(x => x.Name == "CanFitInInventory" && x.GetParameters().Length >= 1 && x.GetParameters()[0].ParameterType == typeof(ThingDef));

                    TryPatch("CE.CompInventory.CanFitInInventory(Thing)",
                        mThing, postfix: new HarmonyMethod(patchType, nameof(Postfix_CombatExtended_CompInventory_CanFitInInventoryThing)));
                    TryPatch("CE.CompInventory.CanFitInInventory(ThingDef)",
                        mThingDef, postfix: new HarmonyMethod(patchType, nameof(Postfix_CombatExtended_CompInventory_CanFitInInventoryThingDef)));
                }
                else LogError("Survival Tools - Could not find CombatExtended.CompInventory type to patch");
            }

            // -------- Prison Labor --------
            if (ModCompatibilityCheck.PrisonLabor)
            {
                var mineTweak = GenTypes.GetTypeInAnyAssembly("PrisonLabor.JobDriver_Mine_Tweak");
                if (mineTweak != null)
                {
                    TryPatch("PrisonLabor.JobDriver_Mine_Tweak.ResetTicksToPickHit",
                        AccessTools.Method(mineTweak, "ResetTicksToPickHit"),
                        transpiler: new HarmonyMethod(patchType, nameof(Transpile_JobDriver_Mine_ResetTicksToPickHit)));
                }
            }

            // NOTE: In 1.6 there is no FloatMenuMakerMap.AddHumanlikeOrders — we intentionally do NOT patch it.

            // Dev gizmo for wear simulation
            TryPatch("Pawn.GetGizmos",
                AccessTools.Method(typeof(Pawn), nameof(Pawn.GetGizmos)),
                postfix: new HarmonyMethod(patchType, nameof(Postfix_Pawn_GetGizmos)));
        }

        // ---------------- Vanilla postfixes/transpilers ----------------
        // This function needs integration with if STC is active. (We block fell tree when STC is active.) 
        public static void Postfix_HandleBlockingThingJob(ref Job __result, Pawn worker)
        {
            if (__result == null || worker == null) return;

            // Redirect tree clearing to our FellTree job if the plant is a tree and pawn is allowed
            if (__result.def == JobDefOf.CutPlant && __result.targetA.Thing?.def?.plant?.IsTree == true)
            {
                // STC authority: do not redirect; leave vanilla CutPlant (or allow other system to handle). Also avoid nulling.
                if (SurvivalTools.Helpers.TreeSystemArbiterActiveHelper.IsSTCAuthorityActive()) return;
                if (worker.CanFellTrees())
                    __result = new Job(ST_JobDefOf.FellTree, __result.targetA);
                else
                    __result = null;
            }
        }

        // Postfix to add Simulate Wear gizmo (DevMode only, selected pawn)
        public static void Postfix_Pawn_GetGizmos(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            try
            {
                if (!Prefs.DevMode || __instance == null || !Find.Selector.IsSelected(__instance)) return;
                var list = __result != null ? __result.ToList() : new List<Gizmo>();
                list.Add(new Command_Action
                {
                    defaultLabel = "ST: Sim Wear 30s",
                    defaultDesc = "Simulate 30 seconds of survival tool wear pulses on this pawn's current best tools.",
                    icon = TexCommand.DesirePower, // generic icon
                    action = () => global::SurvivalTools.DebugTools.DebugAction_SimulateWearPulses.SimulateWear()
                });
                __result = list;
            }
            catch { }
        }

        public static IEnumerable<CodeInstruction> Transpile_JobDriver_PlantWork_MakeNewToils(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var plantHarvestingSpeed = AccessTools.Field(typeof(ST_StatDefOf), nameof(ST_StatDefOf.PlantHarvestingSpeed));
            var vanillaPlantWorkSpeed = AccessTools.Field(typeof(StatDefOf), nameof(StatDefOf.PlantWorkSpeed));

            for (int i = 0; i < list.Count; i++)
            {
                var ins = list[i];

                if (ins.opcode == OpCodes.Stloc_0)
                {
                    yield return ins;                                           // keep store
                    yield return new CodeInstruction(OpCodes.Ldloc_0);           // actor
                    yield return new CodeInstruction(OpCodes.Ldsfld, plantHarvestingSpeed);
                    ins = new CodeInstruction(OpCodes.Call, TryDegradeTool);    // TryDegradeTool(actor, PlantHarvestingSpeed)
                }

                if (ins.opcode == OpCodes.Ldsfld && Equals(ins.operand, vanillaPlantWorkSpeed))
                {
                    ins.operand = plantHarvestingSpeed;                          // replace PlantWorkSpeed -> PlantHarvestingSpeed
                }

                yield return ins;
            }
        }

        public static IEnumerable<CodeInstruction> Transpile_JobDriver_Mine_ResetTicksToPickHit(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            bool injectedAtStart = false;

            for (int i = 0; i < codes.Count; i++)
            {
                var ins = codes[i];

                // Inject once at method entry:
                if (!injectedAtStart)
                {
                    // TryDegradeTool(this.pawn, ST_StatDefOf.DiggingSpeed);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);                   // this
                    yield return new CodeInstruction(OpCodes.Ldfld, PawnField);          // this.pawn
                    yield return new CodeInstruction(OpCodes.Ldsfld, DiggingSpeedField); // ST_StatDefOf.DiggingSpeed
                    yield return new CodeInstruction(OpCodes.Call, TryDegradeTool);      // call
                    injectedAtStart = true;
                }

                // Replace MiningSpeed with DiggingSpeed
                if (ins.opcode == OpCodes.Ldsfld && Equals(ins.operand, MiningSpeedField))
                    ins.operand = DiggingSpeedField;

                yield return ins;
            }
        }

        public static IEnumerable<CodeInstruction> Transpile_JobDriver_ConstructFinishFrame_MakeNewToils(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var ins = list[i];
                if (ins.opcode == OpCodes.Stloc_0)
                {
                    yield return ins;
                    yield return new CodeInstruction(OpCodes.Ldloc_0);
                    yield return new CodeInstruction(OpCodes.Ldsfld, ConstructionSpeed);
                    ins = new CodeInstruction(OpCodes.Call, TryDegradeTool);
                }
                yield return ins;
            }
        }

        public static IEnumerable<CodeInstruction> Transpile_JobDriver_Repair_MakeNewToils(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var ins = list[i];
                if (ins.opcode == OpCodes.Stloc_0)
                {
                    yield return ins;
                    yield return new CodeInstruction(OpCodes.Ldloc_0);
                    yield return new CodeInstruction(OpCodes.Ldsfld, ConstructionSpeed);
                    ins = new CodeInstruction(OpCodes.Call, TryDegradeTool);
                }
                yield return ins;
            }
        }

        public static void Prefix_JobDriver_Deconstruct_TickActionInterval(JobDriver_Deconstruct __instance)
        {
            if (__instance?.pawn != null)
                SurvivalToolUtility.TryDegradeTool(__instance.pawn, ST_StatDefOf.DeconstructionSpeed);
        }

        public static IEnumerable<CodeInstruction> Transpile_AffectRoof_Tick(
            IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var code = instructions.ToList();

            // The display class that holds the captured locals for the lambda
            var displayToilField = original.DeclaringType
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(f => f.FieldType == typeof(Toil));

            if (displayToilField == null)
            {
                if (IsDebugLoggingEnabled)
                    LogWarning("[SurvivalTools] AffectRoof tick transpiler: could not find captured Toil field; passing through.");
                for (int i = 0; i < code.Count; i++) yield return code[i];
                yield break;
            }

            var toilActorField = AccessTools.Field(typeof(Toil), nameof(Toil.actor)); // Pawn
            var constructionSpeed = AccessTools.Field(typeof(StatDefOf), nameof(StatDefOf.ConstructionSpeed));

            // Inject at method start: TryDegradeTool(this.<toil>.actor, StatDefOf.ConstructionSpeed);
            bool injected = false;
            for (int i = 0; i < code.Count; i++)
            {
                if (!injected)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);                   // this (display class)
                    yield return new CodeInstruction(OpCodes.Ldfld, displayToilField);   // this.<toil>
                    yield return new CodeInstruction(OpCodes.Ldfld, toilActorField);     // this.<toil>.actor
                    yield return new CodeInstruction(OpCodes.Ldsfld, constructionSpeed); // StatDefOf.ConstructionSpeed
                    yield return new CodeInstruction(OpCodes.Call, TryDegradeTool);      // call TryDegradeTool
                    injected = true;
                }

                yield return code[i];
            }
        }

        // ---------- Modded JobDrivers ----------

        public static IEnumerable<CodeInstruction> Transpile_JobDriver_Maintenance_MakeNewToils(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var ins = list[i];
                if (ins.opcode == OpCodes.Stloc_0)
                {
                    yield return ins;
                    yield return new CodeInstruction(OpCodes.Ldloc_0);
                    yield return new CodeInstruction(OpCodes.Ldsfld, MaintenanceSpeedField);
                    ins = new CodeInstruction(OpCodes.Call, TryDegradeTool);
                }
                yield return ins;
            }
        }

        public static IEnumerable<CodeInstruction> Transpile_JobDriver_PlantSow_MakeNewToils(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var ins = list[i];
                if (ins.opcode == OpCodes.Stloc_0)
                {
                    yield return ins;
                    yield return new CodeInstruction(OpCodes.Ldloc_0);
                    yield return new CodeInstruction(OpCodes.Ldsfld, SowingSpeedField);
                    ins = new CodeInstruction(OpCodes.Call, TryDegradeTool);
                }
                yield return ins;
            }
        }

        public static IEnumerable<CodeInstruction> Transpile_JobDriver_Research_MakeNewToils(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var ins = list[i];
                if (ins.opcode == OpCodes.Stloc_0)
                {
                    yield return ins;
                    yield return new CodeInstruction(OpCodes.Ldloc_0);
                    yield return new CodeInstruction(OpCodes.Ldsfld, ResearchSpeedField);
                    ins = new CodeInstruction(OpCodes.Call, TryDegradeTool);
                }
                yield return ins;
            }
        }

        public static IEnumerable<CodeInstruction> Transpile_JobDriver_MineQuarry_ResetTicksToPickHit(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var miningSpeed = AccessTools.Field(typeof(StatDefOf), nameof(StatDefOf.MiningSpeed));
            var diggingSpeed = AccessTools.Field(typeof(ST_StatDefOf), nameof(ST_StatDefOf.DiggingSpeed));

            for (int i = 0; i < list.Count; i++)
            {
                var ins = list[i];
                if (ins.opcode == OpCodes.Ldsfld && Equals(ins.operand, miningSpeed))
                    ins.operand = diggingSpeed;
                yield return ins;
            }
        }

        public static void Postfix_JobDriver_MineQuarry_Mine(JobDriver __instance, Toil __result)
        {
            if (__instance == null || __result == null) return;

            var tickAction = __result.tickAction;
            var pawn = __instance.pawn;

            __result.tickAction = () =>
            {
                if (pawn != null)
                    SurvivalToolUtility.TryDegradeTool(pawn, ST_StatDefOf.DiggingSpeed);
                tickAction?.Invoke();
            };

            if (pawn != null)
                __result.defaultDuration = (int)Mathf.Clamp(3000f / pawn.GetStatValue(ST_StatDefOf.DiggingSpeed), 500f, 10000f);
        }

        public static void Postfix_JobDriver_UpgradeTurret_Upgrade(JobDriver __instance, Toil __result)
        {
            if (__instance == null || __result == null) return;

            var tickAction = __result.tickAction;
            var pawn = __instance.pawn;

            __result.tickAction = () =>
            {
                if (pawn != null)
                    SurvivalToolUtility.TryDegradeTool(pawn, StatDefOf.ConstructionSpeed);
                tickAction?.Invoke();
            };
        }

        // ---------- Combat Extended hooks ----------

        public static void Postfix_CombatExtended_Utility_HoldTracker_GetExcessThing(ref bool __result, Thing dropThing)
        {
            if (__result && dropThing is SurvivalTool)
                __result = false;
        }

        public static void Postfix_CombatExtended_CompInventory_CanFitInInventoryThing(ThingComp __instance, ref bool __result, Thing thing, ref int count)
        {
            if (__result && thing is SurvivalTool && __instance?.parent is Pawn pawn && !pawn.CanCarryAnyMoreSurvivalTools())
            {
                count = 0;
                __result = false;
            }
        }

        public static void Postfix_CombatExtended_CompInventory_CanFitInInventoryThingDef(ThingComp __instance, ref bool __result, ThingDef thingDef, ref int count)
        {
            if (__result && thingDef != null && typeof(SurvivalTool).IsAssignableFrom(thingDef.thingClass) && __instance?.parent is Pawn pawn && !pawn.CanCarryAnyMoreSurvivalTools())
            {
                count = 0;
                __result = false;
            }
        }
    }
}
