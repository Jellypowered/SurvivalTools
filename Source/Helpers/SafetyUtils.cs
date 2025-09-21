// RimWorld 1.6 / C# 7.3
// Source/Helpers/SafetyUtils.cs
// TODO: Evaluate if these need to be integrated into our refactored codebase or if they are redundant.
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using static SurvivalTools.ST_Logging;

namespace SurvivalTools.Helpers
{
    /// <summary>
    /// Utility class providing safe operations and null-safe extensions
    /// to reduce crashes and improve error handling.
    /// 
    /// Future idea:
    /// - Add overloads with context-sensitive logging (e.g. debug vs release).
    /// - Add async-safe versions if ever integrating with tasks/threads.
    /// - Optionally collect/report recurring exceptions instead of spamming the log.
    /// </summary>
    public static class SafetyUtils
    {
        // ---------------------------
        // Collection helpers
        // ---------------------------

        /// <summary>
        /// Safely gets the count of a collection, returning 0 if null.
        /// </summary>
        public static int SafeCount<T>(this ICollection<T> collection)
        {
            return collection?.Count ?? 0;
        }

        /// <summary>
        /// Safely gets the count of an enumerable, returning 0 if null.
        /// </summary>
        public static int SafeCount<T>(this IEnumerable<T> enumerable)
        {
            return enumerable == null ? 0 : enumerable.Count();
        }

        /// <summary>
        /// Safely gets the first element or default value.
        /// </summary>
        public static T SafeFirstOrDefault<T>(this IEnumerable<T> enumerable)
        {
            return enumerable == null ? default(T) : enumerable.FirstOrDefault();
        }

        /// <summary>
        /// Safely gets the first element matching predicate or default value.
        /// </summary>
        public static T SafeFirstOrDefault<T>(this IEnumerable<T> enumerable, Func<T, bool> predicate)
        {
            return (enumerable == null || predicate == null) ? default(T) : enumerable.FirstOrDefault(predicate);
        }

        /// <summary>
        /// Safely accesses an element by index, returning default if null or out of bounds.
        /// </summary>
        public static T SafeElementAt<T>(this IList<T> list, int index)
        {
            return (list == null || index < 0 || index >= list.Count) ? default(T) : list[index];
        }

        /// <summary>
        /// Safely checks if a collection has any elements.
        /// </summary>
        public static bool SafeAny<T>(this IEnumerable<T> enumerable)
        {
            return enumerable != null && enumerable.Any();
        }

        /// <summary>
        /// Safely checks if a collection has any elements matching predicate.
        /// </summary>
        public static bool SafeAny<T>(this IEnumerable<T> enumerable, Func<T, bool> predicate)
        {
            return enumerable != null && predicate != null && enumerable.Any(predicate);
        }

        // ---------------------------
        // Execution helpers
        // ---------------------------

        /// <summary>
        /// Safely executes an action with exception handling and logging.
        /// </summary>
        public static void SafeExecute(Action action, string context = "SafeExecute")
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                LogError($"[SurvivalTools] Exception in {context}: {ex}");
            }
        }

        /// <summary>
        /// Safely executes a function with exception handling and logging, returning default on error.
        /// </summary>
        public static T SafeExecute<T>(Func<T> func, T defaultValue = default(T), string context = "SafeExecute")
        {
            try
            {
                return func != null ? func() : defaultValue;
            }
            catch (Exception ex)
            {
                LogError($"[SurvivalTools] Exception in {context}: {ex}");
                return defaultValue;
            }
        }

        // ---------------------------
        // Def / Pawn / Thing helpers
        // ---------------------------

        /// <summary>
        /// Safely gets a ThingDef by name, with fallback and logging if not found.
        /// </summary>
        public static ThingDef SafeGetThingDef(string defName, ThingDef fallback = null)
        {
            if (string.IsNullOrEmpty(defName)) return fallback;

            var def = DefDatabase<ThingDef>.GetNamed(defName, errorOnFail: false);
            if (def == null)
                LogWarning($"[SurvivalTools] Could not find ThingDef '{defName}'");

            return def ?? fallback;
        }

        /// <summary>
        /// Validates that a pawn is not null, not dead, and not destroyed.
        /// </summary>
        public static bool IsValidPawn(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Destroyed;
        }

        /// <summary>
        /// Validates that a thing is not null and not destroyed.
        /// </summary>
        public static bool IsValidThing(Thing thing)
        {
            return thing != null && !thing.Destroyed;
        }

        // ---------------------------
        // Mod extension helpers
        // ---------------------------

        /// <summary>
        /// Safely gets a mod extension from a def, with null checking.
        /// </summary>
        public static T SafeGetModExtension<T>(this Def def) where T : DefModExtension
        {
            return def?.GetModExtension<T>();
        }

        /// <summary>
        /// Safely tries to get a mod extension, returning whether it was found.
        /// </summary>
        public static bool TrySafeGetModExtension<T>(this Def def, out T extension) where T : DefModExtension
        {
            extension = def?.GetModExtension<T>();
            return extension != null;
        }
    }
}
