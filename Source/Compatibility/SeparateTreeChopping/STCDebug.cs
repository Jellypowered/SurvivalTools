// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SeparateTreeChopping/STCDebug.cs

namespace SurvivalTools.Compat.SeparateTreeChopping
{
    public static class SeparateTreeChoppingDebug
    {
        // Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs
        public static void LogStatus()
        {
            if (ST_Logging.IsDebugLoggingEnabled && ST_Logging.IsCompatLogging())
                ST_Logging.LogCompatMessage($"SeparateTreeChopping active={SeparateTreeChoppingHelpers.IsSeparateTreeChoppingActive()}");
        }
    }
}
