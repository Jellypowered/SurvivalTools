# Phase 11.14 Part 3: Hot Path Logging Throttling

## Problem Analysis

Many logging calls are in extremely hot paths that execute multiple times per tick per pawn:

### Critical Hot Paths

**1. JobGate.ShouldBlock()** - Called on EVERY WorkGiver scan

- 10-50+ calls per pawn per tick (one per WorkGiver evaluation)
- Early-outs log EVERY time (pawn ineligible, normal mode, toolless job, etc.)
- **Current**: Logs decision on every evaluation
- **Problem**: Generates 1000+ log entries per second in a 10-pawn colony

**2. AssignmentSearch.TryUpgradeFor()** - Tool upgrade checks

- Called frequently during idle/job-start cycles
- Logs at entry, during validation, on every candidate check
- **Current**: 10-15 debug logs per upgrade attempt
- **Problem**: Spams logs during tool switching

**3. PreWork_AutoEquip** - Before every job

- Called before EVERY job start
- Logs parameters, gating checks, results
- **Current**: 5-8 debug logs per job start
- **Problem**: Continuous spam during normal gameplay

**4. ST_WearService** - Tool degradation

- Called on every work action
- Logs every HP reduction
- **Current**: Logs every digging pulse, every consumption
- **Problem**: Mining = 100+ logs per job

**5. GatingEnforcer** - Job cancellation

- Called when jobs are cancelled
- Logs every cancellation and queue pruning
- **Current**: Logs every cancelled job
- **Problem**: Spam during enforcement sweeps

## Solution Strategy

### Tier 1: Use Existing ST_Logging Features

Many hot paths already use cooldown keys but need better strategies:

1. **Per-Pawn-Per-Reason Cooldowns**: Use composite keys like `JobGate.EarlyOut|{pawnID}|{reason}`
2. **Aggregation Keys**: Use ST_Logging's existing buffering/dedup
3. **Conditional Logging**: Only log on state changes or failures

### Tier 2: Add Smarter Throttling

For ultra-hot paths, add:

1. **Per-Pawn Tick Cooldowns**: Don't log same pawn more than once per N ticks
2. **Summary Logging**: Log "Processed 50 evaluations (40 allow, 10 block)" instead of 50 individual lines
3. **State-Change Only**: Only log when outcome changes from last evaluation

### Tier 3: Make Debug Levels

Add granularity levels:

- **Level 0** (Production): Critical errors only
- **Level 1** (Normal Debug): Decisions, failures, state changes
- **Level 2** (Verbose): Every evaluation with cooldowns
- **Level 3** (Ultra-Verbose): Everything, no throttling

## Proposed Changes

### High Priority (Part 3A)

**JobGate.cs:**

- Remove `LogDecisionLine` from every early-out
- Only log when actually blocking (outcome = true)
- Add summary logging for allow decisions (throttled per-pawn)
- Change: 50 logs → 1-5 logs per pawn per tick

**AssignmentSearch.cs:**

- Consolidate entry/exit logging
- Remove intermediate validation step logs
- Only log on success/failure, not every check
- Change: 15 logs → 2-3 logs per upgrade attempt

**PreWork_AutoEquip.cs:**

- Remove parameter logging (redundant)
- Only log on rescue mode or failure
- Change: 8 logs → 1-2 logs per job (only when interesting)

### Medium Priority (Part 3B)

**ST_WearService.cs:**

- Throttle wear pulse logging to once per 100 ticks per pawn-tool pair
- Only log consumption events (tool destroyed)
- Change: 100+ logs → 1-2 logs per tool lifecycle

**GatingEnforcer.cs:**

- Aggregate cancellations into summary
- Only log individual cancels in verbose mode
- Change: 10 logs per enforcement → 1 summary log

**SurvivalToolValidation.cs:**

- Only log validation failures, not every check
- Summary of validation sweep results
- Change: 20 logs per sweep → 1 summary + failures

### Low Priority (Part 3C - Future)

- Right-click rescue logging (already mostly dev-mode gated)
- Mote helper logging (infrequent)
- Tool backing resolution (one-time per session)

## Implementation Plan

### Phase 3A: Critical Hot Path Fixes (This Phase)

1. JobGate.cs - Remove early-out spam
2. AssignmentSearch.cs - Consolidate logs
3. PreWork_AutoEquip.cs - Log only interesting events

### Phase 3B: Secondary Hot Paths (Future)

1. ST_WearService.cs - Throttle wear logging
2. GatingEnforcer.cs - Aggregate cancellations
3. SurvivalToolValidation.cs - Summary logging

### Phase 3C: Optional Polish (Future)

1. Add debug verbosity levels
2. Add summary statistics
3. Performance counters

## Expected Impact

**Before (10-pawn colony, 1 minute):**

- JobGate: ~3000 log entries
- AssignmentSearch: ~500 log entries
- PreWork: ~400 log entries
- **Total: ~4000+ debug log entries per minute**

**After Phase 3A (same scenario):**

- JobGate: ~50 log entries (blocks only)
- AssignmentSearch: ~30 log entries (results only)
- PreWork: ~20 log entries (rescues/failures only)
- **Total: ~100 debug log entries per minute (40x reduction)**

**After Phase 3B (same scenario):**

- ST_WearService: ~10 log entries (consumption only)
- GatingEnforcer: ~5 log entries (summaries)
- **Total: ~50 debug log entries per minute (80x reduction)**

## Testing Strategy

1. Enable debug logging
2. Run colony for 1 in-game day
3. Count log entries by category
4. Verify no important information lost
5. Confirm performance improvement

## Success Criteria

✅ Log volume reduced by 80%+ in typical gameplay
✅ Important events still logged (blocks, failures, rescues)
✅ No spam of repetitive "allow" decisions
✅ Debug mode remains useful for troubleshooting
✅ No performance regression
