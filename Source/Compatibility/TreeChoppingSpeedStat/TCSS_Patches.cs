// RimWorld 1.6 / C# 7.3
// Source/Compatibility/TreeChoppingSpeedStat/TCSS_Patches.cs
// Harmony patches for Tree Chopping Speed Stat (TCSS) mod integration.
// Strips SurvivalTools FellTree WorkGivers when TCSS has authority, allowing TCSS to govern tree chopping.

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SurvivalTools.Compat.TCSS
{
    internal static class TCSS_Patches
    {
        private static bool _initialized;

        /// <summary>
        /// Initialize TCSS compatibility patches.
        /// Strips SurvivalTools FellTree WorkGivers if TCSS has authority.
        /// Does NOT patch vanilla WorkGiver_PlantsCut (unlike STC).
        /// Does NOT bypass TreeFellingSpeed gating.
        /// </summary>
        public static void Init(Harmony h)
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                // Early exit if TCSS not detected
                if (!TCSS_Helpers.IsActive())
                {
                    if (TCSS_Debug.IsCompatLogging())
                        TCSS_Debug.LogCompat($"[{TCSS_Helpers.ShortName}] Not detected, skipping patches.");
                    return;
                }

                // If STC is also detected, STC takes authority
                var stcAuth = SurvivalTools.Compatibility.TreeStack.TreeSystemArbiter.Authority;
                if (stcAuth == SurvivalTools.Compatibility.TreeStack.TreeAuthority.SeparateTreeChopping)
                {
                    TCSS_Debug.LogCompatWarning($"[{TCSS_Helpers.ShortName}] Both TCSS and STC found; STC takes authority. Skipping TCSS patches.");
                    return;
                }

                // TCSS has authority: apply patches
                if (TCSS_Debug.IsCompatLogging())
                    TCSS_Debug.LogCompat($"[{TCSS_Helpers.ShortName}] Detected, applying compatibility patches.");

                StripSTFellTreeWorkGivers();
            }
            catch (Exception ex)
            {
                TCSS_Debug.LogCompatWarning($"[{TCSS_Helpers.ShortName}] Init exception: {ex}");
            }
        }

        /// <summary>
        /// Strip SurvivalTools FellTree WorkGivers from all WorkTypeDefs when TCSS has authority.
        /// This prevents ST from offering "Fell tree" options, allowing TCSS to govern tree chopping.
        /// </summary>
        private static void StripSTFellTreeWorkGivers()
        {
            try
            {
                var fellTreesWG = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ST_FellTrees");
                var fellTreesDesignatedWG = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ST_FellTreesDesignated");

                if (fellTreesWG == null && fellTreesDesignatedWG == null)
                {
                    if (TCSS_Debug.IsCompatLogging())
                        TCSS_Debug.LogCompat($"[{TCSS_Helpers.ShortName}] No ST FellTree WorkGivers found to strip.");
                    return;
                }

                var removed = new List<string>();
                var workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;

                for (int i = 0; i < workTypes.Count; i++)
                {
                    var workType = workTypes[i];
                    if (workType?.workGiversByPriority == null) continue;

                    int preCount = workType.workGiversByPriority.Count;

                    if (fellTreesWG != null)
                        workType.workGiversByPriority.RemoveAll(wg => wg == fellTreesWG);

                    if (fellTreesDesignatedWG != null)
                        workType.workGiversByPriority.RemoveAll(wg => wg == fellTreesDesignatedWG);

                    int postCount = workType.workGiversByPriority.Count;

                    if (preCount != postCount)
                        removed.Add($"{workType.defName}({preCount}->{postCount})");
                }

                if (removed.Count > 0 && TCSS_Debug.IsCompatLogging())
                {
                    string summary = string.Join(", ", removed.ToArray());
                    TCSS_Debug.LogCompat($"[{TCSS_Helpers.ShortName}] Removed ST FellTree WGs under TCSS authority: {summary}");
                }
                else if (removed.Count == 0 && TCSS_Debug.IsCompatLogging())
                {
                    TCSS_Debug.LogCompat($"[{TCSS_Helpers.ShortName}] No ST FellTree WGs were registered to remove.");
                }
            }
            catch (Exception ex)
            {
                TCSS_Debug.LogCompatWarning($"[{TCSS_Helpers.ShortName}] StripSTFellTreeWorkGivers exception: {ex}");
            }
        }
    }
}
