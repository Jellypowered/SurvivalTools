# Survival Tools Reborn — Session Summary (Refactor branch)

Date: 2025-09-22

## Overview
This session focused on stabilizing and refining the Harmony "brain" for Survival Tools Reborn: deterministic tool upgrades before work, gating by difficulty, preserving original jobs, and reducing churn/log noise. We fixed edge cases (notably a queued-start NullReference and construction rescues for unassigned pawns), tightened gating rules, and centralized/resilient logging.

## Problems Observed
- Rescue/upgrade triggered for a pawn not assigned to a work type (e.g., Patton rescuing a hammer while Construction priority was 0).
- Occasional NullReferenceException when starting a queued acquisition job (equip/take-inventory).
- Log spam:
  - Repeated focus-block entries.
  - Large, verbose job queue dumps.
  - Duplicated acquisition/drop entries.
- Ping-pong with carry limit 1: just-acquired tools being dropped immediately.
- Risk of losing the original job when auto-equipping before work.
- Build warnings:
  - Obsolete API usage in `JobGate` (CompatAPI forwarder).
  - Unused field in `PreWork_AutoEquip`.

## What We Tried (and what didn’t work)
- Gating only for disabled work types: prevented some rescues, but still allowed rescues for unassigned (priority 0) work types.
- Starting acquisition jobs directly from the queue without cloning: could lead to race conditions and a rare NullReference when list mutated.
- Printing full job queues every time: useful for debugging, but produced excessive noise with long queues.
- Threshold-only rescue logic: without a short “focus” window, cross-stat upgrades could thrash.

## Final Solutions Implemented

### 1) Skip rescue/gating for unassigned work
- Tighten early-out in `JobGate.ShouldBlock` to skip rescue and gating when:
  - Work type is disabled for the pawn, OR
  - Work type is not active (`WorkIsActive` is false), AND
  - The action isn’t forced.
- Mirror the same guard in `PreWork_AutoEquip` WorkGiver prefix to avoid opportunistic rescues.
- Result: No more construction rescues for pawns unassigned to Construction.

Files:
- `Source/Gating/JobGate.cs`
- `Source/Assign/PreWork_AutoEquip.cs`

### 2) Preserve original jobs across auto-equip
- Pre-work hooks requeue the original job at the front when an upgrade is queued, then block the start so equip jobs run first.
- Applied to both ordered (`TryTakeOrderedJob`) and AI (`StartJob`) paths; skips tool-management jobs to avoid loops.

Files:
- `Source/Assign/PreWork_AutoEquip.cs`

### 3) Harden queued-start helper (NullReference fix)
- Snapshot matching queued jobs before starting one, guard `targetA` access, and purge duplicates after a job starts.
- Applies to both acquisition (`Equip`/`TakeInventory`) and drop jobs (`DropSurvivalTool`/`DropEquipment`).

Files:
- `Source/Assign/AssignmentSearch.cs` (method: `TryStartQueuedToolJobFor`)

### 4) Reduce log spam and make diagnostics useful
- Focus-block logging now on cooldown per pawn+stat key.
- Job queue dumps capped to a max of 20 entries with a “+N more” suffix.
- Added queued-job dedupe checks before enqueue; remove duplicates after start.

Files:
- `Source/Assign/AssignmentSearch.cs`
- `Source/Helpers/ST_Logging.cs` (cooldowns/dedup already in place and used)

### 5) Avoid ping-pong at carry-limit 1
- Added a short “recent acquisition” protection window to prevent immediate drops of the just-acquired tool.
- When carry-limit is effectively 1, drop the worst tool while protecting the best-for-focus stat tool (`FindWorstCarriedToolRespectingFocus`).

Files:
- `Source/Assign/AssignmentSearch.cs`

### 6) Deterministic, LINQ-light tool search and scoring
- Deterministic scoring order with pooled buffers (`_candidateBuffer`, `_inventoryBuffer`, `_stockpileBuffer`).
- Hysteresis window to require extra gain for re-upgrading the same tool.
- Short focus window per stat to avoid cross-stat thrashing.

Files:
- `Source/Assign/AssignmentSearch.cs`
- `Source/Scoring/*` (pre-existing APIs used)

### 7) Centralize WorkGiver → stat mapping and remove obsolete API warning
- Switched `JobGate` to use `StatGatingHelper.GetStatsForWorkGiver(wg)` instead of the obsolete `CompatAPI` forwarder.
- Updated debug message to reflect resolver usage.

Files:
- `Source/Gating/JobGate.cs`
- `Source/Helpers/StatGatingHelper.cs` (mapping logic)

### 8) Clean up build warnings
- Removed unused `_patchApplied` field from `PreWork_AutoEquip`.
- Eliminated obsolete API usage warning in `JobGate`.

Files:
- `Source/Assign/PreWork_AutoEquip.cs`
- `Source/Gating/JobGate.cs`

## Notable Behavior Changes
- Gating/rescue won’t trigger for disabled or unassigned work types unless the user forces the job.
- Player orders are preserved: if an upgrade is needed, the original job is put back at the front of the queue.
- Reduced log noise while keeping actionable, cooldowned diagnostics.

## Example Log Improvements (from Patton scenario)
- Before: Patton (Construction priority 0) repeatedly attempted to grab a hammer while mining.
- After: Log now shows a single cooldowned line such as:
  - `[JobGate] Skipping gate/rescue for disabled or inactive work type Construction on Patton`
  - No construction-tool rescues are queued unless the job is forced.

## Quality gates
- Build: PASS (Debug), warnings addressed.
- Packaging: DLL mirrored to Mods path, ZIP generated.
- Runtime smoke: Log lines reflect skip behavior for inactive work types; no observed NRE from queued-start.

## Files touched (high-level)
- `Source/Assign/AssignmentSearch.cs` — focus/hysteresis, dedupe, queued-start hardening, carry-limit handling, logging caps/cooldowns.
- `Source/Assign/PreWork_AutoEquip.cs` — ordered/AI job preservation, WG skip for inactive/disabled, selective logging.
- `Source/Gating/JobGate.cs` — skip inactive/disabled, use resolver mapping, rescue-first allow-job behavior.
- `Source/Helpers/StatGatingHelper.cs` — WG→stat mapping used by JobGate.
- `Source/Helpers/ST_Logging.cs` — leveraged cooldown/dedup; no functional changes required this session.

## Follow-ups
- Field validation under larger mod stacks to catch rare edge cases.
- Consider demoting some PreWork initialization messages from Warning to Debug to reduce baseline noise.
- Evaluate removing/locking down `CompatAPI` forwarders after refactor stabilizes.

---
This summary captures the intent, attempts, and concrete fixes from this session to improve determinism, preserve jobs, avoid inappropriate rescues, and keep logs readable while retaining visibility into the upgrade pipeline.
