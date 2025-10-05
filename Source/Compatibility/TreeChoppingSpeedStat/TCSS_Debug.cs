// RimWorld 1.6 / C# 7.3
// Source/Compatibility/TreeChoppingSpeedStat/TCSS_Debug.cs
// Debug and logging helpers for TCSS compatibility.

namespace SurvivalTools.Compat.TCSS
{
    internal static class TCSS_Debug
    {
        /// <summary>
        /// Proxy to central ST_Logging.IsCompatLogging() to check if compat logging is enabled.
        /// </summary>
        public static bool IsCompatLogging()
        {
            try
            {
                return ST_Logging.IsCompatLogging();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Log a compat message with cooldown safety via ST_Logging.
        /// </summary>
        public static void LogCompat(string msg, string key = null)
        {
            try
            {
                ST_Logging.LogCompatMessage(msg, key);
            }
            catch
            {
                // Fallback: silent fail if logging system unavailable
            }
        }

        /// <summary>
        /// Log a compat warning with cooldown safety via ST_Logging.
        /// </summary>
        public static void LogCompatWarning(string msg)
        {
            try
            {
                ST_Logging.LogCompatWarning(msg);
            }
            catch
            {
                // Fallback: silent fail if logging system unavailable
            }
        }
    }
}
