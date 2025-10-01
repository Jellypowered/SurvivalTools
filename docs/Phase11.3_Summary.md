# Phase 11.3 — Consolidate WorkGiver/Job Gates to JobGate

## Completion Summary

### Goal

Eliminate ad-hoc per-WG gating helpers; `JobGate` is authority. Keep behavior the same.

### Investigation Results

✅ **GATING ALREADY CONSOLIDATED TO JOBGATE**

The gating consolidation was completed in earlier phases. `JobGate.ShouldBlock()` is already the single authority for all gating decisions.

#### Current Gating Architecture (Correct & Complete)

**Single Authority: `JobGate.ShouldBlock()`** (`Source/Gating/JobGate.cs`)

- **All gating decisions** flow through this single method
- Called from:
  - `WorkGiver_Gates` Harmony patches (HasJobOnThing/HasJobOnCell/JobOnThing/JobOnCell)
  - `GatingEnforcer` (validation of running jobs)
  - `Alert_ToolGatedWork` (UI alerts)
  - `PreWork_AutoEquip` (for logging consistency)
  - UI builders (RightClickRescue)
  - Debug tools

**Helper Utilities (Called BY JobGate, Not Duplicates):**

1. **`StatGatingHelper.ShouldBlockJobForStat()`**

   - Determines if a specific stat should hard-block under current settings/mode
   - **Called BY JobGate** at line 127: `if (StatGatingHelper.ShouldBlockJobForStat(s, settings, pawn))`
   - Not a duplicate - it's a utility function for stat-level logic
   - Integrates with RR compatibility

2. **`StatGatingHelper.GetStatsForWorkGiver()`**

   - Resolves which stats a WorkGiver requires (extension or heuristics)
   - **Called BY JobGate** via `ResolveRequiredStats()` at line 286
   - Not a duplicate - it's a stat resolution utility

3. **`SurvivalToolUtility.ShouldGateByDefault()`**

   - Keyword-based early filter for WorkGivers (is this type of work gate-eligible?)
   - Used in LIMITED contexts:
     - `WorkSpeedGlobalConfigWindow` (UI filtering for settings display)
     - `SurvivalToolsSettings` (settings initialization)
     - `Patch_WorkGiver_MissingRequiredCapacity` (early-out before checking capacities)
     - `SurvivalToolValidation` (validation filtering)
   - **NOT used for actual gating decisions** - those go through JobGate
   - This is a pre-filter, not a gate decision maker

4. **`WorkSpeedGlobalHelper.ShouldGateJob()`**
   - Checks if a WorkSpeedGlobal job should be gated based on settings
   - Only used for WorkSpeedGlobal-specific UI/settings logic
   - NOT used in actual runtime gating path

### Verification of No Duplicates

**✅ All gating calls route to JobGate:**

- Searched for "ShouldBlock" calls: all point to `JobGate.ShouldBlock()`
- No alternate gating decision makers found
- Helper methods are utilities called BY JobGate, not parallel implementations

**✅ Single decision point verified:**

```csharp
// All actual gating decisions go through this:
JobGate.ShouldBlock(pawn, wg, job, forced, out reasonKey, out a1, out a2)

// These are helpers that JobGate CALLS internally:
StatGatingHelper.ShouldBlockJobForStat(stat, settings, pawn)  // ← called by JobGate
StatGatingHelper.GetStatsForWorkGiver(wgDef)                   // ← called by JobGate

// This is a pre-filter, NOT a gating decision:
SurvivalToolUtility.ShouldGateByDefault(wgDef)                 // ← UI/settings only
```

**✅ Unified logging:**

- All gating decisions produce `[JobGate] Decision:` log lines via `LogDecisionLine()`
- Consistent format: `BLOCK` or `ALLOW` with reason codes
- Right-click rescue triggers properly off JobGate outcomes

### Architecture Flow (Current State - Correct)

```
WorkGiver_Scanner.HasJobOnThing/HasJobOnCell/JobOnThing/JobOnCell
  → WorkGiver_Gates.Pre/Post patches
    → JobGate.ShouldBlock()  ← SINGLE ENTRY POINT
      ├→ Early-outs (pawn eligibility, mode check, tool-less jobs)
      ├→ ResolveRequiredStats(wg, job)
      │   └→ StatGatingHelper.GetStatsForWorkGiver()  ← helper
      ├→ Filter to hard-blocking stats
      │   └→ StatGatingHelper.ShouldBlockJobForStat()  ← helper
      ├→ Check tool availability via ToolScoring
      ├→ Rescue logic (AssignmentSearch.TryUpgradeFor)
      └→ Final block/allow decision
        └→ LogDecisionLine()  ← unified logging
```

### Code Organization

The architecture follows proper separation of concerns:

- **JobGate** = Decision authority (the "what" and "when" to block)
- **StatGatingHelper** = Stat-level rules (the "which stats" matter)
- **ToolScoring** = Tool availability (the "does pawn have adequate tools")
- **AssignmentSearch** = Tool acquisition (the "rescue" flow)
- **SurvivalToolUtility.ShouldGateByDefault** = Pre-filter utility (not a gate)

No consolidation needed - this is already well-architected!

### Acceptance Criteria

#### ✅ Same gating decisions printed

- All decisions log via `JobGate.LogDecisionLine()`
- Format: `[JobGate] Decision: BLOCK|ALLOW | pawn=X | ctx=Y | forced=Z | reason=R`
- Includes stat detail when relevant

#### ✅ Right-click rescue still triggers off JobGate outcomes

- `RightClickRescueBuilder` calls `JobGate.ShouldBlock()` directly
- Lines 257, 374, 412 in RightClickRescueBuilder.cs
- Rescue logic integrated into JobGate (lines 150-237 of JobGate.cs)

### Conclusion

**Phase 11.3 is already complete.** The gating consolidation was accomplished in earlier phases (Phase 5-6). The current architecture has:

- ✅ **Single authority:** `JobGate.ShouldBlock()`
- ✅ **No duplicate gating logic:** All helpers are utilities called BY JobGate
- ✅ **Unified logging:** All decisions produce consistent log lines
- ✅ **Proper separation:** Pre-filters vs decision makers are clearly distinguished

**No changes needed for Phase 11.3.**

### Flag Status

```csharp
public const bool STRIP_11_3_MISC_WG_GATES = false;
```

**Recommendation:** Can be set to `true` immediately since there are no miscellaneous WorkGiver gates to strip. The flag can remain as a placeholder for documentation purposes, but no code guards are needed.

### Helper Methods Analysis

The following methods are NOT duplicates and should remain:

1. **`StatGatingHelper.ShouldBlockJobForStat()`** - KEEP (called by JobGate)
2. **`StatGatingHelper.GetStatsForWorkGiver()`** - KEEP (called by JobGate)
3. **`StatGatingHelper.ShouldBlockBuildRoof()`** - KEEP (specialized helper for roofing)
4. **`SurvivalToolUtility.ShouldGateByDefault()`** - KEEP (pre-filter for UI/settings, not a gate)
5. **`WorkSpeedGlobalHelper.ShouldGateJob()`** - KEEP (UI/settings only, not runtime gating)
6. **`HasSurvivalToolFor()` extension** - KEEP (utility for tool presence checks)

### Next Steps

Proceed to **Phase 11.4 — Strip old invalidation logic** as Phase 11.3 requires no action.
