# Global Difficulty Scale — Implementation Plan

Date: 2026-04-25

## Goal

Add one single slider (`overallDifficultyScale`) that tunes all SurvivalTools difficulty
systems in a balanced way. At the neutral value of 1.0 behavior is identical to current
settings. Above 1.0 = harder. Below 1.0 = easier.

Existing Normal / Hardcore / Nightmare mode rules are preserved. The global scale layers on
top of them.

---

## Safety Constraints

1. At `overallDifficultyScale = 1.0`, every computed value must be bit-for-bit identical
   to the current raw setting value (no unintended change on neutral).
2. `useLegacyDifficultyBehavior = true` bypasses all scaling and falls back to raw setting
   values. This survives in saves so users can opt-out without breaking their colony.
3. Hard clamps on every mapped output. No subsystem can receive an unsafe value.
4. Epsilon comparison logic (0.001 thresholds in scoring/gating) is never touched.
5. Tech multiplier `const` bounds in settings are not changed. The clamp is applied after
   scaling so slider cannot escape existing architecture bounds.
6. All mapping functions are pure (no side effects). Settings object stays clean.

---

## New Files

| File | Purpose |
|------|---------|
| `Source/Helpers/DifficultyScaling.cs` | Pure mapping functions: raw setting → effective value |

---

## Modified Files by Phase

### Phase 1 — Framework (DifficultyScaling helper + settings fields)

| File | Change |
|------|--------|
| `Source/Helpers/DifficultyScaling.cs` | **CREATE** — pure mapping class, all subsystem scale functions, hard clamps |
| `Source/SurvivalToolsSettings.cs` | Add `overallDifficultyScale = 1.0f`, `useLegacyDifficultyBehavior = false`, Scribe lines, computed property block (EffectiveNoToolFactor, EffectiveHardcoreEffectiveness, EffectiveAssignMinGainPct, EffectiveSearchRadius, EffectivePathBudget, EffectiveResolverMult(tech), EffectiveDegradationFactor) |

### Phase 2 — Throughput paths (formulas 1-5, 9)

| File | Change |
|------|--------|
| `Source/Stats/StatPart_SurvivalTools.cs` | `val *= settings.noToolStatFactorNormal` → `val *= settings.EffectiveNoToolFactor` |
| `Source/Stats/StatPart_SurvivalTools.cs` | Explanation string line → use `EffectiveNoToolFactor` |
| `Source/SurvivalToolUtility.cs` (ToolFactorCache) | `noToolStatFactorNormal ?? 0.4f` → `SurvivalToolsMod.Settings?.EffectiveNoToolFactor ?? 0.4f` |
| `Source/Scoring/ToolScoring.cs` | HC/NM effectiveness apply: `settings.hardcoreToolEffectiveness` → `settings.EffectiveHardcoreEffectiveness` |
| `Source/Alerts/Alert_ColonistNeedsSurvivalTool.cs` | `settings.noToolStatFactorNormal` (threshold) → `settings.EffectiveNoToolFactor` |

### Phase 3 — Assignment / gating paths (formulas 10-13)

| File | Change |
|------|--------|
| `Source/Assign/PreWork_AutoEquip.cs` | `GetMinGainPct` returns `settings.EffectiveAssignMinGainPct` (already difficulty-scaled, remove duplicate HC/XHC scaling inside the function OR keep it as an additional layer — see note below) |
| `Source/Assign/PreWork_AutoEquip.cs` | `GetSearchRadius` → `settings.EffectiveSearchRadius` |
| `Source/Assign/PreWork_AutoEquip.cs` | `GetPathCostBudget` → `settings.EffectivePathBudget` |
| `Source/Assign/PreWork_AutoEquip.cs` | Direct reads of `settings.assignSearchRadius` (line 1235) → `settings.EffectiveSearchRadius` |
| `Source/Game/ST_GameComponents.cs` | `settings.assignSearchRadius` and `assignPathCostBudget` reads → effective variants |

> Note on PreWork_AutoEquip difficulty scaling: the existing per-mode multipliers inside
> `GetMinGainPct` (HC ×1.25, XHC ×1.5) apply on top of the base. The global scale will
> apply to the BASE before those mode multipliers, so stacking is: 
> `effectiveBase * modeMultiplier`. This is intentional — mode still has its own feel.

### Phase 4 — Resolver + degradation (formulas 15-20)

| File | Change |
|------|--------|
| `Source/SurvivalToolsSettings.cs` | `GetToolResolverTechMultiplier` clamp → calls `EffectiveResolverMult(baseMult, min, max)` |
| `Source/SurvivalToolsSettings.cs` | `EffectiveToolDegradationFactor` property → multiply result by `DifficultyScaling.ScaleDegradation(1f, overallDifficultyScale)` OR fold directly into the property chain |

### Phase 5 — UI + diagnostics

| File | Change |
|------|--------|
| `Source/SurvivalToolsSettings.cs` (DoWindowContents) | Add master slider section above HC/XHC section: label, slider, reset button, tooltip |
| `Source/SurvivalToolsSettings.cs` | Add `LogAllSettings` output for overallDifficultyScale and all Effective* values |
| `Source/Debug/` or nearest debug dump | Add one-line dump of all mapped subsystem values on settings change event |

---

## Formula → Influence Mapping

| # | Formula system | Influence | Effective property |
|---|----------------|-----------|-------------------|
| 1 | finalValue = base × toolFactor | Full | via EffectiveNoToolFactor (baseline shift) |
| 2 | noTool penalty | Full | EffectiveNoToolFactor |
| 3 | Mode penalty stacking | Medium | EffectiveNoToolFactor (base fed into stacking) |
| 4 | Validation baseline | None | kept as-is (mode-only, changing breaks gating logic) |
| 5 | Score over baseline | Full | follows EffectiveNoToolFactor indirectly |
| 6 | Smoothing tiebreak 2% | None | fixed tiebreak, not difficulty-relevant |
| 7 | Quality curve multiplier | Medium | EffectiveHardcoreEffectiveness |
| 8 | Condition multiplier | Medium | EffectiveHardcoreEffectiveness |
| 9 | HC/NM effectiveness slider | Full | EffectiveHardcoreEffectiveness |
| 10 | Candidate gain % | Medium | EffectiveAssignMinGainPct |
| 11 | Candidate acceptance | Medium | EffectiveAssignMinGainPct |
| 12 | Same-family +20% requirement | Low | fixed constant (anti-thrash, not difficulty) |
| 13 | Assignment radius/budget | Medium | EffectiveSearchRadius + EffectivePathBudget |
| 14 | Carry limit | Low | diffCap unchanged (mode semantic), stat cap not scaled |
| 15 | Resolver slider bounds | Full | EffectiveResolverMult per tech tier |
| 16 | Auto-enhanced stat injection | Full | same as 15 (uses GetToolResolverTechMultiplier) |
| 17 | Name-hint fallbacks | Medium | same as 15 (uses GetTechLevelMultiplier → shares resolver mult path) |
| 18 | Default factor = 0 | None | correctness guard, never touch |
| 19 | Degradation factor by mode | Medium | EffectiveDegradationFactor |
| 20 | Wear pulse HP loss | Low | follows EffectiveDegradationFactor |
| 21 | Plant work per tick | Medium | TreeFellingSpeed is a stat — affected by throughput path |
| 22 | Mining duration clamp | Low | DiggingSpeed stat affected by throughput path |

---

## DifficultyScaling.cs API (exact signatures)

```
class DifficultyScaling
  const Min = 0.5f
  const Max = 2.0f
  const Neutral = 1.0f

  ScaleNoToolFactor(float baseFactor, float scale) -> float
    // harder = lower factor (more penalty)
    // result = Clamp(baseFactor / scale, 0.05, 1.0)
    // at scale 1.0: 0.4 / 1.0 = 0.4 (unchanged)
    // at scale 2.0: 0.4 / 2.0 = 0.20
    // at scale 0.5: 0.4 / 0.5 = 0.80

  ScaleHardcoreEffectiveness(float baseEff, float scale) -> float
    // harder = higher (quality matters more)
    // result = Clamp(baseEff * sqrt(scale), 0.5, 1.5)
    // sqrt dampens to avoid runaway at 2.0
    // at scale 1.0: 1.0 * 1.0 = 1.0 (unchanged)
    // at scale 2.0: 1.0 * 1.414 -> clamped 1.414
    // at scale 0.5: 1.0 * 0.707 = 0.707

  ScaleAssignMinGainPct(float baseGain, float scale) -> float
    // harder = higher threshold
    // result = Clamp(baseGain * scale, 0.01, 0.5)
    // at scale 1.0: 0.10 (unchanged)
    // at scale 2.0: 0.20
    // at scale 0.5: 0.05

  ScaleSearchRadius(float baseRadius, float scale) -> float
    // harder = shorter search
    // result = Clamp(baseRadius / scale, 5, 200)
    // at scale 1.0: 25 (unchanged)
    // at scale 2.0: 12.5
    // at scale 0.5: 50

  ScalePathBudget(int baseBudget, float scale) -> int
    // harder = tighter budget
    // result = RoundToInt(Clamp(baseBudget / scale, 50, 5000))
    // at scale 1.0: 500 (unchanged)
    // at scale 2.0: 250
    // at scale 0.5: 1000

  ScaleResolverMult(float baseMult, float scale, float min, float max) -> float
    // harder = lower tool power on auto-enhanced modded tools
    // result = Clamp(baseMult / sqrt(scale), min, max)
    // sqrt dampens to stay within existing bounds
    // at scale 1.0: baseMult (unchanged)
    // at scale 2.0: baseMult / 1.414 (moderately weaker)
    // at scale 0.5: baseMult / 0.707 (moderately stronger)

  ScaleDegradationMult(float scale) -> float
    // harder = faster wear; returns a pure multiplier for EffectiveToolDegradationFactor
    // result = Clamp(scale, 0.1, 5.0)
    // at scale 1.0: x1.0 (unchanged)
    // at scale 2.0: x2.0
    // at scale 0.5: x0.5
```

---

## SurvivalToolsSettings additions

```
// Global difficulty scale
float overallDifficultyScale = 1.0f    // [0.5 .. 2.0]
bool  useLegacyDifficultyBehavior = false

// Computed effective properties (all subsystems read these, never raw fields directly)
float EffectiveNoToolFactor
float EffectiveHardcoreEffectiveness
float EffectiveAssignMinGainPct
float EffectiveSearchRadius
int   EffectivePathBudget
float EffectiveResolverMult(TechLevel)     // replaces GetToolResolverTechMultiplier internals
float EffectiveDegradationFactor           // replaces raw EffectiveToolDegradationFactor
```

---

## Scribe additions in ExposeData

```
Scribe_Values.Look(ref overallDifficultyScale, nameof(overallDifficultyScale), 1.0f);
Scribe_Values.Look(ref useLegacyDifficultyBehavior, nameof(useLegacyDifficultyBehavior), false);
```

---

## UI Specification

Location: above existing HC/XHC toggles in settings window.

```
[Section header: "Global Difficulty Scale"]
Slider: 0.5 → 2.0, step 0.05, current value shown as label
Reset to 1.0 button
Tooltip: "1.0 = current defaults. Higher = harder (tougher penalties, 
          weaker auto-tools, stricter assignment). Lower = easier."
Tiny footnote: "Stacks on top of Normal/Hardcore/Nightmare mode settings."
Checkbox: "Use legacy behavior (ignore global scale)" [useLegacyDifficultyBehavior]
```

---

## Regression Validation Matrix

Run at scale 1.0 before and after implementation:

| Check | Expected |
|-------|----------|
| noToolStatFactorNormal = 0.40 → EffectiveNoToolFactor at scale 1.0 | 0.40 |
| hardcoreToolEffectiveness = 1.0 → EffectiveHardcoreEffectiveness at scale 1.0 | 1.0 |
| assignMinGainPct = 0.10 → EffectiveAssignMinGainPct at scale 1.0 | 0.10 |
| assignSearchRadius = 25 → EffectiveSearchRadius at scale 1.0 | 25.0 |
| assignPathCostBudget = 500 → EffectivePathBudget at scale 1.0 | 500 |
| toolResolverMultIndustrial = 1.0 → EffectiveResolverMult(Industrial) at scale 1.0 | 1.0 |
| toolDegradationFactor = 1.0, HC → EffectiveDegradationFactor at scale 1.0 | 1.5 |
| Build Debug + Release | Exit 0 |
| Alert system stable with 30+ colonists | No spam |
| RR gating path still blocks without tool | Blocked |

---

## Commit Strategy

1. Phase 1 alone: DifficultyScaling.cs + settings fields (no call sites changed yet, neutral by default)
2. Phases 2-4 together: all call site migrations (neutral parity confirmed)
3. Phase 5: UI + diagnostics
4. Each phase: build Debug + Release before proceeding

---

## Rollback

Set `useLegacyDifficultyBehavior = true` in saves or settings to bypass all scaling.
Old per-setting fields are never removed from ExposeData.
