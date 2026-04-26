# SurvivalTools Implementation Tickets: Stat Gating

Date: 2026-04-25
Baseline audit: docs/Stat_Gating_Full_Audit_2026-04-25.md
Purpose: Execution checklist for implementation with compatibility safety and rollback criteria.

## Ticket 1: CleaningSpeed Policy Fix (HC configurable, XHC mandatory)

Scope:
- Make CleaningSpeed gate path reachable and policy-accurate.

Primary files:
- Source/Helpers/StatGatingHelper.cs
- Source/SurvivalToolsSettings.cs
- Source/Alerts/Alert_ColonistNeedsSurvivalTool.cs

Required behavior:
- Hardcore: cleaning requirement controlled by setting.
- Extra-Hardcore: cleaning is mandatory.
- WorkSpeedGlobal remains non-hard-gated unless explicitly changed.

Acceptance criteria:
- Cleaning jobs block/unblock exactly per mode/policy above.
- No regressions in non-cleaning stats.

Rollback criteria:
- Any unexpected broad blocking in normal/high-frequency work jobs.

## Ticket 2: Mode Normalization for Validation and Alerts

Scope:
- Replace hardcore-only mode checks with unified CurrentMode semantics.

Primary files:
- Source/Helpers/SurvivalToolValidation.cs
- Source/Alerts/Alert_ColonistNeedsSurvivalTool.cs

Required behavior:
- Validation and alert mode gates behave consistently for Normal/HC/XHC.

Acceptance criteria:
- Enabling XHC always triggers expected validation behavior.
- Alert behavior matches actual gate mode.

Rollback criteria:
- Save-state edge cases cause unexpected job cancellations.

## Ticket 3: Equivalence Consolidation (Remove Drift Risk)

Scope:
- Remove duplicate butchery equivalence logic in legacy patch paths.
- Route all required-stat satisfaction checks through shared helper.

Primary files:
- Source/SurvivalToolUtility.cs
- Source/Harmony/Patch_WorkGiver_MissingRequiredCapacity.cs
- Source/Gating/JobGate.cs
- Source/Helpers/SurvivalToolValidation.cs

Required behavior:
- Gate/report/debug all agree on butchery equivalence outcomes.

Acceptance criteria:
- No path reports missing butchery tool when equivalent stat tool is valid.

Rollback criteria:
- Any divergence between JobGate outcome and debug/report strings.

## Ticket 4: Alert Coverage Expansion Without Spam

Scope:
- Expand blocked-work visibility beyond representative categories while preserving anti-spam behavior.

Primary files:
- Source/Alerts/Alert_ToolGatedWork.cs
- Source/Alerts/Alert_ColonistNeedsSurvivalTool.cs

Required behavior:
- All relevant blocked work families can be surfaced.
- Stickiness, dedupe, and capped summaries prevent spam.

Acceptance criteria:
- Research/medical/butchery/deconstruction blocks are represented.
- Alert text remains compact and stable under heavy load.

Rollback criteria:
- Noticeable alert flicker or spam in high-colonist colonies.

## Ticket 5: RR Gating Hardening (Keep Broad Coverage)

Scope:
- Keep functional broad gating while reducing over-patch risk.

Primary files:
- Source/Compatibility/ResearchReinvented/RRPatches.cs
- Source/Compatibility/ResearchReinvented/RRHelpers.cs

Required behavior:
- Allowlist-first for known methods.
- Controlled fallback dynamic scan retained for compatibility.
- Structured diagnostics for patched method list.

Acceptance criteria:
- No regression in current RR gating effectiveness.
- Fallback usage is observable in logs/diagnostics.

Rollback criteria:
- RR progress bypass appears after tightening logic.

## Ticket 6: Eligibility Contract Consolidation

Scope:
- Define one authoritative pawn eligibility contract and migrate call sites carefully.

Primary files:
- Source/Helpers/PawnEligibility.cs
- Source/Helpers/PawnToolValidator.cs
- Source/SurvivalToolUtility.cs
- Source/Stats/StatPart_SurvivalTools.cs
- Source/Gating/JobGate.cs
- Source/Gating/GatingEnforcer.cs

Required behavior:
- No functional drift for colonists/slaves/prisoners/quest lodgers/mechs.

Acceptance criteria:
- All major gating/alert/validation paths call unified eligibility contract.

Rollback criteria:
- Any change in allowed pawn cohorts without explicit design approval.

## Ticket 7: WorkGiver Mapping Reliability Improvements

Scope:
- Keep heuristic fallback but improve explicit mapping and diagnostics.

Primary files:
- Source/Helpers/StatGatingHelper.cs
- Source/ModExtensions/WorkGiverExtension.cs
- 1.6/Defs/WorkGiverDefs/*.xml

Required behavior:
- Prefer explicit requiredStats where possible.
- Heuristic-only resolution is visible in diagnostics.

Acceptance criteria:
- Core vanilla + key modded work givers map deterministically.

Rollback criteria:
- Loss of gating on known work givers after mapping changes.

## Ticket 8: Docs and UX Contract Alignment

Scope:
- Align comments/tooltips/labels to real policy and alert scopes.

Primary files:
- Source/SurvivalToolsSettings.cs
- docs/Stat_Gating_Full_Audit_2026-04-25.md
- docs/GatingBugFixes.md

Required behavior:
- User-facing text matches exact mode policy and alert semantics.
- User-facing text documents bounded HC/XHC auto-enhanced tool tech-multiplier sliders (current values are minimum floors, with capped upper bounds).
- Notes include runtime caveat that already-resolved defs may need reload/restart to fully reflect changed multipliers.

Acceptance criteria:
- No contradictory policy statements remain in updated docs/comments.
- New tuning controls are described consistently across settings text and docs.

## Cross-Ticket Validation Matrix

Run after each ticket and full pass:
1. Butchery equivalence across gate/report/alert paths.
2. Cleaning policy in HC vs XHC.
3. Query-only contexts never queue rescue.
4. RR bench/tick/chunk/side-channel behavior with and without tool.
5. Alert parity between blocked-work and general missing-tool alerts.
6. Build and smoke test in a large colony save with compat mods.

## Suggested Commit Strategy

1. Ticket 1 + 2 (policy/mode) in one commit.
2. Ticket 3 (equivalence consolidation) in one commit.
3. Ticket 4 (alert expansion) in one commit.
4. Ticket 5 (RR hardening) in one commit.
5. Ticket 6 + 7 + 8 (consolidation/docs) in one commit.

This allows fast bisect and safe rollback at each milestone.
