using Verse;

namespace SurvivalTools.Compat.PrimitiveTools
{
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
