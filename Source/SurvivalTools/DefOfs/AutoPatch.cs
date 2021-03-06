﻿using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using RimWorld;
using Verse;
using HarmonyLib;
using UnityEngine;

namespace SurvivalTools
{
    public static class AutoPatch
    {
        public static List<StatPatchDef> StatsToPatch = DefDatabase<StatPatchDef>.AllDefsListForReading;

    }
    public class StatPatchDef : Def
    {
        #region Fields and Properties
        // Patched stat definition
        public StatDef oldStat;
        public StatDef newStat;
        public Type StatReplacer;
        public List<StatDef> potentialStats = new List<StatDef>();
        // Stat patching utility
        public FieldInfo oldStatFieldInfo;
        public FieldInfo newStatFieldInfo;
        private Type oldStatType;
        private Type newStatType;
        public List<CodeInstruction> Stat_CodeInstructions
            => (List<CodeInstruction>) StatReplacer_CodeInstructions.GetValue(null);
        private MethodInfo StatReplacer_Initialize;
        private PropertyInfo StatReplacer_CodeInstructions;
        public bool canPatch;
        // Patch jobDrivers to use ST stats
        private bool patchAllJobDrivers = true;
        private List<Type> JobDriverExemption = new List<Type>();
        private List<Type> JobDriverList = new List<Type>();
        public List<Type> OtherTypes = new List<Type>();
        // Patch workGivers to require tool
        public bool patchAllWorkGivers = true;
        private List<Type> WorkGiverExemption = new List<Type>();
        public List<Type> WorkGiverList = new List<Type>();
        public List<JobDriverPatch> FoundJobDrivers = new List<JobDriverPatch>();
        public List<WorkGiverPatch> FoundWorkGivers = new List<WorkGiverPatch>();
        public List<JobDefPatch> FoundJobDef = new List<JobDefPatch>();
        public bool skip;
        public bool addToolDegrade = true;
        #endregion
        #region Methods
        public void Initialize()
        {
            List<Type> StatDefOfTypes = GenTypes.AllTypesWithAttribute<DefOf>().
                Where(t => t.GetFields().FirstOrDefault(tt => tt.FieldType == typeof(StatDef)) != null).ToList();
            StringBuilder BaseMessage = new StringBuilder("[SurivalTools.AutoPatcher] : ");
            // Initialize oldStat FieldInfo
            if (oldStatType is null)
            {
                List<Type> foundTypes = StatDefOfTypes.Where(t => AccessTools.Field(t, oldStat.defName) != null).ToList();
                if (foundTypes.Count == 0)
                    Log.Error(BaseMessage.ToString() + $"Did not find StatDefOf: [{oldStat}] : Please include it somewhere.");
                else if (foundTypes.Count > 1)
                {
                    // This has no effect, I just want to prefer vanilla
                    if (foundTypes.Contains(typeof(StatDefOf)))
                        oldStatType = typeof(StatDefOf);
                    else if (foundTypes.Contains(typeof(ST_StatDefOf)))
                        oldStatType = typeof(ST_StatDefOf);
                    else
                        oldStatType = foundTypes[0];
                }
                else
                    oldStatType = foundTypes[0];
            }
            oldStatFieldInfo = AccessTools.Field(oldStatType, oldStat.defName);
            // Initialize newStat FieldInfo
            if (newStatType is null && newStat != null)
            {
                List<Type> foundTypes = StatDefOfTypes.Where(t => AccessTools.Field(t, newStat.defName) != null).ToList();
                if (foundTypes.Count == 0)
                    Log.Error(BaseMessage.ToString() + $"Did not find StatDefOf: [{newStat}] : Please include it somewhere.");
                else if (foundTypes.Count > 1)
                {
                    if (foundTypes.Contains(typeof(StatDefOf)))
                        newStatType = typeof(StatDefOf);
                    else if (foundTypes.Contains(typeof(ST_StatDefOf)))
                        newStatType = typeof(ST_StatDefOf);
                    else
                        newStatType = foundTypes[0];
                }
                else
                    newStatType = foundTypes[0];
            }
            if (newStat != null)
                newStatFieldInfo = AccessTools.Field(newStatType, newStat.defName);
            // Find StatReplacer.Initialize()
            if (StatReplacer != null)
            {
                StatReplacer_Initialize = AccessTools.Method(StatReplacer, "Initialize");
                StatReplacer_CodeInstructions = AccessTools.Property(StatReplacer, "CodeInstructions");
            }
        }
        public void Initialize_StatReplacer(Type jobDriver, Type nestedClass = null)
        {
            canPatch = (bool)StatReplacer_Initialize.Invoke(null, new object[] { jobDriver, nestedClass });
        }
           
        public void CheckJobDriver(Type jd)
        {
            if (patchAllJobDrivers)
                skip = JobDriverExemption.Contains(jd) ? true : false;
            else
                skip = JobDriverList.Contains(jd) ? false : true;
        }
        public void CheckWorkGiver(Type wg)
        {
            if (patchAllWorkGivers)
                skip = WorkGiverExemption.Contains(wg) ? true : false;
            else
                skip = true;
        }
        public bool CheckIfValidPatch()
        {
            if (oldStat is null)
                return false;
            if (!patchAllJobDrivers && (JobDriverList.Count == 0) && (OtherTypes.Count == 0))
                return false;
            return true;
        }
        #endregion
    }
    public class JobDriverPatch
    {
        public Type driver;
        public List<MethodInfo> methods = new List<MethodInfo>();
        public List<MethodInfo> auxmethods = new List<MethodInfo>();
        public List<MethodInfo> degradeMethods = new List<MethodInfo>();
        public bool FoundStage1 = false;
        public bool FoundStage2 = false;
        public JobDriverPatch(Type Driver) { driver = Driver; }
    }
    public class JobDefPatch
    {
        public JobDef def;
        public FieldInfo fieldInfo;
        public JobDefPatch(JobDef Def, FieldInfo FInfo) { def = Def; fieldInfo = FInfo; }
    }
    public class WorkGiverPatch
    {
        public Type giver;
        public List<MethodInfo> methods = new List<MethodInfo>();
        public List<PropertyInfo> properties = new List<PropertyInfo>();
        public List<FieldInfo> fields = new List<FieldInfo>();
        public WorkGiverPatch(Type Giver) { giver = Giver; }
    }
}
