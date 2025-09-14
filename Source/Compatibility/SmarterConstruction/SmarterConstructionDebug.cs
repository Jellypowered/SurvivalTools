// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SmarterConstruction/SmarterConstructionDebug.cs
using Verse;

// Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Compat.SmarterConstruction
{
    internal static class SmarterConstructionDebug
    {
        public static void D(string msg)
        {
            try
            {
                if (!SmarterConstructionHelpers.IsSmarterConstructionActive()) return;
                LogCompatMessage("[SmarterConstruction] " + msg, "SmarterConstruction.Debug");
            }
            catch { }
        }
    }
}
