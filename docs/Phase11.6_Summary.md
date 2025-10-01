# Phase 11.6 Summary: Legacy Scoring API Migration (Already Complete)

**Date**: 2025-09-30  
**Branch**: Refactor  
**Flag**: `STRIP_11_6_OLD_SCORING_CALLS = true` (no-op)

## Objective

Unify scoring API: leave `ToolScoreUtility` as a public `[Obsolete(false)]` forwarder for external mods, but switch all internal calls to `SurvivalTools.Scoring.ToolScoring`.

## Investigation Results

### Finding: Already Complete in Phase 9

**No work required** - this phase was already completed in an earlier refactor (Phase 9 based on file headers).

### Current Architecture

#### **MODERN API (Internal Use) - Scoring.ToolScoring**

**File**: `Source/Scoring/ToolScoring.cs` (Phase 3)

- **Methods**:
  - `Score(Thing tool, Pawn pawn, StatDef workStat)` - Score a tool for work stat
  - `GetBestTool(Pawn pawn, StatDef workStat, out float score)` - Find best tool
  - `TopContributors(Thing tool, Pawn pawn, StatDef workStat, int max)` - Get top contributing factors
- **Features**:
  - Zero allocations in hot path
  - ScoreCache integration with resolver version tracking
  - Deterministic scoring using ToolStatResolver exclusively
  - Quality scaling, condition penalties, smoothing bonuses

#### **LEGACY FORWARDERS (External Compatibility) - ToolScoreUtility**

**File**: `Source/Legacy/LegacyScoringForwarders.cs` (Phase 9)

- **Purpose**: Maintain backward compatibility for external mods
- **Methods** (all forward to `Scoring.ToolScoring`):
  - `Score(Thing, Pawn, StatDef)` → `ToolScoring.Score()`
  - `GetBestTool(Pawn, StatDef, out score)` → `ToolScoring.GetBestTool()`
  - `GetToolStatFactor(Thing, Pawn, StatDef)` → `ToolScoring.Score()` (legacy shim)
  - `GetBestToolWithFactor(Pawn, StatDef, out factor)` → `ToolScoring.GetBestTool()` (legacy shim)
  - `TopContributors(Thing, Pawn, StatDef, int)` → converts to string array for compatibility
- **Attributes**: All marked `[Obsolete("Use SurvivalTools.Scoring.ToolScoring instead.", false)]`
- **Dual Namespace**: Available in both `SurvivalTools` and `SurvivalTools.Legacy` namespaces

### Internal Code Audit

**Search Results**: Zero internal uses of `ToolScoreUtility` found

**All internal code uses modern API**:

```csharp
using SurvivalTools.Scoring;  // 8 files

// Examples:
var score = ToolScoring.Score(tool, pawn, workStat);
var best = ToolScoring.GetBestTool(pawn, workStat, out float score);
var contributors = ToolScoring.TopContributors(tool, pawn, workStat, 2);
```

**Files using modern API**:

1. `Source/Stats/StatPart_SurvivalTools.cs`
2. `Source/Assign/PreWork_AutoEquip.cs`
3. `Source/Assign/AssignmentSearch.cs`
4. `Source/Assign/NightmareCarryEnforcer.cs`
5. `Source/UI/RightClickRescue/FloatMenu_PrioritizeWithRescue.cs`
6. `Source/DebugTools/DebugAction_GearTabTools.cs`
7. `Source/DebugTools/DebugAction_AssignmentSystem.cs`

## Changes Made

### 1. Enabled Flag (Documentation Only)

**File**: `Source/Infrastructure/BuildFlags/Phase11.cs`

```csharp
public const bool STRIP_11_6_OLD_SCORING_CALLS = true; // No-op: already consolidated in Phase 9
```

Added note explaining work already complete, forwarders preserved for external compatibility.

### 2. Created Phase 11.6 Summary

**File**: `docs/Phase11.6_Summary.md` (this document)

Documents investigation results and confirms migration already complete.

## Build Results

**No changes required** - build remains unchanged from Phase 11.5

**Warning Count**: 11 CS0162 warnings (same as Phase 11.5)

- 5 warnings from Phase 11.1
- 4 warnings from Phase 11.4
- 2 warnings from Phase 11.5

**No new warnings** - flag change is documentation-only (no associated guards)

## Acceptance Criteria Verification

✅ **Scoring remains identical** (modern API active since Phase 9)  
✅ **No public API break for external mods** (forwarders preserved with `[Obsolete(false)]`)  
✅ **All internal code uses modern API** (migration complete in Phase 9)  
✅ **Build succeeds** (no changes required)

## Technical Notes

### Phase 9 Implementation (Already Complete)

**What Was Done in Phase 9**:

1. Created `Source/Scoring/ToolScoring.cs` - Modern deterministic scoring system (Phase 3)
2. Created `Source/Legacy/LegacyScoringForwarders.cs` - Thin wrappers for external compatibility
3. Migrated all internal code to use `SurvivalTools.Scoring.ToolScoring` directly
4. Marked forwarders with `[Obsolete("...", false)]` - warning but no error
5. Provided dual namespace support (`SurvivalTools` + `SurvivalTools.Legacy`)

**Why This Design**:

- **Internal code**: Uses modern API directly for best performance (zero obsolete warnings)
- **External mods**: Can continue using `ToolScoreUtility` (compatibility preserved)
- **Migration path**: Obsolete warning guides external devs to new API
- **No breaking changes**: `false` parameter means warning-only, not error

### Forwarder Pattern

The legacy forwarders follow a clean pattern:

```csharp
namespace SurvivalTools
{
    [Obsolete("Use SurvivalTools.Scoring.ToolScoring instead.", false)]
    public static class ToolScoreUtility
    {
        public static float Score(Thing tool, Pawn pawn, StatDef workStat)
            => Scoring.ToolScoring.Score(tool, pawn, workStat);

        public static Thing GetBestTool(Pawn pawn, StatDef workStat, out float score)
            => Scoring.ToolScoring.GetBestTool(pawn, workStat, out score);

        // Legacy shims for old call signatures
        public static float GetToolStatFactor(Thing tool, Pawn pawn, StatDef workStat)
            => Scoring.ToolScoring.Score(tool, pawn, workStat);
    }
}

namespace SurvivalTools.Legacy
{
    using Root = global::SurvivalTools.ToolScoreUtility;
    [Obsolete("Use SurvivalTools.Scoring.ToolScoring instead.", false)]
    public static class ToolScoreUtility
    {
        // All methods delegate to SurvivalTools.ToolScoreUtility
        public static float Score(Thing tool, Pawn pawn, StatDef workStat) => Root.Score(tool, pawn, workStat);
        // ...
    }
}
```

### Performance Considerations

**Zero overhead for internal code**:

- Direct calls to `ToolScoring` (no indirection)
- Compiler can inline methods
- No obsolete warnings in internal code

**Minimal overhead for external mods**:

- Single method call indirection
- JIT can often inline these thin wrappers
- Negligible performance impact

## Future Work

After Phase 11.6 (already complete):

- Phase 11.7: Strip XML duplicate hints/comments
- Phase 11.8: Strip tree toggles/switches
- Phase 11.9: Strip killlist/deprecated components

**Physical Deletion**: After all Phase 11 flags enabled, consider:

- Keep forwarders indefinitely (external mod compatibility)
- Can eventually remove when external mods migrate (major version bump)
- Document in deprecation policy

## Files Modified

1. `Source/Infrastructure/BuildFlags/Phase11.cs` - Enabled flag + added documentation note
2. `docs/Phase11.6_Summary.md` - This document (NEW)

**Files NOT Modified** (work already complete):

- `Source/Scoring/ToolScoring.cs` - Already modern (Phase 3)
- `Source/Legacy/LegacyScoringForwarders.cs` - Already exists (Phase 9)
- Internal code files - Already migrated (Phase 9)

## Commit Message Suggestion

```
Phase 11.6: Document scoring API migration (already complete)

- Enable STRIP_11_6_OLD_SCORING_CALLS flag (documentation-only)
- Investigation confirms migration completed in Phase 9:
  * All internal code uses SurvivalTools.Scoring.ToolScoring
  * Legacy forwarders (LegacyScoringForwarders.cs) preserved for external mods
  * Zero internal uses of ToolScoreUtility found
- No code changes required (flag is no-op)
- Build unchanged (11 warnings from previous phases)
```

---

**Status**: ✅ **COMPLETE** (Already done in Phase 9)  
**Flag**: `STRIP_11_6_OLD_SCORING_CALLS = true` (no-op)  
**Build**: SUCCESS (11 warnings, 0 errors)  
**Behavior**: UNCHANGED (migration already complete, forwarders active)
