// RimWorld 1.6 / C# 7.3
// Source/VirtualTool.cs
//
// VirtualSurvivalTool
// - Lightweight wrapper so tool-stuffs (cloth, wool, hyperweave, etc.) can act like SurvivalTools.
// - Wraps a physical Thing (stack) and exposes work stat modifiers via SurvivalToolProperties.
// - Not spawned / not persisted; used transiently by AutoTool logic.

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SurvivalTools
{
    /// <summary>
    /// A lightweight wrapper so tool-stuffs (cloth, wool, hyperweave, etc.) can act like SurvivalTools.
    /// Wraps a physical Thing (stack) and exposes the tool stat modifiers from its def.
    /// </summary>
    public class VirtualSurvivalTool : SurvivalTool
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

        private VirtualSurvivalTool(Thing sourceThing)
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
        public static VirtualSurvivalTool FromThing(Thing thing)
        {
            if (thing?.def == null) return null;
            if (!LooksLikeToolStuff(thing.def)) return null;
            return new VirtualSurvivalTool(thing);
        }

        #endregion

        #region Labels

        public override string LabelNoCount =>
            $"[Virtual] {SourceDef?.label?.CapitalizeFirst() ?? base.LabelNoCount}";

        public override string LabelCap => LabelNoCount.CapitalizeFirst();

        #endregion

        #region Helpers

        private static bool LooksLikeToolStuff(ThingDef def)
            => def?.GetModExtension<SurvivalToolProperties>() != null;

        #endregion
    }
}
