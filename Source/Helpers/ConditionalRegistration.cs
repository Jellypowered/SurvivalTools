using System.Linq;
using RimWorld;
using Verse;
using SurvivalTools.Helpers;
using SurvivalTools.Compat;
using SurvivalTools.Compat.SeparateTreeChopping;

namespace SurvivalTools
{
    /// <summary>
    /// Handles conditional registration/deregistration of SurvivalTools features
    /// (currently only tree felling) based on settings and compat checks.
    /// 
    /// 🔮 Future: This can be expanded into a general-purpose feature toggle system.
    ///    Example: gating medical tool usage, research tool requirements, etc.,
    ///    with helpers to both disable and re-enable features at runtime instead of
    ///    only removing WorkGivers.
    /// </summary>
    public static class ConditionalRegistration
    {
        private static bool _hasAppliedTreeFellingConditionals = false;

        public static void ApplyTreeFellingConditionals()
        {
            if (_hasAppliedTreeFellingConditionals) return;
            _hasAppliedTreeFellingConditionals = true;

            var enabled = SurvivalTools.Settings?.enableSurvivalToolTreeFelling ?? true;

            Log.Message($"[SurvivalTools] Applying tree felling conditionals: enabled={enabled}");

            if (!enabled)
            {
                DisableTreeFellingFeatures();
            }
        }

        private static void DisableTreeFellingFeatures()
        {
            SafetyUtils.SafeExecute(() =>
            {
                var fellTreesWG = DefDatabase<WorkGiverDef>.GetNamed("ST_FellTrees", errorOnFail: false);
                if (fellTreesWG != null)
                {
                    foreach (var workType in DefDatabase<WorkTypeDef>.AllDefs)
                    {
                        workType.workGiversByPriority?.RemoveAll(wg => wg == fellTreesWG);
                    }

                    Log.Message("[SurvivalTools] Disabled ST_FellTrees WorkGiver due to setting.");
                }
            }, "DisableTreeFellingFeatures");
        }

        public static void ResetConditionals()
        {
            _hasAppliedTreeFellingConditionals = false;
        }

        public static bool IsTreeFellingEnabled()
        {
            var setting = SurvivalTools.Settings?.enableSurvivalToolTreeFelling ?? true;

            if (SeparateTreeChoppingCompat.IsSeparateTreeChoppingActive() &&
                SeparateTreeChoppingCompat.HasTreeFellingConflict())
            {
                return false;
            }

            return setting;
        }
    }
}
