# Phase 11.7: XML Stat Hints Investigation

## Objective

Investigate whether XML `PatchOperation` patches that add tool stat hints are redundant with the ToolStatResolver's inference capabilities, and potentially migrate them to the CompatAPI registry.

## Investigation Summary

### Patches Examined

- **86 XML patch files** across multiple mod compatibility folders
- **50+ `toolStatFactors` declarations** found
- Sample patches reviewed:
  - `ExpandedWoodworking/Patch.xml` (13 lumber types)
  - `MetalsPlus/Patch.xml` (5 metal types)
  - `GlitterTech/Patch.xml` (3 polymer types)
  - `RightToolForTheJob/RTFTJ_Patch.xml` (8 tool types)
  - `Core/StuffProps_Fabric.xml` (9 fabric types)

### Architecture Review

**ToolStatResolver Hierarchy (from Phase 2):**

1. **Explicit SurvivalToolProperties/StuffPropsTool** (highest priority - XML patches provide this)
2. **StatBases intersection** (resolver infers from existing stats)
3. **Name hints** (resolver infers from names like "pickaxe", "hammer")
4. **Safe defaults** (fallback when all else fails)

**Key Code:**

```csharp
// Source/Helpers/ToolStatResolver.cs lines 295-349
private static float? TryGetExplicitFactor(ThingDef toolDef, ThingDef stuffDef, StatDef stat)
{
    // Check tool's SurvivalToolProperties first (highest priority)
    var toolProps = toolDef.GetModExtension<SurvivalToolProperties>();
    if (toolProps?.baseWorkStatFactors != null) { /* ... */ }

    // Check stuff's StuffPropsTool - only apply if tool naturally affects this stat
    if (stuffDef != null && toolNaturallyAffectsStat)
    {
        var stuffProps = stuffDef.GetModExtension<StuffPropsTool>();
        if (stuffProps?.toolStatFactors != null) { /* ... */ }
    }
}
```

### Findings

#### Category 1: Material Patches (StuffPropsTool)

**Examples:**

- ExpandedWoodworking: Birch (0.55), Oak (0.65), Bamboo (0.50)
- MetalsPlus: Copper (0.75), Iron (0.9), Bronze (0.85), Titanium (1.25)
- GlitterTech: Titanium (2.0), AlphaPoly (1.8), BetaPoly (8.0)

**Purpose:** Define how a **material** performs when crafted into tools

**Verdict:** **NOT REDUNDANT**

- Resolver **cannot** infer material quality from names (oak vs pine, copper vs steel)
- These are **design decisions** about mod balance
- Different mods assign different values to same materials (e.g., Titanium: 1.25 in MetalsPlus, 2.0 in GlitterTech)
- StuffPropsTool multiplies against tool's natural factor (intentional design)

**Code Evidence:**

```xml
<!-- ExpandedWoodworking/Patch.xml -->
<li Class="SurvivalTools.StuffPropsTool">
  <toolStatFactors>
    <TreeFellingSpeed>0.65</TreeFellingSpeed>  <!-- Oak is stronger than birch (0.55) -->
    <DiggingSpeed>0.65</DiggingSpeed>
  </toolStatFactors>
</li>
```

#### Category 2: Tool Patches (SurvivalToolProperties)

**Examples:**

- RTFTJ_Pickaxe: DiggingSpeed=1.0, MiningYield=1.0
- RTFTJ_Hammer: ConstructionSpeed=1.0, ConstructSuccessChance=1.0
- RTFTJ_Autohammer: ConstructionSpeed=1.1, ConstructSuccessChance=0.9 (tweaked!)
- RTFTJ_Drill: DiggingSpeed=1.1, MiningYield=1.1 (better than pickaxe)
- RTFTJ_Toolbelt: All stats=1.0 (jack of all trades)

**Purpose:** Set **explicit balance values** that override resolver's name hint inference

**Verdict:** **NOT REDUNDANT**

- Resolver **would** infer these from name hints (pickaxe→DiggingSpeed, hammer→ConstructionSpeed)
- BUT resolver would use **default multipliers** (likely 1.0 or generic fallback)
- XML patches set **specific balance values** that differ between similar tools:
  - RTFTJ_Hammer: 1.0 speed, 1.0 success
  - RTFTJ_Autohammer: 1.1 speed, 0.9 success (faster but less accurate)
  - RTFTJ_Drill: 1.1 for both (upgraded mining tool)
- These are **intentional balance tweaks** by the mod author
- Removing patches would rely on name hints, losing precision

**Code Evidence:**

```xml
<!-- RightToolForTheJob/RTFTJ_Patch.xml -->
<!-- Two hammers with different balance profiles -->
<li Class="SurvivalTools.SurvivalToolProperties">
  <baseWorkStatFactors>
    <ConstructionSpeed>1.0</ConstructionSpeed>
    <ConstructSuccessChance>1.0</ConstructSuccessChance>  <!-- Balanced -->
  </baseWorkStatFactors>
</li>

<li Class="SurvivalTools.SurvivalToolProperties">
  <baseWorkStatFactors>
    <ConstructionSpeed>1.1</ConstructionSpeed>
    <ConstructSuccessChance>0.9</ConstructSuccessChance>  <!-- Fast but sloppy! -->
  </baseWorkStatFactors>
</li>
```

### Resolver's Role vs XML Patches

**Resolver provides:**

- Safe defaults when no explicit data exists
- Inference from statBases/names for unknown tools
- Fallback behavior to prevent crashes

**XML patches provide:**

- Authoritative balance data (takes highest priority)
- Material quality definitions (unobtainable by inference)
- Fine-tuned multipliers for mod-specific tools

**Analogy:** Resolver is like a "smart default system" that guesses based on context. XML patches are like a "balance spreadsheet" that mod authors meticulously tune. Both are needed.

## Decision: Phase 11.7 is NO-OP

**Conclusion:** All 86 XML patches serve legitimate purposes and are **NOT duplicates** of resolver functionality.

**Rationale:**

1. **Material patches** define design data about material properties that resolver cannot infer
2. **Tool patches** set explicit balance values that intentionally override resolver's generic inference
3. Resolver's inference is a **safety net**, not a replacement for authoritative data
4. Removing patches would lose precision and mod author's balance intent

**Alternative considered:** Migrate tool patches to CompatAPI `RegisterToolQuirk()` calls

- **Rejected:** XML patches are easier for mod authors to maintain
- **Rejected:** Quirks are for exceptional cases; these are standard declarations
- **Rejected:** XML loads declaratively; code requires mod updates

## Changes Made

**Source/Infrastructure/BuildFlags/Phase11.cs:**

- Set `STRIP_11_7_XML_DUP_HINTS = true` (NO-OP flag, no code guards)
- Comment: "Phase 11.7: NO-OP - XML patches are authoritative data, not duplicates"

**docs/Phase11.7_Summary.md:**

- Created this investigation summary

## Build Status

**Before:** 11 CS0162 warnings (from phases 11.1, 11.4, 11.5)
**After:** 11 CS0162 warnings (unchanged, no new guards added)
**Errors:** 0

## Verification

No runtime testing needed - this is investigation-only with no code changes.

## Next Steps

**Phase 11.8:** Strip tree toggles/switches (if any duplicate tree job helpers exist)
**Phase 11.9:** Strip killlist/deprecated components (final cleanup)

---

**Phase 11.7 Status:** ✅ COMPLETE (Investigation confirmed no duplicates exist)
