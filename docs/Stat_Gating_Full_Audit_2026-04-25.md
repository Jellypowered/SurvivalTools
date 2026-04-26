# SurvivalTools Full Audit: Stats, Gating Flow, and Risk Review

Date: 2026-04-25
Scope: Full read-only audit of stat definitions, gating flow, validation/cancel paths, alerts/reporting, and compatibility hooks.
Constraint honored: No code changes made in this audit task.

## 1) Audit Method

This audit was performed by tracing the runtime pipeline through:
- Stat definitions and stat part math
- WorkGiver/Job stat resolution
- Hard-gating authority and rescue flow
- Validation/cancel and alert/reporting paths
- Compatibility integrations (RR and tree authority stack)

Primary files reviewed:
- Source/SurvivalToolUtility.cs
- Source/Helpers/StatGatingHelper.cs
- Source/Helpers/StatFilters.cs
- Source/Gating/JobGate.cs
- Source/Gating/GatingEnforcer.cs
- Source/Helpers/SurvivalToolValidation.cs
- Source/Harmony/WorkGiver_Gates.cs
- Source/Harmony/Patch_WorkGiver_MissingRequiredCapacity.cs
- Source/Alerts/Alert_ColonistNeedsSurvivalTool.cs
- Source/Alerts/Alert_ToolGatedWork.cs
- Source/Scoring/ToolScoring.cs
- Source/Helpers/ScoreCache.cs
- Source/Stats/StatPart_SurvivalTools.cs
- Source/Compatibility/ResearchReinvented/RRHelpers.cs
- Source/Compatibility/ResearchReinvented/RRPatches.cs
- Source/Compatibility/TreeStack/TreeSystemArbiter.cs
- Source/Helpers/PawnEligibility.cs
- Source/Helpers/PawnToolValidator.cs
- Source/SurvivalToolsSettings.cs
- 1.6/Defs/Stats/Stats_Pawn_WorkGeneral.xml

## 2) End-to-End Flow (Current Architecture)

### 2.1 Stat math layer
- Stat values are modified in Source/Stats/StatPart_SurvivalTools.cs.
- Effective tool is selected via ToolScoring.GetBestTool(...), then multiplier applied via ToolStatResolver.GetToolStatFactor(...).
- In Normal mode, no-tool penalty can be applied (settings-driven).
- In Hardcore/Nightmare, hard block is not done in StatPart; block happens in gating.

### 2.2 Gating authority layer
- Source/Harmony/WorkGiver_Gates.cs is the WorkGiver patch gateway.
- Prefixes on HasJobOnThing/HasJobOnCell call JobGate.ShouldBlock(..., queryOnly:true) to avoid side effects during query contexts.
- Postfixes on JobOnThing/JobOnCell call JobGate.ShouldBlock(..., queryOnly:false) as final authority and null blocked jobs.
- Source/Gating/JobGate.cs resolves required stats, filters by ShouldBlockJobForStat, runs tool checks, optionally queues rescue, and blocks/permits.

### 2.3 Enforcement and cleanup
- Source/Gating/GatingEnforcer.cs cancels current blocked jobs and prunes queued blocked jobs using JobGate.ShouldBlock.
- Source/Helpers/SurvivalToolValidation.cs performs delayed/manual validation and may cancel jobs.

### 2.4 Alerts/reporting
- Source/Alerts/Alert_ToolGatedWork.cs: active blocked-work alert using queryOnly JobGate checks, but only for representative work categories.
- Source/Alerts/Alert_ColonistNeedsSurvivalTool.cs: broader missing-tools alert/explanation logic based on assigned tool-relevant work givers.
- Source/Harmony/Patch_WorkGiver_MissingRequiredCapacity.cs: legacy compatibility gate path with debug missing-stat reporting.

### 2.5 Compatibility integrations
- RR: Source/Compatibility/ResearchReinvented/RRHelpers.cs + RRPatches.cs.
- Tree authority arbitration: Source/Compatibility/TreeStack/TreeSystemArbiter.cs.

## 3) Full Stat Inventory and Current Gating Semantics

Stats defined in 1.6/Defs/Stats/Stats_Pawn_WorkGeneral.xml and ST_StatDefOf:
- DiggingSpeed
- MiningYieldDigging
- PlantHarvestingSpeed
- SowingSpeed
- TreeFellingSpeed
- MaintenanceSpeed
- DeconstructionSpeed
- ResearchSpeed
- CleaningSpeed
- MedicalOperationSpeed
- MedicalSurgerySuccessChance
- ButcheryFleshSpeed
- ButcheryFleshEfficiency
- WorkSpeedGlobal
- SurvivalToolCarryCapacity (capacity stat, not a work gate stat)

Current hard-gating behavior (as implemented):
- Core hard-gated families in HC/NM through StatFilters.ShouldBlockJobForMissingStat:
  - DiggingSpeed, TreeFellingSpeed, PlantHarvestingSpeed, SowingSpeed, ConstructionSpeed, MaintenanceSpeed, DeconstructionSpeed, ResearchSpeed, ButcheryFleshSpeed
- Optional-family gate checks in StatGatingHelper.ShouldBlockJobForStat:
  - ButcheryFleshSpeed/ButcheryFleshEfficiency => xhc OR requireButcheryTools
  - MedicalOperationSpeed/MedicalSurgerySuccessChance => xhc OR requireMedicalTools
  - CleaningSpeed => xhc OR requireCleaningTools
- Important: WorkSpeedGlobal is explicitly excluded from hard-gating.

Butchery equivalence currently implemented:
- Source/SurvivalToolUtility.cs HasRequiredToolForStatOrEquivalent:
  - ButcheryFleshSpeed and ButcheryFleshEfficiency are mutually acceptable equivalents.

## 4) Findings (All)

Severity rubric:
- Critical: likely gameplay breakage across common paths
- High: strong behavior mismatch/risk in normal use
- Medium: correctness/maintenance risk with realistic edge conditions
- Low: lower-impact inconsistency or technical debt

---

## Critical Findings

### C1) Cleaning tool hard-gating setting is currently unreachable in StatGatingHelper
- Evidence:
  - Source/Helpers/StatGatingHelper.cs line 32: early return false for CleaningSpeed and WorkSpeedGlobal.
  - Source/Helpers/StatGatingHelper.cs line 53: later branch attempts to gate CleaningSpeed (xhc OR requireCleaningTools).
- Why this is a bug:
  - The early return makes the later CleaningSpeed branch dead code.
  - Result: requireCleaningTools cannot take effect through this central gate function.
- Compatibility impact:
  - Any integration expecting CleaningSpeed hard-gating in extra-hardcore or via requireCleaningTools will not get it.
  - Potentially inconsistent behavior across alerts/UI versus actual block outcomes.
- Safe fix strategy:
  1. Remove or narrow the early-return exclusion for CleaningSpeed.
  2. Keep WorkSpeedGlobal excluded if intended.
  3. Re-test all call paths that rely on ShouldBlockJobForStat.

### C2) Optional-family settings apply in Hardcore, but UI placement implies Extra-Hardcore-only control
- Evidence:
  - Source/Helpers/StatGatingHelper.cs lines 56 and 59: Butchery/Medical optional families gate on xhc OR requireXTools.
  - Source/SurvivalToolsSettings.cs line 905+: optional toggles are drawn under the ExtraHardcore section UI.
  - Source/SurvivalToolsSettings.cs defaults: requireButcheryTools=true, requireMedicalTools=true.
- Why this is a bug/risk:
  - In Hardcore, these settings still affect gating because requireXTools is true by default.
  - But users are guided to these controls only in ExtraHardcore UI section.
  - This creates policy surprise and can look like unintended strictness in Hardcore.
- Compatibility impact:
  - Modpacks that expect Hardcore optional stats to remain bonus-like can observe stricter blocking than expected.
- Safe fix strategy:
  1. Decide policy explicitly: Hardcore optional-family gating ON or OFF by default.
  2. Align UI visibility and descriptions with actual mode behavior.
  3. If intended to be Extra-Hardcore-only, gate by xhc only.

---

## High Findings

### H1) Validation path can diverge from mode intent due to hardcore-only guard
- Evidence:
  - Source/Helpers/SurvivalToolValidation.cs line 88: returns unless settings.hardcoreMode == true.
  - ValidateExistingJobs is called from extra-hardcore enable path too (Source/SurvivalToolsSettings.cs line 913).
- Why this is a risk:
  - Current UI largely enforces ExtraHardcore under Hardcore, so this often works.
  - But this guard is fragile against state drift (save edit, migration anomalies, future UI changes).
  - It is semantically inconsistent with other checks that use (hardcore OR extraHardcore).
- Compatibility impact:
  - Edge-case saves or integrations that set extraHardcoreMode independently may skip expected validation.
- Safe fix strategy:
  1. Change guard to (hardcoreMode || extraHardcoreMode).
  2. Keep behavior otherwise identical.

### H2) Duplicated butchery equivalence logic exists outside central helper (drift risk)
- Evidence:
  - Central helper: Source/SurvivalToolUtility.cs lines 55-73.
  - Local duplicate in Source/Harmony/Patch_WorkGiver_MissingRequiredCapacity.cs lines 46-62.
- Why this is a risk:
  - Family equivalence rules can drift over time if one copy changes and the other does not.
  - Legacy patch then reports or gates differently than primary authority paths.
- Compatibility impact:
  - Hard-to-debug mismatch when other mods hook MissingRequiredCapacity path.
- Safe fix strategy:
  1. Route patch reporting checks through HasRequiredToolForStatOrEquivalent.
  2. Remove local copies of family rules.

### H3) Alert_ToolGatedWork is intentionally narrow and can miss blocked categories
- Evidence:
  - Source/Alerts/Alert_ToolGatedWork.cs uses representative WG checks for mining/construction/plant cutting/plant harvest only.
- Why this is a risk:
  - Pawns blocked in research/medical/butchery/deconstruction-only contexts may not appear in this specific alert.
  - Users may assume no blocks exist while other systems still block jobs.
- Compatibility impact:
  - Modded work categories and compatibility-added work givers are more likely to be under-reported.
- Safe fix strategy:
  1. Either document this alert as sampled/representative.
  2. Or expand it to use assigned tool-relevant work givers dynamically (with throttling).

### H4) Dynamic RR award patching is broad and may over-patch methods
- Evidence:
  - Source/Compatibility/ResearchReinvented/RRPatches.cs lines 465+ scans RR assembly for method names containing Research/Progress and patches those with Pawn+amount signatures.
- Why this is a risk:
  - Broad heuristic patching can affect methods not intended as direct progress award paths.
  - Incompatibility risk rises when RR internals change.
- Compatibility impact:
  - Future RR updates may trigger unintended behavior via overly broad prefixes.
- Safe fix strategy:
  1. Maintain an allowlist of known RR progress methods plus fallback logging for unknowns.
  2. Keep dynamic discovery as opt-in debug mode or guarded path.

---

## Medium Findings

### M1) Eligibility criteria are still distributed across multiple helpers
- Evidence:
  - Source/Helpers/PawnEligibility.cs
  - Source/Helpers/PawnToolValidator.cs
  - Source/SurvivalToolUtility.cs CanUseSurvivalTools extension (line 1250+)
  - Source/Stats/StatPart_SurvivalTools.cs local CanUseSurvivalTools
- Why this is a risk:
  - Not all paths use exactly the same gating eligibility semantics.
  - Future edits can accidentally widen/narrow eligibility in one path only.
- Compatibility impact:
  - Edge cases for prisoners, quest lodgers, drafted states, modded pawn races can differ by path.
- Safe fix strategy:
  1. Consolidate to one authoritative eligibility API with mode/context flags.
  2. Replace distributed checks incrementally.

### M2) Alert_ColonistNeedsSurvivalTool mode checks are coupled to hardcoreMode specifically
- Evidence:
  - Source/Alerts/Alert_ColonistNeedsSurvivalTool.cs line 84 and line 311 use settings.hardcoreMode for branch behavior.
- Why this is a risk:
  - If mode state ever diverges (or future mode model changes), this alert may not match actual gate mode semantics.
- Compatibility impact:
  - Mostly edge-state risk today due UI coupling.
- Safe fix strategy:
  1. Normalize to CurrentMode or (hardcoreMode || extraHardcoreMode).

### M3) Heuristic WG stat mapping remains a compatibility hot spot
- Evidence:
  - Source/Helpers/StatGatingHelper.cs GetStatsForWorkGiver relies on defName heuristics for many paths.
- Why this is a risk:
  - Modded WG naming variations can produce misses or unintended matches.
- Compatibility impact:
  - Third-party work givers can under/over-gate.
- Safe fix strategy:
  1. Prefer explicit WorkGiverExtension requiredStats where feasible.
  2. Keep heuristics as fallback with debug diagnostics.

### M4) Mode policy documentation is inconsistent with behavior in several comments
- Evidence:
  - Multiple comments describe optional stats as bonus-like while current settings/defaults can hard-gate some in Hardcore.
- Why this is a risk:
  - Increases maintenance and support friction.
- Compatibility impact:
  - Integrators infer wrong contract from comments.
- Safe fix strategy:
  1. Update inline comments to exact policy truth table.

---

## Low Findings

### L1) Legacy compatibility patch path still duplicates some gate logic
- Evidence:
  - Source/Harmony/Patch_WorkGiver_MissingRequiredCapacity.cs remains active for compatibility.
- Why this is low impact:
  - Useful as fallback, but duplicated logic increases maintenance burden.
- Safe fix strategy:
  1. Keep patch, but delegate all decision logic to shared utility methods.

### L2) Multiple alert systems overlap with different scopes
- Evidence:
  - Alert_ToolGatedWork and Alert_ColonistNeedsSurvivalTool cover related but different surfaces.
- Why this is low impact:
  - Not a functional break, but can confuse interpretation.
- Safe fix strategy:
  1. Clarify in labels/tooltips that one is "currently blocked work" and one is "general missing tools risk".

## 5) Verified Non-Issues / Rechecks

These were explicitly rechecked and found acceptable in current code:
- queryOnly rescue freeze regression: guard is present (JobGate only queues rescue when !queryOnly, and WorkGiver_Gates prefixes call queryOnly:true).
- Score cache condition invalidation: StatPart_SurvivalTools tracks HP buckets and calls ScoreCache.NotifyToolChanged on bucket change.
- RR dynamic award patching function existence: DiscoverAndPatchRRAwardMethods is implemented (risk is broadness, not absence).

## 6) Decision-Adjusted Actionable Plan (No code in this audit)

This plan reflects maintainer decisions provided after the initial audit.

### 6.1 Agreed policy targets
- CleaningSpeed policy target:
  - Hardcore: user-configurable (required or not required)
  - Extra-Hardcore: mandatory (not optional)
- Validation and alert mode logic should normalize to CurrentMode semantics.
- Butchery equivalence logic should be centralized (no duplicates).
- Alert coverage should include all relevant gated families while preserving low spam.
- Eligibility checks should be consolidated carefully without changing intended behavior.
- RR gating should remain broad enough to preserve current functional coverage, but made safer.

### 6.2 Phase 1: Policy and mode correctness (highest priority)
1. Implement CleaningSpeed gating branch so it is reachable in ShouldBlockJobForStat and matches target policy:
  - HC uses user option
  - XHC always requires cleaning
2. Reconcile C2 implications by aligning policy comments and setting semantics for optional families.
3. Update SurvivalToolValidation mode guard to use CurrentMode-equivalent logic (HC or XHC), not hardcore-only.
4. Normalize Alert_ColonistNeedsSurvivalTool mode branching to CurrentMode-equivalent logic.

Compatibility guardrails:
- Keep WorkSpeedGlobal excluded from hard-gating unless explicitly changed by design.
- Preserve existing save compatibility for settings fields.

### 6.3 Phase 2: Equivalence and duplication cleanup
1. Remove duplicated butchery equivalence logic from legacy compatibility patch paths.
2. Route all required-stat satisfaction checks through a single shared helper.
3. Ensure report/debug strings use the same equivalence outcome as gate authority.

Compatibility guardrails:
- Do not remove legacy compatibility patch itself; delegate logic only.

### 6.4 Phase 3: Alert completeness without spam (H3)
Goal: convey full blocked-work picture without noisy repeated messages.

1. Expand Alert_ToolGatedWork data source from representative categories to all assigned tool-relevant work families.
2. Keep anti-spam mechanisms:
  - Existing sticky window and cooldown behavior
  - Per-pawn dedupe
  - Compact grouped summary by stat family
3. Add a capped explanation format:
  - Top N groups shown
  - Remaining counts summarized

Compatibility guardrails:
- Preserve queryOnly checks in alert paths.
- Avoid rescue/job-queue side effects in all alert computation.

### 6.5 Phase 4: RR broad gating hardening (H4)
Given current broad strategy is required for functional coverage, apply a two-layer approach:

1. Keep broad dynamic discovery as functional safety net.
2. Add explicit allowlist-first patching for known stable RR progress methods.
3. Keep broad scanner fallback behind controlled mode:
  - default on for now to avoid regressions
  - add structured logging/metrics for what fallback patches are actually used
4. Add denylist filters for known non-progress helper methods to reduce over-patch risk.
5. Add runtime diagnostics command/report to print patched RR methods for troubleshooting.

Compatibility guardrails:
- Preserve current effective behavior first; reduce breadth incrementally based on observed telemetry.

### 6.6 Phase 5: Eligibility consolidation (M1)
1. Define one authoritative eligibility contract for gating contexts.
2. Migrate callers gradually:
  - JobGate, GatingEnforcer, Validation, Alerts, StatPart helpers
3. Validate no behavior drift for colonists, slaves, prisoners, quest lodgers, mechs, and modded humanlikes.

Compatibility guardrails:
- Keep context-specific exceptions explicit where needed instead of implicit divergence.

### 6.7 Phase 6: Heuristic mapping improvements (M3)
1. Keep current heuristics as fallback for compatibility.
2. Prefer explicit WorkGiverExtension requiredStats where available.
3. Add diagnostics for unresolved or heuristic-only work givers.
4. Leverage ToolResolver-expanded stat coverage while keeping gating authority independent from item-label heuristics.

### 6.8 Documentation and UX updates (M4, L2)
1. Update comments and settings text to match actual policy truth table.
2. Clarify distinction between:
  - currently blocked work alert
  - general missing-tool risk alert

### 6.9 Recommended execution order
1. Phase 1
2. Phase 2
3. Phase 3
4. Phase 4
5. Phase 5
6. Phase 6
7. Phase 6.8 (documentation and UX updates)

## 7) Runtime Verification Plan

After any fixes, run these targeted tests:
- Butchery with tools that have only one of speed/efficiency, across all gate/report paths.
- Cleaning gating in Extra-Hardcore with requireCleaningTools on/off.
- Hardcore optional-family behavior (medical/butchery efficiency) to ensure matches intended policy/UI.
- Query-only contexts (right-click, scanner checks) to confirm no rescue queue side effects.
- RR progress award paths (bench, chunk/tick, and side-channel methods) with and without tools.
- Alert parity tests: blocked jobs in research/medical/butchery compared between both alert types.

## 8) Executive Summary

The core architecture is strong: centralized JobGate authority, explicit queryOnly handling, and robust scoring/stat-part plumbing.

Main correctness risks are policy/consistency risks rather than catastrophic algorithmic failures:
- One confirmed dead branch for CleaningSpeed gating.
- Mode/policy mismatch around optional families and UI expectations.
- Residual duplicated logic and broad compat patch heuristics that increase integration fragility.

No source files were modified by this audit request.