// RimWorld 1.6 / C# 7.3
// Source/Compatibility/SeparateTreeChopping/STCDebug.cs

// Updated to Phase 10 conflict API
using SurvivalTools.Compatibility.SeparateTreeChopping;

namespace SurvivalTools.Compatibility.SeparateTreeChopping
{
    public static class SeparateTreeChoppingDebug
    {
        // Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs
        public static void LogStatus()
        {
            if (ST_Logging.IsDebugLoggingEnabled && ST_Logging.IsCompatLogging())
                ST_Logging.LogCompatMessage($"SeparateTreeChopping active={SeparateTreeChoppingConflict.IsSeparateTreeChoppingActive()} conflict={SeparateTreeChoppingConflict.HasTreeFellingConflict()}");
        }
    }
}
