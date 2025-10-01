// RimWorld 1.6 / C# 7.3
// Source/Helpers/ScoreCache.cs
// Refactor Code Phase 3: KEEP
// Phase 3: Fast tool scoring cache with struct keys and invalidation hooks.
// Provides deterministic, zero-allocation caching for ToolScoring APIs.

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SurvivalTools.Helpers
{
    /// <summary>
    /// Phase 3: Fast tool scoring cache with struct keys and automatic invalidation.
    /// Uses IEquatable struct keys for zero string allocations and efficient lookups.
    /// </summary>
    internal static class ScoreCache
    {
        // Fast struct-based cache key (no string concatenation)
        private struct CacheKey : IEquatable<CacheKey>
        {
            public readonly int PawnId;
            public readonly int ToolId;
            public readonly int StatId;
            public readonly int DifficultySeed;
            public readonly int ResolverVersion;
            public readonly int ChargeBucket; // Phase 12: 0-20 for powered tools, -1 for unpowered

            public CacheKey(Pawn pawn, Thing tool, StatDef stat, int difficultySeed, int resolverVersion)
            {
                PawnId = pawn?.thingIDNumber ?? 0;
                ToolId = tool?.thingIDNumber ?? 0;
                StatId = stat?.index ?? 0;
                DifficultySeed = difficultySeed;
                ResolverVersion = resolverVersion;

                // Phase 12: Include charge bucket in cache key
                var powerComp = tool?.TryGetComp<CompPowerTool>();
                ChargeBucket = powerComp?.GetChargeBucket5() ?? -1;
            }

            public bool Equals(CacheKey other)
            {
                return PawnId == other.PawnId &&
                       ToolId == other.ToolId &&
                       StatId == other.StatId &&
                       DifficultySeed == other.DifficultySeed &&
                       ResolverVersion == other.ResolverVersion &&
                       ChargeBucket == other.ChargeBucket; // Phase 12
            }

            public override bool Equals(object obj)
            {
                return obj is CacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = PawnId;
                    hash = (hash * 397) ^ ToolId;
                    hash = (hash * 397) ^ StatId;
                    hash = (hash * 397) ^ DifficultySeed;
                    hash = (hash * 397) ^ ResolverVersion;
                    hash = (hash * 397) ^ ChargeBucket; // Phase 12
                    return hash;
                }
            }
        }

        // Pooled value holder to avoid boxing floats
        private struct CacheValue
        {
            public readonly float Score;
            public readonly int LastAccess;

            public CacheValue(float score, int lastAccess)
            {
                Score = score;
                LastAccess = lastAccess;
            }
        }

        // Main cache with struct keys for performance
        private static readonly Dictionary<CacheKey, CacheValue> _scoreCache = new Dictionary<CacheKey, CacheValue>();

        // Cache statistics
        private static int _cacheHits = 0;
        private static int _cacheMisses = 0;
        private static int _accessCounter = 0;
        private static int _lastCleanupAccess = 0;

        // Current resolver version for automatic invalidation
        private static int _cachedResolverVersion = -1;

        /// <summary>
        /// Try to get cached score for tool/pawn/stat combination.
        /// Uses struct keys for zero allocation lookups.
        /// </summary>
        public static bool TryGet(Pawn pawn, Thing tool, StatDef stat, out float score)
        {
            score = 0f;
            ++_accessCounter;

            // Auto-invalidate if resolver version changed
            int currentVersion = ToolStatResolver.Version;
            if (_cachedResolverVersion != currentVersion)
            {
                _scoreCache.Clear();
                _cachedResolverVersion = currentVersion;
                ++_cacheMisses;
                return false;
            }

            var key = new CacheKey(pawn, tool, stat, 0, currentVersion);
            if (_scoreCache.TryGetValue(key, out var cached))
            {
                score = cached.Score;
                ++_cacheHits;

                // Update access time for LRU cleanup
                _scoreCache[key] = new CacheValue(cached.Score, _accessCounter);
                return true;
            }

            ++_cacheMisses;
            return false;
        }

        /// <summary>
        /// Cache a computed score with current resolver version.
        /// Uses struct keys for efficient storage.
        /// </summary>
        public static void Set(Pawn pawn, Thing tool, StatDef stat, float score)
        {
            int currentVersion = ToolStatResolver.Version;

            // Auto-invalidate if resolver version changed
            if (_cachedResolverVersion != currentVersion)
            {
                _scoreCache.Clear();
                _cachedResolverVersion = currentVersion;
            }

            var key = new CacheKey(pawn, tool, stat, 0, currentVersion);
            _scoreCache[key] = new CacheValue(score, _accessCounter);

            // Periodic cleanup to prevent unbounded growth
            if (_accessCounter - _lastCleanupAccess > 1000)
            {
                CleanupOldEntries();
                _lastCleanupAccess = _accessCounter;
            }
        }

        /// <summary>
        /// Notify cache that pawn's inventory changed (invalidate pawn's entries).
        /// Scaffolding hook for future gameplay integration.
        /// </summary>
        public static void NotifyInventoryChanged(Pawn pawn)
        {
            if (pawn == null) return;

            // For Phase 3, just clear entire cache to be safe
            // Future optimization: only clear entries for this pawn
            _scoreCache.Clear();
        }

        /// <summary>
        /// Notify cache that tool's properties changed (invalidate tool's entries).
        /// Scaffolding hook for future gameplay integration.
        /// </summary>
        public static void NotifyToolChanged(Thing tool)
        {
            if (tool == null) return;

            // For Phase 3, just clear entire cache to be safe
            // Future optimization: only clear entries for this tool
            _scoreCache.Clear();
        }

        /// <summary>
        /// Notify cache that mod settings changed (full invalidation).
        /// Scaffolding hook for future gameplay integration.
        /// </summary>
        public static void NotifySettingsChanged()
        {
            _scoreCache.Clear();
            _cacheHits = 0;
            _cacheMisses = 0;
        }

        /// <summary>
        /// Notify cache that resolver version was bumped (auto-handled).
        /// Scaffolding hook for debugging and monitoring.
        /// </summary>
        public static void NotifyResolverBumped(int newVersion)
        {
            // Cache automatically invalidates on version mismatch
            // This method is for monitoring/debugging only
            if (_cachedResolverVersion != newVersion)
            {
                _scoreCache.Clear();
                _cachedResolverVersion = newVersion;
            }
        }

        /// <summary>
        /// Clear all cached scores and reset statistics.
        /// </summary>
        public static void ClearCache()
        {
            _scoreCache.Clear();
            _cacheHits = 0;
            _cacheMisses = 0;
            _accessCounter = 0;
            _lastCleanupAccess = 0;
            _cachedResolverVersion = -1;
        }

        /// <summary>
        /// Get cache statistics for debugging and monitoring.
        /// </summary>
        public static (int entryCount, int hits, int misses, int resolverVersion) GetCacheStats()
        {
            return (_scoreCache.Count, _cacheHits, _cacheMisses, _cachedResolverVersion);
        }

        /// <summary>
        /// Remove old cache entries to prevent unbounded growth.
        /// Keeps the most recently accessed entries.
        /// </summary>
        private static void CleanupOldEntries()
        {
            const int MaxCacheSize = 500;

            if (_scoreCache.Count <= MaxCacheSize) return;

            // Find cutoff access time (keep most recent half)
            var accessTimes = new List<int>(_scoreCache.Count);
            foreach (var value in _scoreCache.Values)
            {
                accessTimes.Add(value.LastAccess);
            }

            accessTimes.Sort();
            int cutoffAccess = accessTimes[accessTimes.Count / 2];

            // Remove entries older than cutoff
            var keysToRemove = new List<CacheKey>();
            foreach (var kvp in _scoreCache)
            {
                if (kvp.Value.LastAccess < cutoffAccess)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _scoreCache.Remove(key);
            }
        }
    }
}