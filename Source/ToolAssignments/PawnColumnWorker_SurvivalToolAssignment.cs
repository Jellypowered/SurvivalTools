using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace SurvivalTools
{
    public sealed class PawnColumnWorker_SurvivalToolAssignment : PawnColumnWorker
    {
        public const float TopAreaHeight = 40f;
        public const float TopButtonHeight = 35f;
        public const float TopButtonWidth = 150f;

        public override void DoHeader(Rect rect, PawnTable table)
        {
            base.DoHeader(rect, table);

            var manageRect = new Rect(rect.x, rect.y + (rect.height - 65f), Mathf.Min(rect.width, 360f), 32f);
            if (Widgets.ButtonText(manageRect, "ManageSurvivalToolAssignments".Translate()))
            {
                Find.WindowStack.Add(new Dialog_ManageSurvivalToolAssignments(null));
            }
        }

        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            var tracker = pawn?.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
            if (tracker == null)
                return;

            bool somethingIsForced = tracker.forcedHandler != null && tracker.forcedHandler.SomethingForced;

            int leftWidth = Mathf.FloorToInt((rect.width - 4f) * 0.71428573f);
            int rightWidth = Mathf.FloorToInt((rect.width - 4f) * 0.2857143f);

            float curX = rect.x;

            // Dropdown: choose assignment
            var ddRect = new Rect(curX, rect.y + 2f, leftWidth, rect.height - 4f);
            if (somethingIsForced) ddRect.width -= 4f + rightWidth;

            var buttonLabel = tracker.CurrentSurvivalToolAssignment?.label ?? "Unnamed";
            buttonLabel = buttonLabel.Truncate(ddRect.width);

            // NOTE: use positional args (like vanilla) to match your overload
            Widgets.Dropdown(
                ddRect,
                pawn,
                p => p.TryGetComp<Pawn_SurvivalToolAssignmentTracker>()?.CurrentSurvivalToolAssignment,
                Button_GenerateMenu,
                buttonLabel,
                null,
                tracker.CurrentSurvivalToolAssignment?.label,
                null,
                null,
                true
            );

            curX += ddRect.width + 4f;

            // Clear forced tools (if any)
            if (somethingIsForced)
            {
                var clearRect = new Rect(curX, rect.y + 2f, rightWidth, rect.height - 4f);
                if (Widgets.ButtonText(clearRect, "ClearForcedApparel".Translate()))
                {
                    tracker.forcedHandler.Reset();
                }

                TooltipHandler.TipRegion(clearRect, new TipSignal(() =>
                {
                    var text = "ForcedSurvivalTools".Translate() + ":\n";
                    if (tracker.forcedHandler != null)
                    {
                        foreach (var tool in tracker.forcedHandler.ForcedTools)
                        {
                            text += "\n   " + tool.LabelCap;
                        }
                    }
                    return text;
                }, pawn.GetHashCode() * 128));

                curX += rightWidth + 4f;
            }

            // Edit current assignment
            var editRect = new Rect(curX, rect.y + 2f, rightWidth, rect.height - 4f);
            if (Widgets.ButtonText(editRect, "AssignTabEdit".Translate()))
            {
                Find.WindowStack.Add(new Dialog_ManageSurvivalToolAssignments(tracker.CurrentSurvivalToolAssignment));
            }
        }

        private IEnumerable<Widgets.DropdownMenuElement<SurvivalToolAssignment>> Button_GenerateMenu(Pawn pawn)
        {
            var db = Current.Game?.GetComponent<SurvivalToolAssignmentDatabase>();
            if (db == null)
                yield break;

            foreach (var survivalToolAssignment in db.AllSurvivalToolAssignments)
            {
                var local = survivalToolAssignment;
                yield return new Widgets.DropdownMenuElement<SurvivalToolAssignment>
                {
                    option = new FloatMenuOption(local.label, () =>
                    {
                        var tracker = pawn.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
                        if (tracker != null)
                            tracker.CurrentSurvivalToolAssignment = local;
                    }),
                    payload = local
                };
            }
        }

        public override int GetMinWidth(PawnTable table) =>
            Mathf.Max(base.GetMinWidth(table), Mathf.CeilToInt(194f));

        public override int GetOptimalWidth(PawnTable table) =>
            Mathf.Clamp(Mathf.CeilToInt(251f), GetMinWidth(table), GetMaxWidth(table));

        public override int GetMinHeaderHeight(PawnTable table) =>
            Mathf.Max(base.GetMinHeaderHeight(table), PawnColumnWorker_Outfit.TopAreaHeight);

        public override int Compare(Pawn a, Pawn b) =>
            GetValueToCompare(a).CompareTo(GetValueToCompare(b));

        private int GetValueToCompare(Pawn pawn)
        {
            var tracker = pawn?.TryGetComp<Pawn_SurvivalToolAssignmentTracker>();
            return (tracker?.CurrentSurvivalToolAssignment != null)
                ? tracker.CurrentSurvivalToolAssignment.uniqueId
                : int.MinValue;
        }
    }
}
