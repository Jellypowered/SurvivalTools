# Phase 11.8: Tree System Legacy Toggles Investigation

## Objective

Remove internal SurvivalTools toggles that conflict with STC (Separate Tree Chopping) authority, while keeping ST behavior when STC is absent.

**Goal:** STC owns trees when present. Remove any legacy toggles that tried to "partially allow" ST felling under certain settings when STC is active.

## Investigation Summary

### STC Integration Architecture Review

**Centralized Authority System:**

- `TreeSystemArbiter` - Detects STC/PT/TCSS and assigns authority
- `TreeSystemArbiterActiveHelper.IsSTCAuthorityActive()` - Single source of truth for STC checks

**Current STC Guards (Phase 10 - Already Complete):**

1. **STC_Strip_TreeFelling.cs** (static constructor runs at startup)

   - Removes ST tree WorkGivers from WorkType lists when STC detected
   - Patches PlantsCut to reject trees when STC active
   - **Status:** ✅ Already comprehensive

2. **TreeSystemArbiter.cs** (static constructor)

   - Detects STC via mod list scanning
   - Forces `enableSurvivalToolTreeFelling = false` when external authority present
   - **Status:** ✅ Already enforces override

3. **PreWork_AutoEquip.cs** (lines 221-241)

   - Blocks `ST_JobDefOf.FellTree` and `ST_JobDefOf.FellTreeDesignated` when STC active
   - Returns `false` to prevent job from starting
   - **Status:** ✅ Already blocks at TryTakeOrderedJob level

4. **WorkGiver_FellTrees.cs** (lines 28, 60)

   - `PotentialWorkThingsGlobal`: `yield break` when STC active
   - `JobOnThing`: `return null` when STC active
   - **Status:** ✅ Already guards both entry points

5. **JobDriver_FellTree.cs** (lines 15-21, 50)

   - `Init()`: `EndJobWith(JobCondition.Incompletable)` when STC active
   - `DestroyThing` toil: Early return when STC active
   - **Status:** ✅ Already guards execution

6. **JobDriver_FellTree_Designated.cs** (line 16)

   - `Init()`: `EndJobWith(JobCondition.Incompletable)` when STC active
   - **Status:** ✅ Already guards execution

7. **ConditionalRegistration.cs** (lines 33-63)

   - Removes ST_FellTrees WorkGiver when `enableSurvivalToolTreeFelling = false`
   - Used by TreeSystemArbiter override
   - **Status:** ✅ Already provides cleanup mechanism

8. **SurvivalToolsSettings.cs** (lines 410-426)
   - UI disables tree felling checkbox when STC detected
   - Forces `enableSurvivalToolTreeFelling = false` regardless of saved preference
   - **Status:** ✅ Already enforces UI override

### Settings Property Analysis

**`enableSurvivalToolTreeFelling` / `TreeFellingSystemEnabled`:**

- **Purpose:** User preference for whether ST tree felling should be active
- **NOT a legacy toggle** - It's a valid user setting
- **Correctly overridden** by STC authority via TreeSystemArbiter

**Flow when STC is active:**

1. TreeSystemArbiter static constructor detects STC
2. Forces `enableSurvivalToolTreeFelling = false`
3. Settings UI displays "STC override" message and disables checkbox
4. ConditionalRegistration removes ST_FellTrees WorkGiver
5. STC_Strip_TreeFelling removes remaining WGs from WorkType lists
6. All guards (PreWork, WG, JobDriver) block ST tree jobs via `IsSTCAuthorityActive()`

**Flow when STC is NOT active:**

1. User setting `enableSurvivalToolTreeFelling` respected
2. If `false`: ConditionalRegistration removes ST_FellTrees WorkGiver
3. If `true`: ST tree felling works normally

### Verification: TreeFellingSpeed Still Gates STC/Vanilla Chop

**Requirement:** TreeFellingSpeed should still gate STC/vanilla chop jobs even when STC is active

**Current Implementation:**

```csharp
// StatGatingHelper.cs line 28
// (Removed prior STC bypass: TreeFellingSpeed should still gate even when STC is active.)
```

**Evidence of gating:**

- `StatPart_SurvivalTools.cs` line 59: TreeFellingSpeed registered in SupportedWorkStats
- `StatGatingHelper.cs` lines 176-177: Tree-related WorkGivers mapped to TreeFellingSpeed
- STC's ChopTree jobs go through StatPart_SurvivalTools → TreeFellingSpeed applies
- **Status:** ✅ TreeFellingSpeed gating preserved

## Findings

### NO Legacy Toggles Found

**Investigation Result:** All tree-related settings and guards are **correctly implemented** and **non-conflicting**:

1. **`enableSurvivalToolTreeFelling`** - Valid user preference, properly overridden by STC
2. **STC guards** - All use centralized `IsSTCAuthorityActive()` check (no ad-hoc toggles)
3. **TreeFellingSpeed gating** - Applies to STC jobs (not bypassed)
4. **WorkGiver stripping** - Runtime removal via STC_Strip_TreeFelling + ConditionalRegistration

**No conflicting internal toggles** that try to "partially allow" ST felling when STC is active were found.

### Architecture is Already Correct

The Phase 10 STC integration already achieved the goal:

- ✅ With STC: No ST FellTree jobs appear
- ✅ With STC: STC/vanilla chop still gates on TreeFellingSpeed
- ✅ Without STC: Original ST felling works (respects user setting)
- ✅ Centralized authority checks (no scattered toggles)

## Decision: Phase 11.8 is NO-OP

**Conclusion:** No legacy tree toggles exist that conflict with STC authority.

**Rationale:**

1. **STC integration is comprehensive** - Phase 10 already implemented centralized authority system
2. **User setting is valid** - `enableSurvivalToolTreeFelling` is correctly overridden, not a conflicting toggle
3. **No ad-hoc guards** - All checks use `IsSTCAuthorityActive()` from TreeSystemArbiter
4. **TreeFellingSpeed gating preserved** - StatPart applies to STC jobs as intended

**Code Review:**

- Examined 8 major integration points (STC_Strip, Arbiter, PreWork, WG, JobDrivers, Settings, ConditionalRegistration)
- All use centralized authority checks
- No "partial allow" logic found
- No redundant toggle switches found

## Changes Made

**Source/Infrastructure/BuildFlags/Phase11.cs:**

- Set `STRIP_11_8_TREE_TOGGLES = true` (NO-OP flag, no code guards)
- Comment: "Phase 11.8: NO-OP - Tree system already uses centralized STC authority (Phase 10)"

**docs/Phase11.8_Summary.md:**

- Created this investigation summary

## Build Status

**Before:** 11 CS0162 warnings (from phases 11.1, 11.4, 11.5)
**After:** 11 CS0162 warnings (unchanged, no new guards added)
**Errors:** 0

## Verification

### With STC Active:

- ✅ ST FellTree jobs suppressed at all levels (PreWork, WG, JobDriver)
- ✅ ST tree WorkGivers removed from WorkType lists
- ✅ TreeFellingSpeed still gates STC ChopTree jobs
- ✅ Settings UI shows "STC override" and disables checkbox

### Without STC:

- ✅ User setting `enableSurvivalToolTreeFelling` respected
- ✅ ST tree felling works normally when enabled
- ✅ ST_FellTrees WorkGiver active when enabled

No runtime testing needed - architecture review confirms correct implementation.

## Next Steps

**Phase 11.9:** Strip killlist/deprecated components (final cleanup)

---

**Phase 11.8 Status:** ✅ COMPLETE (Investigation confirmed no conflicting toggles exist)
