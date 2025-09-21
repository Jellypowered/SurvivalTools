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

        /// <summary>Check if modifiers contain an entry for a specific stat.</summary>
        public static bool HasModifierFor(this IEnumerable<StatModifier> modifiers, StatDef stat)
        {
            return modifiers?.Any(m => m.stat == stat) == true;
        }

        /// <summary>Get all unique StatDefs that are modified by this collection.</summary>
        public static IEnumerable<StatDef> GetModifiedStats(this IEnumerable<StatModifier> modifiers)
        {
            return modifiers?.Where(m => m.stat != null).Select(m => m.stat).Distinct() ?? Enumerable.Empty<StatDef>();
        }

        /// <summary>
        /// Filter modifiers to only include improvements (factor above no-tool baseline).
        /// Penalties and neutral factors are excluded.
        /// </summary>
        public static IEnumerable<StatModifier> OnlyImprovements(this IEnumerable<StatModifier> modifiers)
        {
            return modifiers?.Where(m => m.value > GetNoToolBaseline(m.stat) + 0.001f) ?? Enumerable.Empty<StatModifier>();
        }

        /// <summary>
        /// Baseline factor for a stat when no tools are equipped.
        /// Reads from StatPart_SurvivalTool if present.
        /// </summary>
        private static float GetNoToolBaseline(StatDef stat) => SurvivalToolUtility.GetNoToolBaseline(stat);

        #endregion

        #region HashSet Extensions

        /// <summary>Check if a HashSet overlaps with another collection.</summary>
        public static bool Overlaps<T>(this HashSet<T> set, IEnumerable<T> other)
        {
            return other != null && other.Any(set.Contains);
        }

        /// <summary>Add multiple items to a HashSet at once.</summary>
        public static void AddRange<T>(this HashSet<T> set, IEnumerable<T> items)
        {
            if (set == null || items == null) return;
            foreach (var item in items) set.Add(item);
        }

        #endregion

        #region List Utilities

        /// <summary>Get a random element from a list, or default if empty.</summary>
        public static T GetRandomOrDefault<T>(this IList<T> list)
        {
            if (list == null || list.Count == 0) return default(T);
            return list[Rand.Range(0, list.Count)];
        }

        /// <summary>Remove all null entries from a list in place.</summary>
        public static void RemoveNulls<T>(this IList<T> list) where T : class
        {
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null) list.RemoveAt(i);
            }
        }

        /// <summary>Check if a collection is null or empty.</summary>
        public static bool IsNullOrEmpty<T>(this ICollection<T> collection)
        {
            return collection == null || collection.Count == 0;
        }

        /// <summary>
        /// Get the maximum element by selector, or default if empty.
        /// Safe for nullable selector results.
        /// </summary>
        public static T MaxByOrDefault<T, TKey>(this IEnumerable<T> source, Func<T, TKey> selector)
            where TKey : IComparable<TKey>
        {
            if (source == null) return default(T);

            var maxItem = default(T);
            var maxKey = default(TKey);
            bool hasValue = false;

            foreach (var item in source)
            {
                var key = selector(item);
                if (!hasValue || (key != null && key.CompareTo(maxKey) > 0))
                {
                    maxItem = item;
                    maxKey = key;
                    hasValue = true;
                }
            }

            return maxItem;
        }

        #endregion

        #region Dictionary Utilities

        /// <summary>
        /// Get a value from dictionary or add+return a generated default.
        /// </summary>
        public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, Func<TValue> valueFactory)
        {
            if (dict == null || valueFactory == null) return default(TValue);
            if (!dict.TryGetValue(key, out var value))
            {
                value = valueFactory();
                dict[key] = value;
            }
            return value;
        }

        /// <summary>Increment a counter in a dictionary, creating it if absent.</summary>
        public static void IncrementCount<TKey>(this Dictionary<TKey, int> dict, TKey key, int increment = 1)
        {
            if (dict == null) return;
            if (dict.TryGetValue(key, out var count)) dict[key] = count + increment;
            else dict[key] = increment;
        }

        #endregion

        #region String Utilities

        /// <summary>Join non-null, non-empty strings with a separator.</summary>
        public static string JoinNonEmpty(this IEnumerable<string> strings, string separator = ", ")
        {
            if (strings == null) return string.Empty;
            return string.Join(separator, strings.Where(s => !string.IsNullOrEmpty(s)));
        }

        /// <summary>Truncate a string with ellipsis if it exceeds maxLength.</summary>
        public static string TruncateWithEllipsis(this string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str) || maxLength <= 0) return string.Empty;
            if (str.Length <= maxLength) return str;

            const string ellipsis = "...";
            if (maxLength <= ellipsis.Length) return ellipsis.Substring(0, maxLength);

            return str.Substring(0, maxLength - ellipsis.Length) + ellipsis;
        }

        #endregion

        #region Filtering Utilities

        /// <summary>Filter sequence to only items of T (non-null).</summary>
        public static IEnumerable<T> OfTypeNotNull<T>(this IEnumerable<object> source) where T : class
        {
            return source?.OfType<T>().Where(item => item != null) ?? Enumerable.Empty<T>();
        }

        /// <summary>Apply a filter predicate only if condition is true.</summary>
        public static IEnumerable<T> WhereIf<T>(this IEnumerable<T> source, bool condition, Func<T, bool> predicate)
        {
            return condition ? source?.Where(predicate) ?? Enumerable.Empty<T>() : source ?? Enumerable.Empty<T>();
        }

        /// <summary>
        /// Take items while predicate holds, but include the first failing item.
        /// Useful for inclusive ranges.
        /// </summary>
        public static IEnumerable<T> TakeWhileInclusive<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            if (source == null || predicate == null) yield break;
            foreach (var item in source)
            {
                yield return item;
                if (!predicate(item)) yield break;
            }
        }

        #endregion
    }
}
