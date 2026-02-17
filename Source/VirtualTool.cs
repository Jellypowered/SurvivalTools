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
            // CRITICAL FIX: Initialize the base class's _workStatFactors field to prevent
            // lazy initialization when accessed via SurvivalTool reference. The base class
            // property is not virtual, so when code accesses ((SurvivalTool)this).WorkStatFactors,
            // it calls the base getter which checks for null and triggers InitializeWorkStatFactors().
            // During game load this can access uninitialized state and crash.
            try
            {
                var baseField = typeof(SurvivalTool).GetField("_workStatFactors",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (baseField != null)
                    baseField.SetValue(this, _workStatFactors);
            }
            catch
            {
                // Fail silently - the VirtualTool's own property will still work correctly
            }
        }

        /// <summary>
        /// Factory method that wraps a physical Thing (like cloth or hyperweave) into a VirtualSurvivalTool.
        /// Returns null if the Thing's def does not declare SurvivalToolProperties.
        /// Defensive: returns null on any exception during construction to prevent CTD during enumeration.
        /// </summary>
        public static VirtualTool FromThing(Thing thing)
        {
            if (thing?.def == null) return null;
            try
            {
                if (!EligibleTextile(thing.def)) return null;
                return new VirtualTool(thing);
            }
            catch
            {
                // Fail gracefully during construction - better to skip this item than crash
                return null;
            }
        }

        #endregion

        #region Labels

        public override string LabelNoCount => SourceDef?.label?.CapitalizeFirst() ?? base.LabelNoCount;

        public override string LabelCap => LabelNoCount.CapitalizeFirst();

        #endregion

        #region Helpers

        // Phase 8: tighten to textiles only (fabric/wool). Exclude wood, apparel, weapons.
        private static bool EligibleTextile(ThingDef def)
        {
            if (def == null) return false;
            if (def.IsApparel || def.IsWeapon) return false;
            // Must be a stuff item whose stuff categories overlap Fabric or its textile tags
            if (!def.stuffProps?.categories.NullOrEmpty() == true)
            {
                bool textile = false;
                var cats = def.stuffProps.categories;
                for (int i = 0; i < cats.Count; i++)
                {
                    var c = cats[i];
                    if (c?.defName != null && (c.defName.Contains("Fabric") || c.defName.Contains("Textile")))
                    {
                        textile = true; break;
                    }
                }
                if (!textile) return false;
            }
            else return false;
            // Exclude wood (common defNames) explicitly
            var dn = def.defName.ToLowerInvariant();
            if (dn == "woodlog" || dn.Contains("wood")) return false;
            // Must declare SurvivalToolProperties with cleaning stat to be useful as virtual tool
            var ext = def.GetModExtension<SurvivalToolProperties>();
            if (ext?.baseWorkStatFactors == null) return false;
            bool hasCleaning = false;
            for (int i = 0; i < ext.baseWorkStatFactors.Count; i++)
            {
                var sm = ext.baseWorkStatFactors[i];
                if (sm?.stat == ST_StatDefOf.CleaningSpeed)
                {
                    hasCleaning = true; break;
                }
            }
            return hasCleaning;
        }

        #endregion
    }
}
