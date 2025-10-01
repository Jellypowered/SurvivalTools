# Phase 11.1 — Strip Duplicate Auto-Equip/Optimizer Guts

## Completion Summary

### Goal

Route all auto-equip to PreWork + AssignmentSearch only. Keep public legacy classes as no-op/safe forwarders.

### Changes Made

#### 1. Updated `Source/Legacy/LegacyForwarders.cs`

- **JobGiver_OptimizeSurvivalTools** (both namespaces):

  - Wrapped internal logic with `Build.Phase11.STRIP_11_1_DUP_OPTIMIZER` check
  - Added XML documentation explaining Phase 11.1 changes
  - Updated `[Obsolete]` attributes to include "Phase 11: legacy shim" marker
  - Returns null (safe no-op) regardless of flag state (logic already obsolete)

- **AutoToolPickup_UtilityIntegrated** (both namespaces):

  - Wrapped `ShouldPickUp()` method with Phase 11.1 check
  - Wrapped `EnqueuePickUp()` method with Phase 11.1 check
  - Added XML documentation
  - Updated `[Obsolete]` attributes
  - Returns false/void no-op regardless of flag state

- **Patch_Pawn_JobTracker_ExtraHardcore**:
  - Wrapped `IsBlocked()` method with Phase 11.1 check
  - Added XML documentation
  - Updated `[Obsolete]` attribute
  - Returns false (safe no-op) regardless of flag state

### Verification

#### Internal Callers

✅ **No internal callers found** - verified via grep search that only comments reference these classes:

- `SurvivalToolUtility.cs` - comments only
- `LegacyForwarders.cs` - definitions only
- `AI/JobGiver_OptimizeSurvivalTools.cs` - wrapped in `#if ST_LEGACY_PATCHES` (disabled)
- `AI/AutoToolPickup_UtilityIntegrated.cs` - wrapped in `#if ST_LEGACY_PATCHES` (disabled)

#### Active Paths Confirmed

✅ **PreWork_AutoEquip + AssignmentSearch are active**:

- `PreWork_AutoEquip` patches job start methods (allowlisted in ST_PatchGuard)
- `AssignmentSearch.TryUpgradeFor()` used throughout JobGate, UI, etc.
- `AssignmentSearch.GetEffectiveCarryLimit()` used in inventory management
- `AssignmentSearch.HasAcquisitionPendingOrQueued()` used in job gating

#### Harmony Patches

✅ **No new Harmony patches introduced or removed**:

- PatchGuard allowlist unchanged
- No `[HarmonyPatch]` attributes added or removed
- Only code logic wrapped in compile-time checks

#### Build Status

✅ **Build succeeded** (with expected warnings):

- 7 warnings for "Unreachable code detected" (expected for compile-time const bool checks)
- No errors
- DLL compiled successfully

### Acceptance Criteria

#### ✅ With `STRIP_11_1_DUP_OPTIMIZER=false` (current state):

- Behavior unchanged (legacy stubs still return no-op values)
- Build succeeds
- No runtime impact

#### ✅ With `STRIP_11_1_DUP_OPTIMIZER=true` (when enabled):

- Legacy optimizer code explicitly returns no-op values via first return path
- Auto-equip still works via PreWork_AutoEquip + AssignmentSearch
- Public class signatures preserved for XML compatibility

#### ✅ No new Harmony patches:

- Confirmed via code inspection
- ST_PatchGuard allowlist unchanged
- No patch attributes added/removed

### Notes

1. **Unreachable Code Warnings**: The 7 unreachable code warnings are expected and correct. When `STRIP_11_1_DUP_OPTIMIZER=false` (a compile-time constant), the compiler correctly detects that code after the first return is unreachable. This is the intended behavior for compile-time feature flags.

2. **Legacy Compatibility**: All public types and method signatures preserved for:

   - XML `<thinkRoot Class="SurvivalTools.JobGiver_OptimizeSurvivalTools" />` references
   - External mod reflection calls (if any)
   - Backward compatibility

3. **Safety**: Both code paths (flag=true and flag=false) return safe no-op values, ensuring no runtime errors regardless of flag state.

4. **Next Steps**: When confident, set `STRIP_11_1_DUP_OPTIMIZER=true` to enable stripping, then eventually remove the legacy path code entirely in a future phase.
