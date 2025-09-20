// RimWorld 1.6 / C# 7.3
// Source/DebugTools/DebugAction_DumpStatus.cs
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using LudeonTK;

namespace SurvivalTools
{
    internal static class ST_DebugActions
    {
        [LudeonTK.DebugAction("ST", "Dump ST status → Desktop", false, false)]
        private static void DumpStatus()
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("[SurvivalTools] Status dump");
            sb.AppendLine("----------------------------");
            try { sb.AppendLine($"Settings: hardcore={(SurvivalTools.Settings?.hardcoreMode == true)} extraHardcore={(SurvivalTools.Settings?.extraHardcoreMode == true)}"); } catch { }
            try { sb.AppendLine($"Tools present: {DefDatabase<ThingDef>.AllDefsListForReading?.FindAll(t => t != null && t.IsSurvivalTool()).Count}"); } catch { }
            try { sb.AppendLine($"WorkSpeedGlobal jobs discovered: {Helpers.WorkSpeedGlobalHelper.GetWorkSpeedGlobalJobs().Count()}"); } catch { }
            try { sb.AppendLine("Active mods carrying ST hooks:"); sb.AppendLine(CompatLine()); } catch { }
            sb.AppendLine();
            sb.AppendLine("Tip: use this after loading a save and mining a tile to verify wear/penalties.");
            var path = ST_FileIO.WriteUtf8Atomic($"ST_Status_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt", sb.ToString());
            try { Messages.Message("Survival Tools: wrote " + path, MessageTypeDefOf.TaskCompletion); } catch { }
        }

        private static string CompatLine()
        {
            try { return string.Join(", ", Helpers.WorkSpeedGlobalHelper.GetWorkSpeedGlobalJobs().Select(wg => wg.defName)); } catch { return "(n/a)"; }
        }
    }
}