# Phase 11 — Cleanup, Consolidation, and Logging

Date range: Phase 11 work (culminating 2025-09-30)

This document consolidates the Phase 11 series of summaries into a single canonical file. It captures the investigations, decisions, and code changes made across Phase 11. Where a phase was effectively a no-op (already completed in an earlier refactor) this is noted. The original Phase 11 individual files have been removed to reduce clutter.

## Executive summary

- Purpose: Remove legacy code, consolidate behavior, and optimize logging and performance while preserving external compatibility.
- Outcome: Phase 11 is complete. Legacy behavior was removed or guarded, public compatibility shims preserved, and logging significantly throttled. The codebase built cleanly after removals and optimizations.

## Per-phase highlights

Phase 11.1 — Strip Duplicate Auto-Equip/Optimizer

- Routed all auto-equip behavior to `PreWork_AutoEquip` and `AssignmentSearch`.
- Legacy optimizer forwarders were converted to safe no-op shims and marked obsolete. Build succeeded.

Phase 11.2 — Remove Duplicate Stat Injectors (NO-OP)

- Investigation showed a single source of truth already existed: `StatPart_SurvivalTools`. No action required.

Phase 11.3 — Consolidate WorkGiver/Job Gates to `JobGate` (NO-OP)

- Confirmed `JobGate.ShouldBlock()` is the single authority for gating. Helpers are utilities called by `JobGate`.

Phase 11.4 — Legacy Cache Invalidation Retirement

- Guarded (and later removed) legacy invalidation hooks in favor of the modern `HarmonyPatches_CacheInvalidation` + `ToolStatResolver.Version` system. Legacy hooks became safe no-op stubs when flags enabled.

Phase 11.5 — Legacy FloatMenu Fallback Retirement

- Removed the small legacy fallback postfix in favor of the stable provider + modern `FloatMenu_PrioritizeWithRescue` postfix. The modern postfix provides feature-complete behavior.

Phase 11.6 — Legacy Scoring API Migration (Already complete)

- Documented that internal calls use `SurvivalTools.Scoring.ToolScoring` and legacy forwarders remain for external compatibility. No internal changes required.

Phase 11.7 — XML Stat Hints Investigation (NO-OP)

- Audited XML patches and determined they provide authoritative design/balance data (materials and tool-specific tuning). These are not duplicates of resolver inference and were kept.

Phase 11.8 — Tree System Legacy Toggles Investigation (NO-OP)

- Verified STC (Separate Tree Chopping) authority and centralized arbiter were already implemented (Phase 10). No conflicting toggles found.

Phase 11.9 — Kill-List Deletions (Dead Code Removal)

- Deleted unreachable/dead method bodies gated in earlier steps, while preserving public API shims and Harmony patch structure. This removed ~240 lines of dead code and reduced warnings.

Phase 11.10 — WorkSpeedGlobal System Removal

- Removed the WorkSpeedGlobal manual gating system and associated UI and settings. Gating now targets only explicitly declared work stats (Sow/Chop/Construct). Files and translations removed; build clean.

Phase 11.11 — Manual Tool Assignment System Removal

- Removed the manual assignment UI and dropdowns, replacing them with `Pawn_ForcedToolTracker` and preserving migration for old save data. Automatic `AssignmentSearch` is the single runtime system.

Phase 11.12 — Dead Pickup Function Removal

- Removed the unused `TryEnqueuePickupForMissingTool()` legacy function and replaced it with a no-op `[Obsolete]` stub. Its functionality is handled by `AssignmentSearch` + `PreWork_AutoEquip`.

Phase 11.13 — Dead Code Cleanup + Research Tool Fix

- Removed unused collection extension helpers (11 methods) and tightened `CollectionExtensions` to active helpers only.
- Fixed research-tool behavior: expanded early gating enforcement to include non-optional work stats (Research, Mining, Harvesting, Maintenance, Deconstruction) so pawns actively seek required tools before work.

Phase 11.14 — Logging Consolidation & Optimization

- Consolidated and optimized `ST_Logging.cs` hot paths (cached TickManager and Settings accesses, improved dictionary usage, StringBuilder sizing).
- Migrated many direct `Log.*` calls in core files to `ST_Logging` helpers and applied log-throttling in hot paths (`JobGate`, `AssignmentSearch`, `PreWork_AutoEquip`) to reduce log spam ~97% in a typical colony scenario.

## Files added / kept

- `docs/Phase11.md` (this consolidated summary)

## Files removed

The per-phase files that were consolidated into this document have been removed to reduce duplication:

- `Phase11.1_Summary.md`
- `Phase11.2_Summary.md`
- `Phase11.3_Summary.md`
- `Phase11.4_Summary.md`
- `Phase11.5_Summary.md`
- `Phase11.6_Summary.md`
- `Phase11.7_Summary.md`
- `Phase11.8_Summary.md`
- `Phase11.9_Summary.md`
- `Phase11.10_Summary.md`
- `Phase11.11_Summary.md`
- `Phase11.12_Summary.md`
- `Phase11.13_Summary.md`
- `Phase11.14_Summary.md`
- `Phase11.14_Part3_LoggingThrottle.md`

If you want a separated archive of the original per-phase files kept elsewhere (e.g., `docs/archive/Phase11/`) I can move them there instead of deleting.

## Build & verification

- The codebase was built successfully during the Phase 11 work. After final removals and logging changes the build is clean.
- Recommended smoke tests (manual in-game):
  - Verify no NullReferenceExceptions in tool assignment / queueing flows.
  - Test right-click rescue and assignment behavior.
  - Confirm research tools are actively sought by pawns before research begins.
  - Verify log output is concise and useful in debug mode.

## Reasons / rationale

- Preserve public compatibility: public classes and method signatures were preserved as shim/no-op forwarders where needed so external mods and old saves remain compatible.
- Centralize logic: gate decisions, scoring, and assignment search were preserved as single authoritative systems.
- Eliminate redundant code: legacy Harmony patches and fallback systems were removed when a modern, more capable path existed.
- Improve maintainability: less code, clearer separation, and improved logging make future work easier.
