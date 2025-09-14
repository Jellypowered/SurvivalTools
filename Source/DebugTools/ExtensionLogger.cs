// RimWorld 1.6 / C# 7.3
// Source/DebugTools/ExtensionLogger.cs
using System;
//
// Debug utility for logging ThingDefs with SurvivalToolProperties and dumping Harmony patches.
// Runs only when triggered from the in-game Debug Actions menu.

using HarmonyLib;
using RimWorld;
using System.Linq;
using Verse;
using SurvivalTools.Helpers;
using static SurvivalTools.ST_Logging;
using LudeonTK;

namespace SurvivalTools.DebugTools
{
    public static class SurvivalToolExtensionLogger
    {
#if DEBUG
        [DebugAction("SurvivalTools", "Dump SurvivalTool extensions + Harmony patches")]
        public static void DumpExtensionsAndPatches_DebugAction()
        {
            try
            {
                LogSurvivalToolExtensions();
                LogHarmonyPatches();
                LogInfo("[SurvivalTools] ExtensionLogger: dump complete.");
            }
            catch (System.Exception e)
            {
                LogError($"[SurvivalTools] ExtensionLogger failed: {e}");
            }
        }
#endif

        private static void LogSurvivalToolExtensions()
        {
            var defsWithExt = DefDatabase<ThingDef>.AllDefs
                .Where(d => d.GetModExtension<SurvivalToolProperties>() != null)
                .ToList();

            LogInfo($"[SurvivalTools] Found {defsWithExt.Count} ThingDefs with SurvivalToolProperties extension applied.");

            foreach (var def in defsWithExt)
            {
                var ext = def.GetModExtension<SurvivalToolProperties>();
                LogInfo($"[SurvivalTools] {def.defName} has SurvivalToolProperties.");

                if (ext.baseWorkStatFactors != null && ext.baseWorkStatFactors.Count > 0)
                {
                    foreach (var factor in ext.baseWorkStatFactors)
                    {
                        if (factor?.stat != null)
                            LogInfo($"    - {factor.stat.defName}: {factor.value.ToStringPercent()}");
                    }
                }
                else
                {
                    LogInfo("    (no baseWorkStatFactors defined)");
                }

                if (ext.toolWearFactor > 0f)
                    LogInfo($"    - toolWearFactor: {ext.toolWearFactor}");

                if (ext.defaultSurvivalToolAssignmentTags != null && ext.defaultSurvivalToolAssignmentTags.Count > 0)
                {
                    var tags = string.Join(", ", ext.defaultSurvivalToolAssignmentTags);
                    LogInfo($"    - defaultSurvivalToolAssignmentTags: {tags}");
                }
            }
        }

        private static void LogHarmonyPatches()
        {
            var method = AccessTools.Method(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats));
            var patches = Harmony.GetPatchInfo(method);

            if (patches == null)
            {
                LogInfo("[SurvivalTools] No patches found on ThingDef.SpecialDisplayStats");
                return;
            }

            LogInfo("[SurvivalTools] Patches on ThingDef.SpecialDisplayStats:");

            foreach (var patch in patches.Postfixes)
                LogInfo($"  POSTFIX: {patch.PatchMethod.FullDescription()} (Owner={patch.owner}, Priority={patch.priority})");

            foreach (var patch in patches.Prefixes)
                LogInfo($"  PREFIX: {patch.PatchMethod.FullDescription()} (Owner={patch.owner}, Priority={patch.priority})");

            foreach (var patch in patches.Transpilers)
                LogInfo($"  TRANSPILER: {patch.PatchMethod.FullDescription()} (Owner={patch.owner}, Priority={patch.priority})");
        }
    }
}
