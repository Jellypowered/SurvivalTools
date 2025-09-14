// RimWorld 1.6 / C# 7.3
// Source/Compatibility/PrimitiveTools/PTDebug.cs

namespace SurvivalTools.Compat.PrimitiveTools
{
    // Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs
    // Minimal debug helpers for PrimitiveTools compatibility.
    public static class PrimitiveToolsDebug
    {
        public static void LogStatus()
        {
            if (ST_Logging.IsDebugLoggingEnabled && ST_Logging.IsCompatLogging())
            {
                ST_Logging.LogCompatMessage($"PrimitiveTools active={PrimitiveToolsHelpers.IsPrimitiveToolsActive()}");
            }
        }
    }
}
