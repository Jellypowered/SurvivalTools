// RimWorld 1.6 / C# 7.3
// Source/Helpers/ST_FileIO.cs
using System;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace SurvivalTools
{
    internal static class ST_FileIO
    {
        internal static string DesktopPath()
        {
            try
            {
                var p = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (!string.IsNullOrEmpty(p)) return p;
            }
            catch { /* ignore */ }
            // Fallbacks for platforms without a Desktop
            return Application.persistentDataPath ?? GenFilePaths.SaveDataFolderPath;
        }

        internal static string WriteUtf8Atomic(string fileName, string content)
        {
            var dir = DesktopPath();
            try { Directory.CreateDirectory(dir); } catch { }
            var path = Path.Combine(dir, fileName);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            File.Move(tmp, path);
            return path;
        }
    }
}