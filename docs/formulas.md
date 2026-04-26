# SurvivalTools Formula Guide (Human Readable)

Date: 2026-04-25
Scope: This translates formulas 1-22 into plain language with practical RimWorld-style examples.

## How to read this doc

- Raw formula: The literal math used by code.
- Plain English: What it really does.
- Example: A concrete number walkthrough.
- Why it matters: The gameplay impact.

---

## 1) Final stat value with a tool

Raw formula:
finalValue = baseValue * toolFactor

Plain English:
Your pawn's work stat is multiplied by the tool's factor.

Example:
- Base stat = 100
- Tool factor = 1.20
- Final = 100 * 1.20 = 120

Why it matters:
A 1.20 tool means 20% faster output for that stat.

---

## 2) Final stat value with no tool in Normal mode

Raw formula:
finalValue = baseValue * noToolStatFactorNormal

Plain English:
If no useful tool is found, Normal mode can apply a penalty multiplier.

Example:
- Base stat = 100
- noToolStatFactorNormal = 0.40
- Final = 100 * 0.40 = 40

Why it matters:
No tool can heavily slow work if penalties are enabled.

---

## 3) Mode stacking for no-tool penalty baseline

Raw formula:
- start with baseNormal
- if Hardcore: penalty = penalty + 0.4 * (1 - penalty)
- if ExtraHardcore: penalty = penalty + 0.3 * (1 - penalty)

Plain English:
Each harder mode pushes the penalty upward toward 1.0 using part of the remaining gap.

Example:
- baseNormal = 0.40
- Hardcore: 0.40 + 0.4 * 0.60 = 0.64
- ExtraHardcore on top: 0.64 + 0.3 * 0.36 = 0.748

Why it matters:
The baseline used by some checks gets stricter in harder modes.

---

## 4) Validation baseline by mode

Raw formula:
- Hardcore or ExtraHardcore: baseline = 0
- Otherwise: baseline = no-tool baseline

Plain English:
In HC/XHC, any positive tool contribution counts as valid.

Example:
- Tool score = 0.05
- HC baseline = 0, so 0.05 is valid

Why it matters:
Weak bootstrap tools can still satisfy tool checks in hard modes.

---

## 5) Core tool score over baseline

Raw formula:
if toolFactor <= baseline + 0.001 then score = 0
else score = toolFactor - baseline

Plain English:
A tool only scores if it beats baseline by a tiny margin.

Example:
- baseline = 0.40
- toolFactor = 0.39 -> score 0
- toolFactor = 0.60 -> score 0.20

Why it matters:
Unrelated or weak tools get ignored.

---

## 6) Tiny smoothing bonus for construction tools

Raw formula:
if ConstructionSpeed and tool also improves smoothing:
score = score * 1.02

Plain English:
Dual-purpose construction/smoothing tools get a 2% tiebreak bump.

Example:
- score = 0.50
- dual-purpose bump -> 0.51

Why it matters:
Helps pick better all-around builders when scores are close.

---

## 7) Quality multiplier curve

Raw formula:
score = score * QualityCurve(quality)

Curve values:
- Awful 0.70
- Poor 0.85
- Normal 1.00
- Good 1.15
- Excellent 1.30
- Masterwork 1.45
- Legendary 1.60

Plain English:
Better quality amplifies tool score.

Example:
- base score = 0.40
- Good quality (1.15) -> 0.46
- Legendary (1.60) -> 0.64

Why it matters:
Quality can be a major speed difference.

---

## 8) Condition multiplier (HP)

Raw formula:
conditionFactor = 0.5 + 0.5 * (HP / MaxHP)
score = score * conditionFactor

Plain English:
Broken tools are weaker. At 0% HP you would be at 50% factor; at full HP, 100%.

Example:
- HP ratio = 0.30
- conditionFactor = 0.5 + 0.5 * 0.3 = 0.65
- score 0.40 becomes 0.26

Why it matters:
Damaged tools can feel much slower even with good base stats.

---

## 9) Hardcore/Nightmare effectiveness slider

Raw formula:
score = score * clamp(hardcoreToolEffectiveness, 0.5, 1.5)

Plain English:
HC/NM slider scales final score, but is hard-capped between 0.5x and 1.5x.

Example:
- score = 0.40
- slider 1.30 -> 0.52
- slider 2.00 still clamps to 1.5 -> 0.60

Why it matters:
There is a strict upper cap, so huge boosts are impossible from this slider alone.

---

## 10) Candidate gain percent for upgrades

Raw formula:
gainPct = (newScore - currentScore) / max(currentScore, 0.001)

Plain English:
The system compares percent improvement over your current tool.

Example:
- currentScore = 0.20
- newScore = 0.30
- gainPct = 0.10 / 0.20 = 0.50 (50%)

Why it matters:
A tool must beat your current one by enough percent, not just a tiny absolute amount.

---

## 11) Candidate acceptance rule

Raw formula:
- normal mode: gainPct >= minGainPct
- gating rescue: newScore > currentScore + 0.001

Plain English:
Normal upgrading requires minimum gain. Rescue mode accepts almost any improvement.

Example:
- minGainPct = 10%
- gainPct = 7% -> reject in normal
- rescue mode with any positive bump -> accept

Why it matters:
Rescue mode is much more aggressive at finding any workable tool.

---

## 12) Extra requirement for same-family swaps

Raw formula:
requiredGain = minGainPct + 0.20

Plain English:
If swapping hammer -> better hammer, it demands 20% extra gain to avoid thrash.

Example:
- minGainPct = 0.10
- same-family required = 0.30
- gain 0.22 -> reject

Why it matters:
Stops constant micro-swapping for tiny upgrades.

---

## 13) Difficulty scaling for assignment thresholds

Raw formulas:
- minGainPct: HC = base * 1.25, XHC = base * 1.5
- searchRadius: HC = base * 0.75, XHC = base * 0.5
- pathBudget: HC = base * 0.75, XHC = base / 2

Plain English:
Harder modes require bigger improvements and search less far.

Example:
If base minGainPct is 0.10 and radius is 24:
- HC: minGain 0.125, radius 18
- XHC: minGain 0.15, radius 12

Why it matters:
Even with strong tools on map, pawns may not fetch them in time on hard modes.

---

## 14) Effective carry limit

Raw formula:
carryLimit = min(difficultyCap, statCap)
statCap = floor(SurvivalToolCarryCapacity + 0.001)
if toolbelt: carryLimit = max(carryLimit, 3)

Plain English:
You are limited by whichever cap is lower: mode cap or stat cap.

Example:
- difficultyCap = 2
- carry stat = 3.8 -> statCap 3
- final = min(2,3) = 2

Why it matters:
Carry stat alone cannot exceed the mode cap unless toolbelt rule helps.

---

## 15) Tech slider clamp bounds (auto-enhanced tools)

Raw bounds:
- Neolithic: 0.75 to 0.95
- Medieval: 0.85 to 1.05
- Industrial: 1.00 to 1.15
- Spacer: 1.15 to 1.30
- Ultra: 1.30 to 1.45

Plain English:
Each slider has a strict minimum and maximum.

Example:
- You set Neolithic to 1.20
- It clamps down to 0.95

Why it matters:
This is a major reason slider increases may feel weak, especially low tech.

---

## 16) Auto-enhanced stat injection

Raw formulas:
- required stat value = techMultiplier
- secondary stat value = secondaryBase * techMultiplier

Plain English:
ToolResolver writes multiplier values directly onto tool stats when auto-enhancing.

Example:
- techMultiplier = 1.15
- secondaryBase = 0.75
- secondary stat = 0.8625

Why it matters:
Only applies to tools that go through ToolResolver enhancement rules.

---

## 17) Name-hint fallback multipliers

Raw values:
- Neolithic 1.05
- Medieval 1.10
- Industrial 1.15
- Spacer 1.25
- Ultra 1.40

Plain English:
If no explicit stat exists, name-based hints (axe, sickle, etc.) use these default multipliers.

Example:
- Label contains "pickaxe"
- Industrial tech -> fallback factor 1.15 for mining stat

Why it matters:
Fallback can help modded tools, but explicit stats are still more deterministic.

---

## 18) Default unresolved factor

Raw formula:
factor = 0

Plain English:
If tool does not match stat by explicit data, statBases, or name hints, it contributes nothing.

Example:
- random knife for planting stat
- unresolved -> factor 0

Why it matters:
Prevents unrelated tools from falsely satisfying requirements.

---

## 19) Effective degradation factor by mode

Raw formula:
effective = toolDegradationFactor
if Hardcore: effective = effective * 1.5
if ExtraHardcore: effective = effective * 1.25

Plain English:
Hard modes wear tools faster.

Example:
- base degradation = 1.0
- HC only -> 1.5
- HC + XHC flags -> 1.875

Why it matters:
Tool condition drops faster, which reduces effective score over time.

---

## 20) Wear pulse HP loss

Raw formulas:
raw = 0.1 * toolDegradationFactor * statMultiplier + remainder
wholeLoss = floor(raw)
remainder = raw - wholeLoss
cleaning statMultiplier = 1.5, otherwise 1.0

Plain English:
Wear accumulates in fractions and removes HP in chunks.

Example:
- degradation 1.0, non-cleaning
- each pulse adds 0.1 loss
- after 10 pulses, floor(1.0) => lose 1 HP

Why it matters:
Wear may look slow per tick, then suddenly chunk down HP.

---

## 21) Tree/plant work done per tick in custom job driver

Raw formula:
workThisTick = TreeFellingSpeed * lerp(3.3, 1.0, growth)

Plain English:
Immature plants multiply your work speed more; mature plants use less bonus.

Example:
- TreeFellingSpeed = 1.0
- growth 0.0 -> factor 3.3 -> work 3.3/tick
- growth 1.0 -> factor 1.0 -> work 1.0/tick

Why it matters:
Early growth stages can move faster than you might expect.

---

## 22) Mining toil duration patch

Raw formula:
duration = clamp(3000 / DiggingSpeed, 500, 10000)

Plain English:
Higher DiggingSpeed shortens mining duration, but it cannot go below 500 ticks.

Example:
- DiggingSpeed 3.0 -> 3000/3 = 1000 ticks
- DiggingSpeed 10.0 -> 300 ticks, clamped up to 500

Why it matters:
There is a hard speed floor, so very high digging speed stops giving full linear gains.

---

## Quick reality check: why "I raised sliders but it is still slow"

Most common reasons:

1. Tech sliders are bounded tightly, especially early tiers (Neolithic max 0.95).
2. Sliders only affect auto-enhanced ToolResolver paths, not every explicit tool stat in all defs.
3. Quality and HP condition can pull score down a lot.
4. Hard mode assignment settings can reduce search radius/path budget and increase gain threshold.
5. Hard caps exist in some jobs (example: mining duration clamp minimum 500 ticks).

If you want, a temporary test mode can be added that lifts slider caps and disables wear for quick tuning experiments.
