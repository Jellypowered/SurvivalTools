using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using SurvivalTools.Helpers;

namespace SurvivalTools
{
    /// <summary>
    /// Window for configuring which WorkSpeedGlobal jobs should be gated by survival tools
    /// </summary>
    public class WorkSpeedGlobalConfigWindow : Window
    {
        private SurvivalToolsSettings settings;
        private Vector2 scrollPosition;
        private List<WorkGiverDef> workSpeedJobs;

        // Code-side exclusion list (never shown in the UI)
        private static readonly HashSet<string> excludedJobs = new HashSet<string>
        {
            // Example: add defNames here that should never show in the list
            // "CookMealFine",
            // "CookMealLavish",
        };

        public override Vector2 InitialSize => new Vector2(700f, 550f); // Increased width for better text fit

        public WorkSpeedGlobalConfigWindow(SurvivalToolsSettings settings)
        {
            this.settings = settings;
            this.doCloseButton = true;
            this.doCloseX = true;
            this.absorbInputAroundWindow = true;
            this.closeOnClickedOutside = false;

            InitializeWorkSpeedJobs();
        }

        private void InitializeWorkSpeedJobs()
        {
            // Get all work givers that use WorkSpeedGlobal, filter excluded and non-gate-eligible
            workSpeedJobs = WorkSpeedGlobalHelper.GetWorkSpeedGlobalJobs()
                .Where(j => !excludedJobs.Contains(j.defName) && SurvivalToolUtility.ShouldGateByDefault(j))
                .ToList();

            // Initialize any missing job configurations to true (gated by default)
            foreach (var job in workSpeedJobs)
            {
                if (!settings.workSpeedGlobalJobGating.ContainsKey(job.defName))
                {
                    settings.workSpeedGlobalJobGating[job.defName] = true; // Default to gated
                }
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            var prevAnchor = Text.Anchor;
            var prevFont = Text.Font;
            var prevColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;

                // Title
                Text.Font = GameFont.Medium;
                var titleRect = new Rect(inRect.x, inRect.y, inRect.width, 30f);
                Widgets.Label(titleRect, "WorkSpeedGlobal Job Configuration");
                Text.Font = GameFont.Small;

                // Description
                var descRect = new Rect(inRect.x, titleRect.yMax + 5f, inRect.width, 50f);
                Widgets.Label(descRect, "Configure which jobs that use 'general work speed' should be gated by survival tools. Unchecked jobs will not be penalized when no tools are available.");

                // Reserve space for bottom buttons (plus padding and close button)
                const float bottomButtonHeight = 40f;
                const float bottomPadding = 50f; // Extra space for RimWorld's close button
                const float totalBottomSpace = bottomButtonHeight + bottomPadding;

                // Pinned Header area
                const float headerHeight = 25f;
                var headerRect = new Rect(inRect.x, descRect.yMax + 10f, inRect.width, headerHeight);

                // Draw pinned header background
                GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
                Widgets.DrawBoxSolid(headerRect, GUI.color);
                GUI.color = new Color(0.6f, 0.6f, 0.6f, 1f);
                Widgets.DrawBox(headerRect, 1);

                // Header content with proper column alignment
                Text.Anchor = TextAnchor.MiddleCenter;

                // Calculate dynamic column widths based on content
                var measuringFont = Text.Font;
                Text.Font = GameFont.Small;

                // Calculate max job name width
                float maxJobNameWidth = Text.CalcSize("Job Type").x + 20f; // Header text plus padding
                for (int i = 0; i < workSpeedJobs.Count; i++)
                {
                    var jobName = !string.IsNullOrEmpty(workSpeedJobs[i].label) ? workSpeedJobs[i].label.CapitalizeFirst() : workSpeedJobs[i].defName;
                    float w = Text.CalcSize(jobName).x + 10f;
                    if (w > maxJobNameWidth) maxJobNameWidth = w;
                }

                // Calculate Gated column width (centered checkbox)
                float gatedColumnWidth = Mathf.Max(Text.CalcSize("Gated").x + 10f, 60f); // Minimum 60f for checkbox space

                Text.Font = measuringFont;

                // Job Type header
                var jobHeaderRect = new Rect(headerRect.x + 5f, headerRect.y, maxJobNameWidth, headerRect.height);
                GUI.color = Color.cyan;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(jobHeaderRect, "Job Type");

                // Gated header - centered
                var gatedHeaderRect = new Rect(jobHeaderRect.xMax + 10f, headerRect.y, gatedColumnWidth, headerRect.height);
                Widgets.Label(gatedHeaderRect, "Gated");

                // Description header
                var descHeaderRect = new Rect(gatedHeaderRect.xMax + 10f, headerRect.y, headerRect.width - gatedHeaderRect.xMax - 15f, headerRect.height);
                Widgets.Label(descHeaderRect, "Description");
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;

                // Draw vertical separators in header
                float headerSep1X = jobHeaderRect.xMax + 5f;
                float headerSep2X = gatedHeaderRect.xMax + 5f;
                GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
                GUI.DrawTexture(new Rect(headerSep1X - 0.5f, headerRect.y, 1f, headerRect.height), BaseContent.WhiteTex);
                GUI.DrawTexture(new Rect(headerSep2X - 0.5f, headerRect.y, 1f, headerRect.height), BaseContent.WhiteTex);
                GUI.color = prevColor;

                Text.Anchor = TextAnchor.UpperLeft;

                // Content area for scrollable job list (below pinned header, above buttons)
                var contentRect = new Rect(inRect.x, headerRect.yMax, inRect.width, inRect.height - headerRect.yMax - totalBottomSpace);

                if (workSpeedJobs.Count == 0)
                {
                    Widgets.Label(contentRect, "No WorkSpeedGlobal jobs found.");
                    return;
                }

                // Calculate content height for scrolling
                float contentHeight = workSpeedJobs.Count * 30f + 20f; // Extra padding
                var viewRect = new Rect(0, 0, contentRect.width - 20f, contentHeight);

                Widgets.BeginScrollView(contentRect, ref scrollPosition, viewRect);

                float curY = 0f;

                // Job entries (no header here since it's pinned above)
                for (int i = 0; i < workSpeedJobs.Count; i++)
                {
                    var job = workSpeedJobs[i];
                    var rowRect = new Rect(0, curY, viewRect.width, 25f);

                    // Alternate row colors
                    if (i % 2 == 0)
                    {
                        GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.3f);
                        Widgets.DrawBoxSolid(rowRect, GUI.color);
                        GUI.color = prevColor;
                    }

                    // Job name
                    var nameRect = new Rect(rowRect.x + 5f, rowRect.y, maxJobNameWidth, rowRect.height);
                    var jobName = !string.IsNullOrEmpty(job.label) ? job.label.CapitalizeFirst() : job.defName;
                    Widgets.Label(nameRect, jobName);

                    // Gated checkbox - centered in column
                    var checkColumnRect = new Rect(nameRect.xMax + 10f, rowRect.y, gatedColumnWidth, rowRect.height);
                    var checkRect = new Rect(checkColumnRect.x + (checkColumnRect.width - 20f) / 2f, rowRect.y + (rowRect.height - 20f) / 2f, 20f, 20f);
                    bool isGated = settings.workSpeedGlobalJobGating.GetValueOrDefault(job.defName, true);
                    bool newGated = isGated;
                    Widgets.Checkbox(checkRect.position, ref newGated);

                    if (newGated != isGated)
                    {
                        settings.workSpeedGlobalJobGating[job.defName] = newGated;
                        // Immediately persist changes so other windows can see the update
                        settings.Write();
                    }

                    // Description
                    var descriptionRect = new Rect(checkColumnRect.xMax + 10f, rowRect.y, rowRect.width - checkColumnRect.xMax - 15f, rowRect.height);
                    var description = GetJobDescription(job, newGated);
                    GUI.color = newGated ? Color.white : Color.gray;
                    Widgets.Label(descriptionRect, description);
                    GUI.color = prevColor;

                    // Draw vertical separators
                    float sep1X = nameRect.xMax + 5f; // After job name
                    float sep2X = checkColumnRect.xMax + 5f; // After checkbox column
                    GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.4f);
                    GUI.DrawTexture(new Rect(sep1X - 0.5f, rowRect.y, 1f, rowRect.height), BaseContent.WhiteTex);
                    GUI.DrawTexture(new Rect(sep2X - 0.5f, rowRect.y, 1f, rowRect.height), BaseContent.WhiteTex);
                    GUI.color = prevColor;

                    curY += 30f;
                }

                Widgets.EndScrollView();

                // Bottom buttons positioned relative to content area end
                var buttonY = contentRect.yMax + 10f;
                var buttonRect = new Rect(inRect.x, buttonY, 120f, 30f);

                if (Widgets.ButtonText(buttonRect, "Enable All"))
                {
                    for (int i = 0; i < workSpeedJobs.Count; i++)
                    {
                        settings.workSpeedGlobalJobGating[workSpeedJobs[i].defName] = true;
                    }
                    settings.Write();
                }

                buttonRect.x += 130f;
                if (Widgets.ButtonText(buttonRect, "Disable All"))
                {
                    for (int i = 0; i < workSpeedJobs.Count; i++)
                    {
                        settings.workSpeedGlobalJobGating[workSpeedJobs[i].defName] = false;
                    }
                    settings.Write();
                }

                buttonRect.x += 130f;
                if (Widgets.ButtonText(buttonRect, "Reset to Defaults"))
                {
                    for (int i = 0; i < workSpeedJobs.Count; i++)
                    {
                        var j = workSpeedJobs[i];
                        settings.workSpeedGlobalJobGating[j.defName] = GetDefaultGatingForJob(j);
                    }
                    settings.Write();
                }

                buttonRect.x += 150f;
                // Debug-only trace button
                if (Prefs.DevMode && Widgets.ButtonText(buttonRect, "Trace WorkGivers"))
                {
                    DumpWorkSpeedGlobalInfo();
                }
            }
            finally
            {
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
                GUI.color = prevColor;
            }
        }

        private string GetJobDescription(WorkGiverDef job, bool isGated)
        {
            if (isGated) return "Requires tools in hardcore modes";
            return "No tool penalties applied";
        }

        private bool GetDefaultGatingForJob(WorkGiverDef job)
        {
            // Default gating logic - gate production jobs, don't gate basic jobs
            string name = job.defName.ToLower();
            string label = !string.IsNullOrEmpty(job.label) ? job.label.ToLower() : "";

            // Don't gate basic survival activities including manipulation
            string[] noGateKeywords = new string[]
            {
                "haul", "carry", "deliver", "rescue", "tend", "feed", "clean", "extinguish",
                "manipulate", "handle", "grasp", "lift", "place", "arrange", "organize",
                // Manipulation and medical jobs that should never be gated
                "repair", "buildroofs", "deconstruct", "constructfinishframes",
                "constructdeliverresourcestoframes", "constructdeliverresourcestoblueprints",
                "handlingfeedpatientanimals", "cook"
            };

            for (int i = 0; i < noGateKeywords.Length; i++)
            {
                string k = noGateKeywords[i];
                if (name.Contains(k) || label.Contains(k)) return false;
            }

            // Gate production activities
            string[] gateKeywords = new string[]
            {
                "craft", "make", "smith", "tailor", "art", "sculpt", "bill", "produce"
            };
            for (int i = 0; i < gateKeywords.Length; i++)
            {
                string k = gateKeywords[i];
                if (name.Contains(k) || label.Contains(k)) return true;
            }

            // Default to gated for ambiguous cases
            return true;
        }

        // -------- Trace helpers --------

        private static IEnumerable<StatDef> ResolveRequiredStatsFor(WorkGiverDef job)
        {
            // We will try to ask WorkSpeedGlobalHelper for the stats it found when it
            // discovered these jobs. Different implementations may expose different method names.
            // We attempt a few common/likely signatures to keep this window decoupled.
            // If nothing is available, we fall back to WorkSpeedGlobal.

            Type helperType = typeof(WorkSpeedGlobalHelper);

            // Candidate method names returning IEnumerable<StatDef> for either WorkGiverDef or Type
            string[] names = new string[]
            {
                // one-parameter (WorkGiverDef) candidates
                "GetStatDefsFor", "GetStatsFor", "GetRequiredStatDefsFor", "GetRequiredStatsFor",
                "GetWorkSpeedStatDefsFor", "GetWorkSpeedStatsFor",
                // one-parameter (Type) candidates
                "GetStatDefsForType", "GetStatsForType", "GetRequiredStatDefsForType", "GetWorkSpeedStatDefsForType"
            };

            // 1) try WorkGiverDef parameter
            for (int i = 0; i < names.Length; i++)
            {
                MethodInfo m = helperType.GetMethod(
                    names[i],
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new Type[] { typeof(WorkGiverDef) },
                    null);

                if (m != null)
                {
                    try
                    {
                        object r = m.Invoke(null, new object[] { job });
                        IEnumerable<StatDef> stats = r as IEnumerable<StatDef>;
                        if (stats != null) return stats;
                    }
                    catch { }
                }
            }

            // 2) try Type (giverClass) parameter
            for (int i = 0; i < names.Length; i++)
            {
                MethodInfo m = helperType.GetMethod(
                    names[i],
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new Type[] { typeof(Type) },
                    null);

                if (m != null)
                {
                    try
                    {
                        object r = m.Invoke(null, new object[] { job.giverClass });
                        IEnumerable<StatDef> stats = r as IEnumerable<StatDef>;
                        if (stats != null) return stats;
                    }
                    catch { }
                }
            }

            // Fallback: the entire list was computed off WorkSpeedGlobal usage,
            // so at minimum we can report that stat explicitly.
            return new[] { StatDefOf.WorkSpeedGlobal };
        }

        private static string StatNames(IEnumerable<StatDef> stats)
        {
            if (stats == null) return "(unknown)";
            List<string> names = new List<string>();
            foreach (var s in stats)
            {
                if (s != null) names.Add(s.defName);
            }
            if (names.Count == 0) return "(unknown)";
            return string.Join(", ", names.ToArray());
        }

        private void DumpWorkSpeedGlobalInfo()
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string filePath = Path.Combine(desktop, "WorkSpeedGlobalJobsTrace.txt");

                using (var writer = new StreamWriter(filePath, false))
                {
                    writer.WriteLine("WorkSpeedGlobal Jobs Trace");
                    writer.WriteLine("==========================");
                    writer.WriteLine();

                    if (excludedJobs.Count > 0)
                    {
                        writer.WriteLine("Excluded Jobs (never shown):");
                        foreach (var ex in excludedJobs) writer.WriteLine("  - " + ex);
                        writer.WriteLine();
                    }

                    foreach (var job in workSpeedJobs)
                    {
                        bool gated = settings.workSpeedGlobalJobGating.GetValueOrDefault(job.defName, true);

                        writer.WriteLine("DefName: " + job.defName);
                        writer.WriteLine("Label: " + (job.label ?? "(no label)"));
                        writer.WriteLine("WorkType: " + (job.workType != null ? job.workType.defName : "(none)"));
                        writer.WriteLine("GiverClass: " + (job.giverClass != null ? job.giverClass.FullName : "(null)"));
                        writer.WriteLine("Currently Gated: " + gated);

                        // Optional: list recipe stats/skills for DoBill givers
                        if (typeof(WorkGiver_DoBill).IsAssignableFrom(job.giverClass))
                        {
                            var recipes = DefDatabase<RecipeDef>.AllDefsListForReading
                                .Where(r => r.workSpeedStat != null) // only those affected by speed
                                .Select(r => $"{r.defName} (Stat={r.workSpeedStat.defName}, Skill={(r.workSkill != null ? r.workSkill.defName : "none")})")
                                .ToList();

                            if (recipes.Count > 0)
                            {
                                writer.WriteLine("Recipes (with workSpeedStat/workSkill):");
                                foreach (var rec in recipes)
                                    writer.WriteLine("  - " + rec);
                            }
                        }

                        writer.WriteLine(new string('-', 40));
                    }

                }

                Messages.Message("WorkSpeedGlobal job trace written to desktop.", MessageTypeDefOf.TaskCompletion, false);
            }
            catch (Exception ex)
            {
                Log.Error("[SurvivalTools] Failed to dump WorkSpeedGlobal info: " + ex);
            }
        }
    }
}
