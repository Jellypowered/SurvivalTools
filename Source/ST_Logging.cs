using Verse;

namespace SurvivalTools
{
    /// <summary>
    /// Centralized logging helpers for Survival Tools.
    /// </summary>
    public static class ST_Logging
    {
        public static bool IsDebugLoggingEnabled => SurvivalToolUtility.IsDebugLoggingEnabled;

        public static bool ShouldLogWithCooldown(string logKey) => SurvivalToolUtility.ShouldLogWithCooldown(logKey);

        public static void LogDebug(string message, string logKey = null)
        {
            if (!IsDebugLoggingEnabled) return;

            if (logKey != null && !ShouldLogWithCooldown(logKey))
                return;

            Log.Message(message);
        }

        public static void LogWarning(string message)
        {
            Log.Warning($"[SurvivalTools] {message}");
        }

        public static void LogError(string message)
        {
            Log.Error($"[SurvivalTools] {message}");
        }
    }
}
