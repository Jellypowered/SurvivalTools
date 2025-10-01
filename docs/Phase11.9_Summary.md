# Phase 11.9: Kill-List Deletions (Dead Code Removal)

## Objective

Delete dead internal code bodies gated in phases 11.1, 11.4, 11.5 after validation, while keeping public API shims with `[Obsolete(false)]` for external mod compatibility.

## Investigation Summary

### Active Guards Identified (from Phase 11.1-11.8)

**Phase 11.1 (STRIP_11_1_DUP_OPTIMIZER = true):**

- File: `Source/Legacy/LegacyForwarders.cs`
- Guards: 7 conditional blocks checking `STRIP_11_1_DUP_OPTIMIZER`
- Content: Duplicate optimizer/auto-pickup logic (replaced by PreWork_AutoEquip + AssignmentSearch)

**Phase 11.4 (STRIP_11_4_OLD_INVALIDATION = true):**

- File: `Source/Harmony/Patch_ToolInvalidation.cs`
- Guards: 4 conditional blocks checking `STRIP_11_4_OLD_INVALIDATION`
- Content: Legacy cache invalidation hooks (replaced by HarmonyPatches_CacheInvalidation + resolver version)

**Phase 11.5 (STRIP_11_5_OLD_FLOATMENU = true):**

- File: `Source/UI/RightClickRescue/Patch_FloatMenuMakerMap_GetOptions.cs`
- Guards: 2 conditional blocks checking `STRIP_11_5_OLD_FLOATMENU`
- Content: Legacy float menu fallback (replaced by Provider_STPrioritizeWithRescue + FloatMenu_PrioritizeWithRescue)

**Phases 11.2, 11.3, 11.6, 11.7, 11.8:**

- Status: No-op flags (no associated code guards)
- Reason: Already consolidated in earlier phases or investigation found no duplicates

## Changes Made

### 1. LegacyForwarders.cs (Phase 11.1 Cleanup)

**Deleted dead code bodies while preserving public API:**

- `JobGiver_OptimizeSurvivalTools.TryGiveJob()` (both namespaces)

  - **Before:** Conditional logic with 11-line block
  - **After:** Direct `return null;` with Phase 11.9 comment
  - **Preserved:** Class shell with `[Obsolete(false)]` for XML compatibility

- `AutoToolPickup_UtilityIntegrated.ShouldPickUp()` (both namespaces)

  - **Before:** Conditional logic with 6-line block
  - **After:** Direct `return false;` with Phase 11.9 comment
  - **Preserved:** Public method signature with `[Obsolete(false)]`

- `AutoToolPickup_UtilityIntegrated.EnqueuePickUp()` (both namespaces)

  - **Before:** Conditional logic with 5-line block
  - **After:** Empty method body with Phase 11.9 comment
  - **Preserved:** Public method signature with `[Obsolete(false)]`

- `Patch_Pawn_JobTracker_ExtraHardcore.IsBlocked()`
  - **Before:** Conditional logic with 5-line block
  - **After:** Direct `return false;` with Phase 11.9 comment
  - **Preserved:** Public method signature with `[Obsolete(false)]`

**Code Reduction:**

- Removed ~40 lines of dead conditional logic
- Kept 7 public API shims (class shells + method signatures)
- All classes retain `[Obsolete(false)]` attributes

### 2. Patch_ToolInvalidation.cs (Phase 11.4 Cleanup)

**Deleted dead method bodies while preserving Harmony patch structure:**

- `Postfix_Thing_TakeDamage()`

  - **Before:** 43-line conditional block with debug logging and invalidation
  - **After:** 4-line no-op stub with Phase 11.9 comment
  - **Preserved:** Harmony patch method signature and attribute

- `Postfix_Thing_Destroy()`

  - **Before:** 66-line conditional block with holder tracking and invalidation
  - **After:** 4-line no-op stub with Phase 11.9 comment
  - **Preserved:** Harmony patch method signature and attribute

- `Postfix_ThingMaker_MakeThing()`

  - **Before:** 31-line conditional block with virtual tool detection
  - **After:** 4-line no-op stub with Phase 11.9 comment
  - **Preserved:** Harmony patch method signature and attribute

- `Postfix_Equipment_Changed()`
  - **Before:** 22-line conditional block with equipment logging
  - **After:** 4-line no-op stub with Phase 11.9 comment
  - **Preserved:** Harmony patch method signature and attribute

**Code Reduction:**

- Removed ~160 lines of dead invalidation logic
- Kept Harmony patch infrastructure (Init method registers all 4 patches)
- Patches remain attached but do nothing (safe no-op stubs)

**Why keep patches?** Harmony patch removal/re-application can cause stability issues. Empty patch stubs are safer than dynamic unpatch operations.

### 3. Patch_FloatMenuMakerMap_GetOptions.cs (Phase 11.5 Cleanup)

**Deleted dead method bodies while preserving Harmony patch structure:**

- `Prefix()`

  - **Before:** 10-line conditional block with BeginClick call
  - **After:** 4-line no-op stub with Phase 11.9 comment
  - **Preserved:** Harmony prefix method signature and attribute

- `Postfix()`
  - **Before:** 33-line conditional block with fallback rescue option logic
  - **After:** 4-line no-op stub with Phase 11.9 comment
  - **Preserved:** Harmony postfix method signature and attribute

**Code Reduction:**

- Removed ~43 lines of dead fallback logic
- Kept Harmony patch structure (both Prefix and Postfix attributes)
- Modern system (Provider + comprehensive postfix) provides full coverage

### 4. Phase11.cs Flag Update

Updated `STRIP_11_9_KILLLIST` flag:

```csharp
public const bool STRIP_11_9_KILLLIST = true;
```

Added comprehensive comment documenting:

- Files modified (3 files)
- What was removed (method bodies)
- What was preserved (public API + Harmony patch structure)

## Build Status

**Before Phase 11.9:**

- **Warnings:** 11 CS0162 (Unreachable code detected) from active guards
- **Errors:** 0

**After Phase 11.9:**

- **Warnings:** 0 ✅ (All unreachable code removed!)
- **Errors:** 0 ✅
- **DLL:** Successfully compiled and deployed

**Code Size Reduction:**

- **Estimated ~240 lines** of dead code removed
- **Zero functional changes** (all removed code was already unreachable)
- **Public API preserved** for external mod compatibility

## Verification

### Public API Compatibility

**External mods can still:**

1. Reference `JobGiver_OptimizeSurvivalTools` in XML `<thinkRoot>` tags
2. Call `AutoToolPickup_UtilityIntegrated.ShouldPickUp()` (returns false)
3. Call `AutoToolPickup_UtilityIntegrated.EnqueuePickUp()` (no-op)
4. Call `Patch_Pawn_JobTracker_ExtraHardcore.IsBlocked()` (returns false)
5. Reference `LegacyScoringForwarders` API (unchanged - not part of Phase 11.9)

**All preserved symbols marked `[Obsolete(false)]`:**

- No compiler warnings for external mods
- Clear signal that methods are deprecated but safe to use
- Forwards to modern systems or returns safe defaults

### Harmony Patch Stability

**Patch infrastructure preserved:**

- `Patch_ToolInvalidation.Init()` still registers 4 patches
- `Patch_FloatMenuMakerMap_GetOptions` still has Prefix + Postfix attributes
- Patches attached but do nothing (safer than dynamic unpatch)
- Modern systems provide full coverage (no functionality lost)

### Functional Equivalence

**Before Phase 11.9 (with flags = true):**

- Conditional guards returned early (no-op)
- Legacy code paths never executed
- 11 CS0162 warnings about unreachable code

**After Phase 11.9:**

- Method bodies are direct no-ops
- Same runtime behavior (no-op)
- 0 warnings (cleaner code)

**Result:** Zero functional changes, cleaner codebase, same external API.

## Phase 11 Complete Summary

### All Phases Status

| Phase | Flag                                   | Status      | Action Taken                                     |
| ----- | -------------------------------------- | ----------- | ------------------------------------------------ |
| 11.0  | N/A                                    | ✅ Complete | Safety harness created                           |
| 11.1  | `STRIP_11_1_DUP_OPTIMIZER = true`      | ✅ Complete | Optimizer stripped → Dead code removed (11.9)    |
| 11.2  | `STRIP_11_2_DUP_STAT_INJECTORS = true` | ✅ Complete | No-op (already consolidated Phase 4)             |
| 11.3  | `STRIP_11_3_MISC_WG_GATES = true`      | ✅ Complete | No-op (already consolidated Phase 5-6)           |
| 11.4  | `STRIP_11_4_OLD_INVALIDATION = true`   | ✅ Complete | Invalidation stripped → Dead code removed (11.9) |
| 11.5  | `STRIP_11_5_OLD_FLOATMENU = true`      | ✅ Complete | FloatMenu stripped → Dead code removed (11.9)    |
| 11.6  | `STRIP_11_6_OLD_SCORING_CALLS = true`  | ✅ Complete | No-op (already consolidated Phase 9)             |
| 11.7  | `STRIP_11_7_XML_DUP_HINTS = true`      | ✅ Complete | No-op (XML patches are authoritative)            |
| 11.8  | `STRIP_11_8_TREE_TOGGLES = true`       | ✅ Complete | No-op (centralized STC authority Phase 10)       |
| 11.9  | `STRIP_11_9_KILLLIST = true`           | ✅ Complete | Dead code removed, public API preserved          |

### Final Stats

**Warnings Eliminated:**

- Phase 11.1-11.8: 11 CS0162 warnings (unreachable code)
- Phase 11.9: 0 warnings ✅

**Code Removed:**

- ~240 lines of dead code deleted
- 3 files cleaned up
- Public API fully preserved

**External Compatibility:**

- All `[Obsolete(false)]` shims retained
- Harmony patch structure preserved
- Zero breaking changes for external mods

## Next Steps

**Phase 11 is now COMPLETE.** All legacy code has been:

1. ✅ Identified and guarded (Phase 11.0-11.8)
2. ✅ Validated in production (Phase 11.1, 11.4, 11.5 enabled for multiple releases)
3. ✅ Dead code removed (Phase 11.9)
4. ✅ Public API preserved for external mods

**Maintenance Notes:**

- Keep `[Obsolete(false)]` shims for at least one major version
- Consider removing shims in future major release (2.0?) after external mod migration period
- Monitor for external mod compatibility issues (unlikely - API unchanged)

**Future Considerations:**

- Phase 12+ could focus on new features rather than cleanup
- Consider adding `[Obsolete(true)]` to shims in next major version
- Eventually delete entire `LegacyForwarders.cs` once external mods migrate

---

**Phase 11 Status:** ✅ COMPLETE (All dead code removed, public API preserved, 0 warnings, 0 errors)
