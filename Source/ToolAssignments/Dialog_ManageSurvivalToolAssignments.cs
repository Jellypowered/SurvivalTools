using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace SurvivalTools
{
    public class Dialog_ManageSurvivalToolAssignments : Window
    {
        private ThingFilterUI.UIState filterUiState = new ThingFilterUI.UIState();
        private static ThingFilter survivalToolGlobalFilter;
        private SurvivalToolAssignment selSurvivalToolAssignmentInt;

        private string nameBuffer = string.Empty;

        public const float TopAreaHeight = 40f;
        public const float TopButtonHeight = 35f;
        public const float TopButtonWidth = 150f;

        public Dialog_ManageSurvivalToolAssignments(SurvivalToolAssignment selectedToolAssignment)
        {
            forcePause = true;
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;

            if (survivalToolGlobalFilter == null)
            {
                survivalToolGlobalFilter = new ThingFilter();
                survivalToolGlobalFilter.SetAllow(ST_ThingCategoryDefOf.SurvivalTools, true);
            }

            SelectedSurvivalToolAssignment = selectedToolAssignment;
        }

        private SurvivalToolAssignment SelectedSurvivalToolAssignment
        {
            get => selSurvivalToolAssignmentInt;
            set
            {
                selSurvivalToolAssignmentInt = value;
                nameBuffer = value?.label ?? string.Empty;
                EnsureSelectedHasName();
            }
        }

        private void EnsureSelectedHasName()
        {
            if (selSurvivalToolAssignmentInt != null && selSurvivalToolAssignmentInt.label.NullOrEmpty())
                selSurvivalToolAssignmentInt.label = "Unnamed";
        }

        public override Vector2 InitialSize => new Vector2(700f, 700f);

        public override void DoWindowContents(Rect inRect)
        {
            float x = 0f;

            var rectSelect = new Rect(x, 0f, TopButtonWidth, TopButtonHeight);
            x += TopButtonWidth + 10f;
            if (Widgets.ButtonText(rectSelect, "SelectSurvivalToolAssignment".Translate()))
            {
                var db = Current.Game.GetComponent<SurvivalToolAssignmentDatabase>();
                var opts = new List<FloatMenuOption>();
                foreach (var entry in db.AllSurvivalToolAssignments)
                {
                    var local = entry;
                    opts.Add(new FloatMenuOption(local.label, () => SelectedSurvivalToolAssignment = local));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            var rectNew = new Rect(x, 0f, TopButtonWidth, TopButtonHeight);
            x += TopButtonWidth + 10f;
            if (Widgets.ButtonText(rectNew, "NewSurvivalToolAssignment".Translate(), active: true, doMouseoverSound: false, drawBackground: true))
            {
                var db = Current.Game.GetComponent<SurvivalToolAssignmentDatabase>();
                SelectedSurvivalToolAssignment = db.MakeNewSurvivalToolAssignment();
            }

            var rectDelete = new Rect(x, 0f, TopButtonWidth, TopButtonHeight);
            if (Widgets.ButtonText(rectDelete, "DeleteSurvivalToolAssignment".Translate(), active: true, doMouseoverSound: false, drawBackground: true))
            {
                var db = Current.Game.GetComponent<SurvivalToolAssignmentDatabase>();
                var opts = new List<FloatMenuOption>();
                foreach (var entry in db.AllSurvivalToolAssignments)
                {
                    var local = entry;
                    opts.Add(new FloatMenuOption(local.label, () =>
                    {
                        var rep = db.TryDelete(local);
                        if (!rep.Accepted)
                        {
                            Messages.Message(rep.Reason, MessageTypeDefOf.RejectInput, historical: false);
                        }
                        else if (local == SelectedSurvivalToolAssignment)
                        {
                            SelectedSurvivalToolAssignment = null;
                        }
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            var mainRect = new Rect(0f, TopAreaHeight, inRect.width, inRect.height - TopAreaHeight - CloseButSize.y).ContractedBy(10f);

            if (SelectedSurvivalToolAssignment == null)
            {
                GUI.color = Color.grey;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(mainRect, "NoSurvivalToolAssignmentSelected".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            GUI.BeginGroup(mainRect);

            // Name field
            var nameRect = new Rect(0f, 0f, 300f, 30f);
            Widgets.Label(new Rect(nameRect.x, nameRect.y, 70f, nameRect.height), "Name".Translate() + ":");
            var nameEditRect = new Rect(nameRect.x + 75f, nameRect.y, nameRect.width - 75f, nameRect.height);
            nameBuffer = Widgets.TextField(nameEditRect, nameBuffer ?? string.Empty, 64);
            SelectedSurvivalToolAssignment.label = nameBuffer.NullOrEmpty() ? "Unnamed" : nameBuffer;

            // Filter config
            var filterRect = new Rect(0f, TopAreaHeight, 300f, mainRect.height - 45f - 10f);
            var filter = SelectedSurvivalToolAssignment.filter;
            var parentFilter = survivalToolGlobalFilter;
            const int openMask = 16;

            // Use UIState rather than Vector2
            ThingFilterUI.DoThingFilterConfigWindow(filterRect, filterUiState, filter, parentFilter, openMask);

            GUI.EndGroup();
        }

        public override void PreClose()
        {
            base.PreClose();
            EnsureSelectedHasName();
        }
    }
}
