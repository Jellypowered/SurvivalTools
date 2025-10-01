# Phase 11.2 — Remove Duplicate Stat Injectors

## Completion Summary

### Goal

Single source of truth for stat math: `StatPart_SurvivalTools` (and `RRStatPart` for RR). Delete/disable any legacy stat factoring.

### Investigation Results

✅ **NO DUPLICATE STAT INJECTORS FOUND**

The stat injection consolidation was **already completed in Phase 4**. All stat modifications flow through the StatPart system exclusively:

#### Current Stat Injection Architecture (Correct & Complete)

1. **`StatPart_SurvivalTools`** (`Source/Stats/StatPart_SurvivalTools.cs`)

   - **Single source of truth** for all vanilla work stat bonuses/penalties
   - Handles all supported stats: Mining, Construction, Planting, Harvesting, Research, Cleaning, Medical, etc.
   - Uses `ToolScoring.GetBestTool()` for deterministic tool selection
   - Applies `ToolStatResolver.GetToolStatFactor()` for final multipliers
   - Includes Normal mode penalty logic (`settings.noToolStatFactorNormal`)
   - Zero allocations in hot path

2. **`StatPart_RR_NoToolPenalty`** (`Source/Compatibility/ResearchReinvented/RRStatPart.cs`)
   - **Only applies to Research Reinvented compatibility**
   - Only affects `ResearchSpeed` stat
   - Only applies penalty in Normal mode when pawn has no research tool
   - Does NOT duplicate `StatPart_SurvivalTools` logic (different stat, different condition)

#### Verification of No Duplicates

**✅ No Harmony patches modifying stats:**

- Searched for `Postfix.*ref float`, `Transpiler.*GetStatValue`, `Patch.*GetStatValue`
- Only found `Patch_ThingDef_SpecialDisplayStats` which displays stat info in UI (not modification)
- No legacy stat injection patches found

**✅ No legacy stat multipliers:**

- Searched for legacy stat factor code in `Source/Legacy/`
- Only found optimizer/auto-pickup stubs (already handled in Phase 11.1)
- No stat calculation code in legacy folder

**✅ No per-WorkGiver stat injection:**

- WorkGiver patches (`Patch_WorkGiver_*`) only do job gating, not stat modification
- They check for tool availability via `JobGate.ShouldBlock()`, don't multiply stats

**✅ No float math outside StatPart:**

- All tool factor calculations route through:
  - `ToolStatResolver.GetToolStatFactor()` → used by StatPart
  - `SurvivalToolUtility.GetToolProvidedFactor()` → used by resolver
  - `ToolFactorCache` → caching layer for resolver
- These are support utilities FOR the StatPart, not duplicate injection points

**✅ Semantic search confirmed:**

- No matches for "Harmony patch that modifies stat values outside of StatPart"

### Architecture Validation

#### Correct Flow (Current State)

```
Pawn.GetStatValue(workStat)
  → StatDef.Worker.GetValueUnfinalized()
    → StatPart_SurvivalTools.TransformValue()  ← SINGLE INJECTION POINT
      → ToolScoring.GetBestTool()
      → ToolStatResolver.GetToolStatFactor()
        → ToolFactorCache.GetOrComputeToolFactors()
          → SurvivalToolUtility helper methods
    → [Other vanilla StatParts]
  → Final value
```

#### No Legacy Duplicates

- ✅ No direct `GetStatValue()` multiplication outside StatPart
- ✅ No job-level stat overrides
- ✅ No WorkGiver-specific stat bonuses
- ✅ No transpilers injecting stat math
- ✅ No postfixes multiplying values

### Phase 4 Evidence

From `docs/RefactorPlan.md`:

> **Phase 4 — StatPart as single math path** > **Goal:** all bonuses/penalties come from `StatPart_SurvivalTool` for vanilla work stats.

From `docs/DesignTheory.md`:

> **Phase 4 — StatPart (single math path)**
> StatPart_SurvivalTools is now the only way bonuses/penalties enter vanilla stat math.

From `Source/Stats/StatPart_SurvivalTools.cs` header:

> Phase 4: StatPart as the single math path for survival tool bonuses/penalties.
> Uses ToolScoring and ScoreCache for deterministic, cache-friendly calculations.

### Acceptance Criteria

#### ✅ Tool explanations (Inspect pane) unchanged

- `StatPart_SurvivalTools.ExplanationPart()` provides all tool-related stat explanations
- No changes needed

#### ✅ Speed/penalty numbers unchanged

- No stat injection code to modify
- Current behavior is correct

#### ✅ No double-counting

- Only one StatPart per stat type
- `StatPart_RR_NoToolPenalty` only affects ResearchSpeed (non-overlapping with StatPart_SurvivalTools)

### Conclusion

**Phase 11.2 is already complete.** The duplicate stat injector removal was accomplished in Phase 4 of the refactor. The current architecture has:

- ✅ **Single source of truth:** `StatPart_SurvivalTools`
- ✅ **No legacy stat patches:** All removed or disabled
- ✅ **No duplicate math:** All calculations consolidated
- ✅ **Correct separation:** RR compatibility has its own non-overlapping StatPart

**No changes needed for Phase 11.2.**

### Flag Status

```csharp
public const bool STRIP_11_2_DUP_STAT_INJECTORS = false;
```

**Recommendation:** Can be set to `true` immediately since there are no duplicate stat injectors to strip. The flag can remain as a placeholder for potential future cleanup or documentation purposes, but no code guards are needed.

### Next Steps

Proceed to **Phase 11.3 — Strip miscellaneous WorkGiver gates** as Phase 11.2 requires no action.
