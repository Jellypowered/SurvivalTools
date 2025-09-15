// RimWorld 1.6 / C# 7.3
// Source/VirtualTool.cs
//
// VirtualTool
// - Wrapper so tool-stuff (cloth, hyperweave, etc.) or other qualifying Things behave like SurvivalTool at runtime.
// - Exposes work stat modifiers via SurvivalToolProperties / statBases through the SurvivalTool base caches.
// - Not saved separately; ephemeral adapter used in scoring/assignment paths.

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SurvivalTools
{
    /// <summary>
    /// A lightweight wrapper so tool-stuff or other resource Things can act like SurvivalTools transparently.
    /// Wraps a physical Thing (stack) and exposes tool stat modifiers from its def (extension + statBases).
    /// </summary>
    public class VirtualTool : SurvivalTool
    {
        #region Fields & Properties

        /// <summary>Physical thing (stack) that this wrapper represents.</summary>
        public Thing SourceThing { get; }

        /// <summary>Definition of the wrapped thing.</summary>
        public ThingDef SourceDef { get; }

        /// <summary>Precomputed stat factors pulled from SurvivalToolProperties on the source def.</summary>
        private readonly List<StatModifier> _workStatFactors;

        /// <summary>
        /// Hide the base property since it's not virtual. For virtual tools this returns only
        /// non-null, non-zero stat modifiers defined in SurvivalToolProperties.baseWorkStatFactors.
        /// </summary>
        public new IEnumerable<StatModifier> WorkStatFactors => _workStatFactors;

        #endregion

        #region Construction & Factory

        private VirtualTool(Thing sourceThing)
        {
            SourceThing = sourceThing;
            SourceDef = sourceThing?.def;

            var props = SourceDef?.GetModExtension<SurvivalToolProperties>();
            _workStatFactors = props?.baseWorkStatFactors?
                .Where(sm => sm?.stat != null && sm.value != 0f)
                .ToList()
                ?? new List<StatModifier>();

            // Present as the same def for labeling/inspection consistency.
            if (SourceDef != null)
                def = SourceDef;
        }

        /// <summary>
        /// Factory method that wraps a physical Thing (like cloth or hyperweave) into a VirtualSurvivalTool.
        /// Returns null if the Thing's def does not declare SurvivalToolProperties.
        /// </summary>
        public static VirtualTool FromThing(Thing thing)
        {
            if (thing?.def == null) return null;
            if (!LooksLikeToolStuff(thing.def)) return null;
            return new VirtualTool(thing);
        }

        #endregion

        #region Labels

        public override string LabelNoCount => SourceDef?.label?.CapitalizeFirst() ?? base.LabelNoCount;

        public override string LabelCap => LabelNoCount.CapitalizeFirst();

        #endregion

        #region Helpers

        private static bool LooksLikeToolStuff(ThingDef def)
            => def?.GetModExtension<SurvivalToolProperties>() != null;

        #endregion
    }
}
