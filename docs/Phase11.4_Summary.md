# Phase 11.4 Summary: Legacy Cache Invalidation Retirement

**Date**: 2025-09-30  
**Branch**: Refactor  
**Flag**: `STRIP_11_4_OLD_INVALIDATION = true`

## Objective

Remove old "tool stat cache invalidation" Harmony hooks that predate the resolver versioning/events system (Phase 4).

## Investigation Results

### Two Invalidation Systems Found

#### **MODERN SYSTEM (Phase 4) - KEEP**

**File**: `Source/Harmony/HarmonyPatches_CacheInvalidation.cs`

- **Purpose**: Minimal cache invalidation for ScoreCache freshness
- **Hooks**: 5 patches targeting inventory/equipment changes
  - `Pawn_InventoryTracker.Notify_ItemRemoved`
  - `Pawn_EquipmentTracker.Notify_EquipmentAdded`
  - `Pawn_EquipmentTracker.Notify_EquipmentRemoved`
  - `ThingOwner.NotifyAdded` (internal)
  - `ThingOwner.NotifyRemoved` (internal)
- **Calls**: `ScoreCache.NotifyInventoryChanged(pawn)` and `NotifyToolChanged(tool)`
- **Auto-invalidation**: Resolver version tracking (`ToolStatResolver.Version`) provides automatic invalidation on:
  - Quirk registration/clearing
  - Resolver initialization
  - Settings changes (bumps version)

#### **LEGACY SYSTEM (Pre-Phase 4) - STRIP**

**File**: `Source/Harmony/Patch_ToolInvalidation.cs`

- **Purpose**: Old cache invalidation for `ToolFactorCache` (predates modern system)
- **Hooks**: 5 patches targeting damage/destroy/creation/equipment
  - `Thing.TakeDamage` (postfix)
  - `Thing.Destroy` (postfix)
  - `ThingMaker.MakeThing` (postfix)
  - `Pawn_EquipmentTracker.AddEquipment` (reflection-based postfix)
  - `Pawn_EquipmentTracker.RemoveEquipment` (reflection-based postfix)
- **Calls**: `ToolFactorCache.InvalidateForThing(thing)` and `ClearCountersForThing(thing)`
- **Comments**: File header includes "Is this still needed with the new centralized ToolStatResolver?"
- **Artifacts**: Extensive debug logging (development artifact)

### Coverage Analysis

Modern system handles ALL legacy cases:

1. **Equipment add/remove** → Modern: `HarmonyPatches_CacheInvalidation` hooks equipment changes
2. **HP damage** → Modern: Score calculation includes dynamic condition factor (lines 135-137 in `ToolScoring.cs`)
3. **Quality changes** → Modern: Resolver version bump invalidates all scores
4. **Settings changes** → Modern: Resolver version bump invalidates all scores
5. **Tool destruction** → Modern: Not needed (no persistent cache for destroyed things)
6. **Tool creation** → Modern: Not needed (resolver version handles def changes)

**Key Finding**: HP damage does NOT need explicit cache invalidation because `ToolScoring.Score()` calculates condition factor dynamically:

```csharp
if (tool.def.useHitPoints && tool.MaxHitPoints > 0)
{
    float conditionFactor = ConditionMinimum + (1f - ConditionMinimum) * (tool.HitPoints / (float)tool.MaxHitPoints);
    score *= conditionFactor;
}
```

## Changes Made

### 1. Added Phase 11.4 Guards

**File**: `Source/Harmony/Patch_ToolInvalidation.cs`

Wrapped all 4 postfix methods with early return guards:

```csharp
if (Build.Phase11.STRIP_11_4_OLD_INVALIDATION) return;
```

Methods guarded:

- `Postfix_Thing_TakeDamage`
- `Postfix_Thing_Destroy`
- `Postfix_ThingMaker_MakeThing`
- `Postfix_Equipment_Changed`

### 2. Updated File Header

Replaced vague "Is this still needed?" comment with explicit Phase 11.4 documentation explaining:

- Modern system components (HarmonyPatches_CacheInvalidation, resolver version, dynamic HP)
- Why legacy hooks are redundant (point-by-point coverage analysis)
- Phase 11.4 guard instruction

### 3. Enabled Flag

**File**: `Source/Infrastructure/BuildFlags/Phase11.cs`

```csharp
public const bool STRIP_11_4_OLD_INVALIDATION = true;
```

Added summary comment explaining modern system advantages.

## Build Results

**Before guards (baseline)**: 5 warnings (Phase 11.1 only)
**After guards + flag=false**: 9 warnings (5 + 4 new unreachable code from guards)
**After flag=true**: 9 warnings (expected - guards render legacy code unreachable)

All warnings are **CS0162: Unreachable code detected** - expected behavior for compile-time constant evaluation.

### Warning Breakdown

- 5 warnings: `Source/Legacy/LegacyForwarders.cs` (Phase 11.1)
- 4 warnings: `Source/Harmony/Patch_ToolInvalidation.cs` (Phase 11.4 - NEW)

**No real errors** - build succeeded, ZIP created, output mirrored to RimWorld Mods folder.

## Acceptance Criteria Verification

✅ **Build succeeds with flag enabled**  
✅ **Expected CS0162 warnings only (9 total)**  
✅ **Modern invalidation remains active** (`HarmonyPatches_CacheInvalidation.cs` untouched)  
✅ **Resolver version tracking continues** (version bumps on quirk/settings changes)  
✅ **Dynamic HP scoring preserved** (`ToolScoring.Score()` includes condition factor)  
✅ **No runtime behavior changes** (legacy hooks now no-op, modern system already handled all cases)

## Technical Notes

### Why Legacy System Was Redundant

1. **Equipment changes**: Modern system hooks same points via `HarmonyPatches_CacheInvalidation`
2. **HP damage**: Score recalculates condition factor every time (no cache to invalidate)
3. **Quality/settings**: Resolver version auto-invalidates (cache keys include `ResolverVersion`)
4. **Destruction**: Destroyed things can't be scored (no stale entries possible)
5. **Creation**: New tools have no cached entries yet (first score populates cache)

### Cache Architecture (Phase 3-4)

**ScoreCache** uses struct-based keys:

```csharp
private struct CacheKey : IEquatable<CacheKey> {
    public readonly int PawnID;
    public readonly int ThingID;
    public readonly ushort StatDefIndex;
    public readonly int DifficultySeed;
    public readonly int ResolverVersion;  // ← Auto-invalidates on version mismatch
}
```

When `ToolStatResolver.Version` increments (quirks, settings, initialization), ALL cached scores become stale automatically - no explicit invalidation needed.

### Performance Impact

**Neutral** - Modern system was already handling all invalidation cases. Stripping legacy hooks:

- Removes 5 redundant Harmony patches (lighter patch surface)
- Eliminates duplicate invalidation calls (no performance benefit, already fast)
- Removes debug logging overhead (conditional, but still executed in Debug builds)

## Future Work

After Phase 11.4 completes:

- Phase 11.5: Strip old FloatMenu implementations
- Phase 11.6: Strip old scoring method calls
- Phase 11.7: Strip XML duplicate hints/comments
- Phase 11.8: Strip tree toggles/switches
- Phase 11.9: Strip killlist/deprecated components

Then consider physical deletion of guarded code (all Phase 11 flags enabled = safe to delete).

## Files Modified

1. `Source/Harmony/Patch_ToolInvalidation.cs` - Added guards + updated header
2. `Source/Infrastructure/BuildFlags/Phase11.cs` - Enabled flag + added summary comment
3. `docs/Phase11.4_Summary.md` - This document (NEW)

## Commit Message Suggestion

```
Phase 11.4: Strip legacy cache invalidation hooks

- Add Phase 11.4 guards to Patch_ToolInvalidation.cs (4 postfix methods)
- Enable STRIP_11_4_OLD_INVALIDATION flag
- Modern system (Phase 4) covers all cases:
  * Equipment changes: HarmonyPatches_CacheInvalidation
  * HP damage: Dynamic condition factor in ToolScoring.Score()
  * Quality/settings: Resolver version auto-invalidation
- Build succeeds with 9 expected CS0162 warnings (5 from 11.1 + 4 new)
- No runtime behavior changes (legacy hooks were redundant)
```

---

**Status**: ✅ **COMPLETE**  
**Flag**: `STRIP_11_4_OLD_INVALIDATION = true`  
**Build**: SUCCESS (9 warnings, 0 errors)  
**Behavior**: UNCHANGED (modern system active, legacy hooks disabled)
