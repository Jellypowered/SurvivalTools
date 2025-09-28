# Survival Tools Reborn — Session Summary (Refactor branch)

Date: 2025-09-22
Last updated: 2025-09-25

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

## Example Log Improvements (from Patton scenario)

- `[JobGate] Skipping gate/rescue for disabled or inactive work type Construction on Patton`
- No construction-tool rescues are queued unless the job is forced.

## Phase 9 — Consolidation (In Progress)

Objectives:

- Remove/neutralize legacy auto-pickup & optimizer logic while preserving save / XML compatibility
- Minimize Harmony surface to an explicit allowlist (job start / gear tab only)
- Provide tooling (debug actions) to verify cleanliness post-load
- Safeguard against accidental whole-stack textile destruction (Phase 8 bound consumables) — now observable via debug dump

Implemented so far:

- Legacy forwarders/stubs: `Legacy/LegacyForwarders.cs` supplies inert versions of `JobGiver_OptimizeSurvivalTools` & `AutoToolPickup_UtilityIntegrated` (public signatures intact; bodies inert). Original sources wrapped in `#if ST_LEGACY_PATCHES`.
- PatchGuard sweep extended (planned next): will enforce allowlist: `PreWork_AutoEquip`, `ITab_Gear_ST` only on hotspot methods.
- Added debug actions:
  - "Dump bound consumables → Desktop" (registry state of per-(pawn,stat) rag bindings)
  - "Verify consolidation (patch allowlist)" (ensures only allowlisted patches remain; reports any lingering legacy ones)
- Bound consumables registry exposes `ActiveBindingCount` & structured dump helper for diagnostics.
- Legacy scoring forwarders: `LegacyScoringForwarders.cs` adds obsolete `ToolScoreUtility` (root + Legacy namespace) redirecting to `Scoring.ToolScoring` / `ToolStatResolver`.
- Settings migration: legacy `toolOptimization` / `autoTool` flags persisted but mapped to unified `enableAssignments` with post-load reconciliation.

Pending:

- PatchGuard enhancement (current file present; allowlist refinement pass still to apply for complete consolidation scope)
- XML PatchOps pruning / relocation of obsolete patches to a quarantine directory
- Settings migration & obsolete flag forwarding
- Score forwarders (mark old APIs `[Obsolete]` but forward to new scoring pipeline)
- Documentation finalization with acceptance checklist & risk notes
  (Some items above now done: scoring forwarders, partial settings migration; list will be re-trimmed on final pass.)

### Acceptance Checklist (Phase 9 Consolidation)

Goal: all legacy logic neutralized; only curated patch surface; optional stats not hard-gated.

Runtime / Logs:

- [ ] Startup log shows PatchGuard allowlist summary (only `PreWork_AutoEquip` + `ITab_Gear_ST` hotspots).
- [ ] No warnings about unexpected Harmony owners after running debug action: Verify consolidation (patch allowlist).
- [ ] Optional stat validator ("Validating demoted optional stats...") either silent or reports 0 flagged WorkGivers.
- [ ] No WorkGiver gating messages referencing MiningYieldDigging / ButcheryFleshEfficiency / MedicalSurgerySuccessChance (unless extra-hardcore explicitly enables via settings).

Backward Compatibility:

- [ ] Saves with legacy optimizer still load (no red errors for missing JobGiver / AI classes).
- [ ] External mods calling old `ToolScoreUtility` functions get Obsolete warnings only; behavior matches new scoring.
- [ ] Legacy settings (toolOptimization / autoTool) migrate deterministically to `enableAssignments` (verify in a save that had them diverged).

Bound Consumables / Wear:

- [ ] Debug dump shows <= (colonists \* active gated stats) bindings.
- [ ] No multi-stack deletions; textile parent stacks remain unless originally count=1.

Scoring / Assignment:

- [ ] No upgrade thrash across stats within focus window.
- [ ] Carry-limit=1 pawns keep best tool; no immediate drop/reacquire loops.

Housekeeping:

- [ ] Quarantine directory retains removed XML (historical diff provenance).
- [ ] Quarantined XML uses .off extension (not parsed by RimWorld) to avoid silent side-effects.
- [ ] `summary.md` reflects forwarders + migration rationale.
- [ ] No new warnings introduced (treat Obsolete forwarders as informational only).

### Optional Stat Validator

A new deferred long event now scans all `WorkGiverDef` requirements post-load. If any demoted optional stats are found as _required_ (extension or registry path), a single warning is emitted listing the offending WorkGivers and the stats involved. This guards against third-party patches unintentionally re-hardening efficiency or bonus stats, preserving predictable gating semantics.

Demoted optional stats monitored:

- MiningYieldDigging
- ButcheryFleshEfficiency
- MedicalSurgerySuccessChance

## Phase 9 Final Blurb

Phase 9 finalized: Legacy acquisition logic fully neutralized (public stubs only), Harmony patch surface restricted to two audited patch containers, bonus/efficiency stats demoted out of hard gating with a debug-gated validator, and backward compatibility preserved via scoring + settings forwarders. Bound consumables system safeguards textile stacks with drift-aware unbinding. Quarantined XML renamed with .off extension to guarantee it is not ingested by the def loader. This establishes a lean, auditable foundation for future feature work without legacy patch debt.

Action on warning: Either adjust the external XML to remove those from `requiredStats` (preferred), or explicitly accept the design (no further action needed). No automatic mutation occurs.

Safety / Save Integrity:

- No public type removals — all previously XML-addressable classes remain resolvable (stubs) → avoids red errors on load
- Harmony patch pruning is subtractive and limited to ST-owned legacy patches; does not touch third-party mods
- Registry-based textile wear ensures at most 1 split-off unit per (pawn,stat); parent stack preserved unless fallback path triggered (single-count stack) in which case no hiding occurs

Verification Workflow (recommended):

1. Load an existing save with legacy optimizer active
2. Run "Verify consolidation" debug action → expect only allowlisted patches
3. Perform mining & cleaning work for 2+ in-game days
4. Run "Dump bound consumables" to ensure bindings <= colonists × relevant stats, no runaway growth
5. Observe no duplicate gear rows, no stuck rescue loops, and no entire textile stacks vanishing at 0 HP

Snapshot (current date auto-generated on build completion earlier in session) — section will be updated when PatchGuard allowlist refactor lands.

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

---

## Phase 8 addendum (Sep 2025)

Recent targeted stability/QoL improvements:

- Textiles-only virtual tools

  - VirtualTool eligibility tightened to Fabric-only; leather/wood/apparel/weapons excluded.
  - Gear tab shows a single virtual entry (e.g., Cloth) only if present in inventory; no duplicate stuff rows.
  - Wear service pulses HP on the underlying textile stack; no comps required; idempotent 60-tick throttle.

- Pure-delivery WorkGivers are never gated

  - Central predicate `JobGate.IsPureDeliveryWorkGiver` exempts resource delivery (blueprints/frames) from rescue/gating.
  - PreWork auto-equip also respects this exemption to avoid churn.

- Optional stat handling to prevent hard blocks

  - MiningYieldDigging and other “bonus” stats treated as optional during gating.
  - JobGate filters declared stats through `StatGatingHelper` before deciding to block.

- Unified decision logging

  - Every JobGate exit logs a single compact line: `Decision: ALLOW|BLOCK | pawn=… | ctx=WG:…/Job:… | forced=… | reason=…`.
  - Queue summaries available on cooldown for fast diagnostics.

- Drops prefer storage/home and enqueue hauling
  - All tool drops are unforbidden.
  - Prefer storage cell; otherwise home-area cell; enqueue HaulToCell when needed.

Quality gates: Build PASS, artifacts mirrored and zipped; no Harmony target churn from these changes.

Commit refs

- Start: 886d91acdf953b11aeecd03ab698cf1253a6ab04
- Acceptance: b7d76f9

Files touched (Phase 8)

- Source/AI/JobDriver_DropSurvivalTool.cs — controlled drops (storage/home-first, unforbid, haul enqueue)
- Source/Assign/AssignmentSearch.cs — rescue semantics, requeue logic, cooldowned queue summaries
- Source/Gating/GatingEnforcer.cs — cancel/prune invalid jobs on mode changes; compact queue snapshots
- Source/Gating/JobGate.cs — optional-stat filtering, pure-delivery exemption, acquisition-in-motion allowance, unified decision logging
- Source/Helpers/ST_Logging.cs — queue summary helper with cooldown/dedup
- Source/Helpers/StatFilters.cs — marks MiningYieldDigging as optional
- Source/Helpers/ToolStatResolver.cs — textiles-only virtual candidates
- Source/UI/GearTab_ST.cs — dedup virtuals; hide raw tool-stuff rows
- docs/summary.md — this addendum

Artifacts

- 1.6/Assemblies/SurvivalTools.dll updated
- Survival Tools Reborn_1.6-Debug.zip generated

### Also in Phase 8

- Rescue-first gating behavior
  - If acquisition is already pending/queued, JobGate allows immediately; otherwise, after rescue is queued it blocks until acquisition starts.
- PreWork ping-pong suppression
  - Management cooldown after tool actions; requeue only when a new acquisition was actually enqueued; avoids repeated rescues and job churn.
- Carry-limit and “real tool” handling
  - Treat tool-stuff as a single virtual tool for carry checks; prefer keeping best-for-focus tool when limit is effectively one.
- Centralized wear/degrade
  - All degrade calls route through `ST_WearService` (including virtuals) with deterministic, throttled pulses.
  - Hotfix: Virtual textile wear now binds a single-unit consumable per (pawn,stat). A 1-count "rag" is split off and degraded; parent stack never deleted wholesale. Registry: `ST_BoundConsumables`.
  - Gear tab shows only the degrading bound unit (parent stack hidden while bound) preventing duplicate virtual entries.
  - Debug pulses for DiggingSpeed produce one cooldowned line per (pawn,tool) bucket.
- Mode-change enforcement
  - `GatingEnforcer` can cancel/prune now-invalid jobs when switching difficulty modes or on load; logs compact queue snapshots on cooldown.
