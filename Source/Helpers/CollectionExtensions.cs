// RimWorld 1.6 / C# 7.3
// Source/Helpers/CollectionExtensions.cs
// LEGACY CODE: KEEP.
//
// Centralized extension methods for collections and common data patterns.
// These reduce boilerplate across SurvivalTools and keep code maintainable.
// Includes helpers for StatModifier lookups, List/HashSet/Dictionary utilities,
// and string/sequence filters.

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Helpers
{
    public static class CollectionExtensions
    {
        // Deduplication guard for stat lookup debug logs
        private static readonly HashSet<string> loggedStatLookupKeys = new HashSet<string>();

        #region StatModifier Collections

        /// <summary>
        /// Get the stat factor from a list of StatModifiers, returning 1.0 if not found.
        /// Includes optional cooldowned debug logging to help trace mismatches.
        /// </summary>
        public static float GetStatFactorFromList(this IEnumerable<StatModifier> modifiers, StatDef stat)
        {
            if (modifiers == null || stat == null) return 1f;
            float val = 1f;
            foreach (var m in modifiers)
            {
                if (m?.stat == stat) { val = m.value; break; }
            }
            return val;
        }

        #endregion

        #region HashSet Extensions

        /// <summary>Check if a HashSet overlaps with another collection.</summary>
        public static bool Overlaps<T>(this HashSet<T> set, IEnumerable<T> other)
        {
            return other != null && other.Any(set.Contains);
        }

        // Phase 11.13: Removed unused HashSet extension:
        // - AddRange<T>() - zero call sites (all AddRange uses were List<T>.AddRange, not our extension)

        #endregion

        #region Filtering Utilities

        // Phase 11.13: Removed unused filtering extension methods:
        // - OfTypeNotNull<T>() - zero call sites
        // - WhereIf<T>() - zero call sites  
        // - TakeWhileInclusive<T>() - zero call sites
        // All were speculative helpers never actually used.

        #endregion
    }
}
