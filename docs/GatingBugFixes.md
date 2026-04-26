# Gating Bug Fixes — April 2025

## Summary

Two related bugs caused pawns to perform tool-gated work without having the correct tool equipped. Both were in the tool scoring/validation pipeline.

---

## Bug 1 — `ShouldBlockJobForStat` false-allowed via `HasSurvivalToolFor`

**File:** `Source/Helpers/StatGatingHelper.cs`  
**Symptom:** Pawns in Hardcore/Nightmare mode could start tool-gated jobs (e.g. plant harvesting) despite having no valid tool. The gate appeared to pass even when no sickle/scythe was available.

**Root Cause:**  
`ShouldBlockJobForStat` was calling `pawn.HasSurvivalToolFor(stat)` internally and returning `false` (allow) when that returned `true`. `HasSurvivalToolFor` uses `GetToolProvidedFactor`, which — in Normal mode — returns the no-tool baseline (0.4f) for any stat a tool doesn't explicitly cover. Since baseline > 0, any carried item could satisfy any stat check.

**Fix:**  
Removed the `pawn.HasSurvivalToolFor(stat)` call from all branches of `ShouldBlockJobForStat`. The function now returns a pure boolean based on settings + stat type only. Callers (`JobGate`, `PreWork_AutoEquip`) are responsible for the actual tool-presence check via `ToolScoring.GetBestTool`.

---

## Bug 2 — `ToolStatResolver.CreateDefaultInfo` returned no-tool baseline (0.4f) for unrelated tools

**File:** `Source/Helpers/ToolStatResolver.cs`  
**Symptom:** A steel pickaxe scored 0.272 for `PlantHarvestingSpeed`, allowing it to satisfy the sickle gate. Log evidence: `[PreWork.EarlyGate] hasTool=True (best=steel pickaxe (normal 36%) score=0.272 baseline=0)`.

**Root Cause:**  
`CreateDefaultInfo` — the fallback when no explicit, statBases, or name-hint match is found — returned `GetNoToolBaseline()` (0.4f in Normal mode) as the factor. In Hardcore mode `GetToolValidationBaseline` returns 0f, so `0.4f > 0f` meant every carried item appeared to satisfy every stat gate. The pickaxe was falling through to `CreateDefaultInfo` for `PlantHarvestingSpeed` (no sickle rule matches it) and getting 0.4f, which after condition scaling (36% HP) produced 0.272.

**Fix (two parts):**

1. `CreateDefaultInfo` now returns `0f`. An unrelated tool contributes nothing to stats it doesn't cover. The 0.4f no-tool penalty is a `StatPart` applied when working bare-handed — it must not appear as a tool score.

2. The Normal-mode baseline clamp in `GetToolStatFactor` now has an additional guard: it only elevates the factor when `info.Source != "Default"` and `factor > 0f`. This prevents re-introducing the same problem in Normal mode by clamping `0f → 0.4f` for unrelated tools.

---

## Validation

After both fixes, logs should show:

```
[PreWork.EarlyGate] pawn=Fu job=CutPlantDesignated workStat=PlantHarvestingSpeed
  shouldGate=True hasTool=False (best=<null> score=0 baseline=0)
→ CANCEL
```

A steel pickaxe should score 0 for `PlantHarvestingSpeed`. Only sickles/scythes (with an explicit `baseWorkStatFactors` entry or matching the name-hint rule) should score > 0.

---

## Diagnostic Logging

A new debug setting **"Verbose gating logs"** (`debugGatingVerbose`) was added under **Settings → Debug → Debug Logging**. When enabled (requires `debugLogging` also on), it activates `IsGatingLoggingEnabled` which gates all step-by-step logs in:

- `JobGate.cs` — `[JobGate.Enter]`, `[JobGate.Stats]`, `[JobGate.Required]`, `[JobGate.PreCheck]`, `[JobGate.PreSummary]`, `[JobGate.FinalCheck]`, `[JobGate.JLUT]`, `[JobGate] Decision`
- `PreWork_AutoEquip.cs` — `[PreWork.Enter2]`, `[PreWork.JobUsesTools]`, `[PreWork.EarlyGate]`
- `GatingEnforcer.cs` — `[GatingEnforcer]`, `[GatingEnforcer.CancelIfBlocked]`

These logs emit once per `ShouldBlock` call, per pawn, per stat. **They are very noisy in normal gameplay** — only enable when diagnosing gating bugs.

The previous behaviour was that these logs were gated by the general `debugLogging` flag, which also controls many other systems. The new separate toggle allows precise control.

---

## Fixes — April 2026 (Stat Gating Audit Tickets 1–8)

### Ticket 1 — CleaningSpeed gating restored

**File:** `Source/Helpers/StatGatingHelper.cs`  
`CleaningSpeed` was in an early `return false` guard inside `ShouldBlockJobForStat`, making the optional-family check at the bottom of the method unreachable. Removed `CleaningSpeed` from the early return so it now falls through to the `return xhc || settings.requireCleaningTools` policy check. Result: HC respects `requireCleaningTools` toggle; XHC makes cleaning mandatory.

### Ticket 2 — Mode normalization (HC/XHC)

**Files:** `Source/Helpers/SurvivalToolValidation.cs`, `Source/Alerts/Alert_ColonistNeedsSurvivalTool.cs`  
Multiple places checked `settings.hardcoreMode` but not `settings.extraHardcoreMode`, so XHC-only mode (extraHardcoreMode=true, hardcoreMode=false) silently skipped validation and alerts. All mode-discriminating checks updated to use `settings.CurrentMode` comparisons (`DifficultyMode.Normal / Hardcore / Nightmare`).

### Ticket 3 — Butchery equivalence consolidation

**File:** `Source/Harmony/Patch_WorkGiver_MissingRequiredCapacity.cs`  
Inline `HasToolForRequiredStatOrEquivalent` local function duplicated butchery equivalence logic (accepts either `ButcheryFleshSpeed` or `ButcheryFleshEfficiency`). Removed and routed to `SurvivalToolUtility.HasRequiredToolForStatOrEquivalent` so all gating/reporting paths agree.

### Ticket 4 — Alert coverage expanded

**File:** `Source/Alerts/Alert_ToolGatedWork.cs`  
Added Research, Deconstruction, Medical, and Butchery work-type checks. Each uses a `Has*Work()` guard before scanning to prevent noise when there is nothing available to do. Translation keys added to `1.6/Languages/English/keyed/ST_Gating.xml`.

### Ticket 5 — Research Reinvented gating hardening

**File:** `Source/Compatibility/ResearchReinvented/RRPatches.cs`  
Dynamic RR method scan now has an explicit allowlist of already-explicitly-patched methods to prevent double-patching. A `_dynamicPatchedMethods` list tracks fallback-patched methods; count and names are logged at compat-log verbosity. `RRDebug.DumpRRDynamicPatchList()` added for diagnostics.

### Ticket 6 — Eligibility contract consolidation

**File:** `Source/Alerts/Alert_ToolGatedWork.cs`  
`UpdateGatedPawns` previously used an inline `!pawn.IsColonist` check. Updated to use `PawnToolValidator.CanUseSurvivalTools(pawn)` — the same contract used by `Alert_ColonistNeedsSurvivalTool` and `JobGate` — plus an explicit `Downed` / `Awake` guard. `StatPart_SurvivalTools.CanUseSurvivalTools` (private, penalty application only) intentionally remains distinct: it applies to any humanlike with Manipulation capacity, not just player-faction pawns.

### Ticket 7 — WorkGiver heuristic resolution diagnostics

**File:** `Source/Helpers/StatGatingHelper.cs`  
`GetStatsForWorkGiver` now logs (once per defName, at debug verbosity) when it resolves stats via heuristics rather than an explicit `WorkGiverExtension.requiredStats`. This makes heuristic-only coverage visible in logs so authors can decide whether to add an explicit extension.

### Ticket 8 — Settings UX alignment

**File:** `Source/SurvivalToolsSettings.cs`  
Auto-enhanced tool multiplier slider section header updated to "bounded: min floor — max ceiling" and the tooltip clarified: minimum enforces a baseline-challenge floor; maximum prevents overtuning. Runtime caveat text updated to "fully take effect after reload/restart."

