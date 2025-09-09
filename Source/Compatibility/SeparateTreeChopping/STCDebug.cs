using Verse;

namespace SurvivalTools.Compat.SeparateTreeChopping
{
    public static class SeparateTreeChoppingDebug
    {
        public static void LogStatus()
        {
            if (ST_Logging.IsDebugLoggingEnabled && ST_Logging.IsCompatLogging())
                ST_Logging.LogCompatMessage($"SeparateTreeChopping active={SeparateTreeChoppingHelpers.IsSeparateTreeChoppingActive()}");
        }
    }
}
