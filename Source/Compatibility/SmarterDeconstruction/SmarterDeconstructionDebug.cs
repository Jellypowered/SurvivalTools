// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SmarterDeconstruction/SmarterDeconstructionDebug.cs
using Verse;

// Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Compat.SmarterDeconstruction
{
    internal static class SmarterDeconstructionDebug
    {
        public static void D(string msg)
        {
            try
            {
                if (!SmarterDeconstructionHelpers.IsSmarterDeconstructionActive()) return;
                LogCompatMessage("[SmarterDeconstruction] " + msg, "SmarterDeconstruction.Debug");
            }
            catch { }
        }
    }
}
