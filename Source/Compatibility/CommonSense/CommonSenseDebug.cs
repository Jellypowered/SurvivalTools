// RimWorld 1.6 / C# 7.3
// Source/Compatibility/CommonSense/CommonSenseDebug.cs
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Compatibility.CommonSense
{
    // Pacifist equip handled centrally in Patch_EquipmentUtility_CanEquip_PacifistTools.cs
    /// <summary>
    /// Debug helpers for CommonSense compatibility.
    /// Use to output detailed diagnostic info when developing compat code.
    /// </summary>
    internal static class CommonSenseDebug
    {
        public static void Log(string msg)
        {
            try
            {
                if (!CommonSenseHelpers.Active) return;
                LogCompatMessage(msg);
            }
            catch
            {
                // swallow
            }
        }
    }
}
