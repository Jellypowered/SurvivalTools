# Phase 11.12: Dead Code Cleanup - TryEnqueuePickupForMissingTool

## Summary

Removed ~180 lines of dead legacy pickup code that was never called. This eliminates the last active reference to the `autoTool` setting.

## Motivation

After Phase 11.11 removed the manual assignment system, investigation revealed:

- The `autoTool` setting had no UI checkbox (replaced by `enableAssignments`)
- The only active check for `autoTool` was in `TryEnqueuePickupForMissingTool()`
- Grep search confirmed the function was **never called** anywhere in the codebase
- All tool acquisition is now handled by `AssignmentSearch` + `PreWork_AutoEquip`

## Changes Made

### Files Modified (1)

#### Source/SurvivalToolUtility.cs

**Removed (~180 lines):**

- Complete implementation of `TryEnqueuePickupForMissingTool()` (lines 1461-1640)
  - Radial scan around pawn (28 tile radius)
  - Storage search (80 tile radius)
  - Nearby haulable search
  - Wide fallback search (200 tiles)
  - Nightmare mode purge logic
  - Job enqueue with `JobDefOf.TakeInventory`
  - Last active reference to `Settings.autoTool`

**Added (~13 lines):**

```csharp
/// <summary>
/// Phase 11.11: Obsolete - unused legacy pickup function.
/// Replaced by AssignmentSearch + PreWork_AutoEquip which handle all tool acquisition.
/// Kept as no-op stub for any external mod references.
/// </summary>
[Obsolete("Phase 11.11: Unused legacy pickup function. Replaced by AssignmentSearch + PreWork_AutoEquip.", true)]
public static bool TryEnqueuePickupForMissingTool(Pawn pawn, List<StatDef> requiredStats)
{
    // Phase 11.11: Dead code removed. AssignmentSearch + PreWork_AutoEquip handle all tool pickup.
    // This function was never called anywhere in the codebase.
    // The last reference to Settings.autoTool has been eliminated.
    return false;
}
```

**Net Change:** ~167 lines removed

## Legacy Settings Status

### autoTool Field

**Status:** Kept for save compatibility only

**Migration Path:**

1. Old saves load `autoTool` from saved XML
2. Settings UI has single `enableAssignments` checkbox
3. Toggle syncs to legacy fields: `autoTool = enableAssignments`
4. No active code checks `autoTool` anymore

**Code Location:**

```csharp
// SurvivalToolsSettings.cs ~line 400
if (assignToggle != enableAssignments)
{
    enableAssignments = assignToggle;
    toolOptimization = assignToggle;  // Legacy sync
    autoTool = assignToggle;          // Legacy sync
}
```

## Replacement Systems

### TryEnqueuePickupForMissingTool → Modern Equivalents

**Old System (removed):**

- Manual on-demand pickup scanning
- Distance-based scoring with legacy `ScoreToolForStats()`
- Direct job enqueue with `JobDefOf.TakeInventory`
- Called by: _nothing_ (dead code)

**New System (Phase 11):**

1. **AssignmentSearch**: Pre-work tool selection

   - `FindBestToolForStats()` - optimized scoring
   - `TryProvisionToolForJob()` - pre-work auto-equip
   - Integrated with job start-of-job checks

2. **PreWork_AutoEquip**: Job system integration

   - `Patch_Toils_JobTransforms_ExtractNextTargetFromQueue` - queue scanning
   - Automatic tool provisioning before work starts
   - Nightmare mode integration

3. **WorkGiver Integration**: Discovery and provisioning
   - Cleaning WorkGiver discovery (Phase 11.10)
   - Integrated tool checks at job discovery time
   - No manual pickup needed

## Build Results

✅ **Build succeeded with 0 errors, 0 warnings**

Previous Phase 11.11 obsolete warnings were resolved when UI files were deleted:

- `Dialog_ManageSurvivalToolAssignments.cs` (removed)
- `PawnColumnWorker_SurvivalToolAssignment.cs` (removed)

## Testing Checklist

### Verification Tests

- [x] Build compiles cleanly (0 errors, 0 warnings)
- [ ] Old saves load without errors
- [ ] Tool pickup still works during jobs
- [ ] Settings checkbox syncs legacy flags
- [ ] No null reference exceptions from removed code

### Regression Tests

- [ ] Pawns auto-equip tools before jobs
- [ ] Nightmare mode tool limits enforced
- [ ] Forced tools (right-click "Force Wear") still work
- [ ] Cleaning jobs discover tools properly

## Phase 11 Cumulative Stats

### Code Removal Totals

- **Phase 11.9**: Legacy optimizer dead code
- **Phase 11.10**: WorkSpeedGlobal gating (~750 lines)
- **Phase 11.11**: Manual assignment system (~280 net lines)
- **Phase 11.12**: Dead pickup function (~167 lines)

**Estimated Total: 1000+ lines of legacy code removed**

### Remaining Legacy Components

All kept for save compatibility:

1. `SurvivalToolAssignmentDatabase` - Loads old profiles (unused)
2. `SurvivalToolAssignment` - Profile stub
3. `Pawn_SurvivalToolAssignmentTracker` - Migrates forcedHandler
4. Settings fields: `autoTool`, `toolOptimization` (sync to `enableAssignments`)

## Future Maintenance

### Do NOT Remove

- Legacy assignment classes (save compatibility)
- Legacy settings fields (save loading)
- Obsolete stub methods (external mod API)

### Safe to Ignore

- "Obsolete" warnings in legacy forwarder files
- Empty stub method bodies
- Unused legacy fields

### Next Phase Candidates

No immediate next phase - Phase 11 cleanup complete. Future phases would target:

- Phase 12: Additional optimization opportunities
- Phase 13: New feature development on clean codebase

## Notes

- Function had sophisticated logic (radial scan, storage priority, distance scoring)
- All functionality replaced by simpler, more integrated systems
- No external mods were using this function (confirmed via search)
- Keeping as `[Obsolete(error: true)]` prevents accidental future use
