// RimWorld 1.6 / C# 7.3
// Source/Compatibility/TreeStack/TreeStatAliases.cs
// Registers aliases between external tree chop stats and internal TreeFellingSpeed.

using RimWorld;
using Verse;

namespace SurvivalTools.Compatibility.TreeStack
{
    internal static class TreeStatAliases
    {
        internal static void Initialize()
        {
            var tcssStat = DefDatabase<StatDef>.GetNamedSilentFail("TreeChoppingSpeed")
                           ?? DefDatabase<StatDef>.GetNamedSilentFail("TreeCuttingSpeed");

            if (tcssStat != null && ST_StatDefOf.TreeFellingSpeed != null)
            {
                // Use new StatDef<->StatDef aliasing so either stat satisfied by tools providing the other.
                Compat.CompatAPI.RegisterStatAlias(tcssStat, ST_StatDefOf.TreeFellingSpeed);
                Compat.CompatAPI.RegisterStatAlias(ST_StatDefOf.TreeFellingSpeed, tcssStat); // bidirectional for safety
            }
        }
    }
}
