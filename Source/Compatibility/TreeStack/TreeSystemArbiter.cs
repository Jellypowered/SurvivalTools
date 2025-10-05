// RimWorld 1.6 / C# 7.3
// Source/Compatibility/TreeStack/TreeSystemArbiter.cs
// Determines which mod owns tree-chop behavior & disables internal system when external present.

using System;
using System.Linq;
using Verse;

namespace SurvivalTools.Compatibility.TreeStack
{
    internal enum TreeAuthority { None, SeparateTreeChopping, TreeChoppingSpeedStat, PrimitiveTools_TCSS, Internal }

    [StaticConstructorOnStartup]
    internal static class TreeSystemArbiter
    {
        internal static TreeAuthority Authority { get; private set; } = TreeAuthority.None;

        internal static bool STC_Active => LoadedModManager.RunningModsListForReading.Any(m => (m.Name?.IndexOf("Separate Tree Chopping", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
        internal static bool PT_Active => LoadedModManager.RunningModsListForReading.Any(m => (m.Name?.IndexOf("Primitive Tools", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
        internal static bool TCSS_Active => LoadedModManager.RunningModsListForReading.Any(m => (m.Name?.IndexOf("Tree Chopping Speed", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
        internal static bool PrimitiveCore_Active => LoadedModManager.RunningModsListForReading.Any(m => (m.Name?.IndexOf("Primitive Core", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);

        static TreeSystemArbiter()
        {
            try
            {
                // Priority: STC > TCSS (standalone) > PT+TCSS > Internal
                if (STC_Active)
                    Authority = TreeAuthority.SeparateTreeChopping;
                else if (TCSS_Active && !PT_Active)
                    Authority = TreeAuthority.TreeChoppingSpeedStat;
                else if (PT_Active && TCSS_Active)
                    Authority = TreeAuthority.PrimitiveTools_TCSS;
                else
                    Authority = TreeAuthority.Internal;

                Log.Message($"[ST] TreeSystemArbiter → {Authority} (STC={STC_Active}, PT={PT_Active}, TCSS={TCSS_Active}, PC={PrimitiveCore_Active})");

                if (Authority != TreeAuthority.Internal && SurvivalToolsMod.Settings != null && SurvivalToolsMod.Settings.enableSurvivalToolTreeFelling)
                {
                    SurvivalToolsMod.Settings.enableSurvivalToolTreeFelling = false;
                    Log.Message("[ST] Internal tree felling disabled (external authority present).");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[ST] TreeSystemArbiter init failed: " + e.Message);
            }
        }
    }
}
