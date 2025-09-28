// RimWorld 1.6 / C# 7.3
// Source/UI/RightClickRescue/Provider_STPrioritizeWithRescue.cs
// Public float menu option provider that injects Survival Tools rescue ("will fetch tool") prioritized options.
// Acts before fallback Harmony patch; scanners/builder contain the gating logic.

using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SurvivalTools.UI.RightClickRescue
{
    // IMPORTANT: public + non-abstract so RimWorld can construct via reflection.
    public sealed class Provider_STPrioritizeWithRescue : FloatMenuOptionProvider
    {
        // Offer in both drafted & undrafted states; allow single-select only (rescue builder ignores multiselect currently)
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        static Provider_STPrioritizeWithRescue()
        {
            if (Prefs.DevMode) Log.Message("[ST.RightClick] Provider type loaded (static cctor).");
        }

        public Provider_STPrioritizeWithRescue()
        {
            if (Prefs.DevMode) Log.Message("[ST.RightClick] Provider instance created (Activator OK).");
        }

        public override bool Applies(FloatMenuContext context)
        {
            if (context == null) return false;
            var s = SurvivalToolsMod.Settings;
            if (s == null) return false;
            // Only participate when feature enabled and in Hardcore / Nightmare (extraHardcore implies hardcore semantics)
            if (!s.enableRightClickRescue) return false;
            if (!(s.hardcoreMode || s.extraHardcoreMode)) return false;
            return context.FirstSelectedPawn != null;
        }

        public override bool SelectedPawnValid(Pawn p, FloatMenuContext context)
        {
            return p != null && p.Spawned && p.Faction == Faction.OfPlayer && p.CanTakeOrder;
        }

        private static int _lastProviderTick = -1;
        public override IEnumerable<FloatMenuOption> GetOptions(FloatMenuContext context)
        {
            var list = new List<FloatMenuOption>();
            try
            {
                var pawn = context?.FirstSelectedPawn;
                if (pawn != null)
                {
                    RightClickRescue.RightClickRescueBuilder.TryAddRescueOptions(pawn, context, list);
                    if (Prefs.DevMode && list.Count > 0 && _lastProviderTick != GenTicks.TicksGame)
                    {
                        _lastProviderTick = GenTicks.TicksGame;
                        Log.Message($"[ST.RightClick] Provider added {list.Count} rescue option(s) at {context.ClickedCell}.");
                    }
                }
            }
            catch { }
            return list;
        }

        public override bool TargetThingValid(Thing thing, FloatMenuContext context) => false; // context-wide only
        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing target, FloatMenuContext context) { yield break; }
        public override bool TargetPawnValid(Pawn target, FloatMenuContext context) => false;
        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn target, FloatMenuContext context) { yield break; }
    }
}
