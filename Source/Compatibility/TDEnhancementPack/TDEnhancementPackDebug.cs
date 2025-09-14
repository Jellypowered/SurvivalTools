// RimWorld 1.6 / C# 7.3
// Source/Compatibility/TDEnhancementPack/TDEnhancementPackDebug.cs
using RimWorld;
using Verse;
using LudeonTK;

// Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Compat.TDEnhancementPack
{
    internal static class TDEnhancementPackDebug
    {
        public static void LogStatus()
        {
            try
            {
                Log.Message($"[SurvivalTools Compat] TD Enhancement Pack active: {TDEnhancementPackHelpers.IsTDEnhancementPackActive()}");
            }
            catch { }
        }

        public static void D(string msg)
        {
            try
            {
                if (!TDEnhancementPackHelpers.IsTDEnhancementPackActive()) return;
                LogCompatMessage("[TDEnhancementPack] " + msg, "TDEnhancementPack.Debug");
            }
            catch { }
        }
    }
}
