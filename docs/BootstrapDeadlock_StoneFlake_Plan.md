# Bootstrap Deadlock Fix + Stone Flake Implementation Plan

**Date:** April 23, 2026  
**Status:** Planned — not yet implemented

---

## Problem Summary

Two separate issues, both related to early-game progression in Hardcore/Nightmare mode:

### Issue 1: False-positive rescue option (UX deadlock)

**Symptom:** Right-click on a cuttable plant shows an *enabled* rescue option like  
`"Prioritize cutting plant (will fetch Plant harvesting speed tool)"` — but clicking it does nothing useful when no valid tool exists on the map.

**Root cause (confirmed in code):**
- `RightClickRescueBuilder` calls `AssignmentSearchPreview.CanUpgradePreview(...)`.
- When `canUpgrade == false`, it falls through, builds the option label with a fallback tool name, and **adds an enabled option anyway**.
- `ExecuteRescue` calls `AssignmentSearch.TryUpgradeFor(...)`. If it returns `false`, the code logs a dev note but **still enqueues the forced job**, which immediately hits the job gate and silently fails.
- Three code sites have this pattern: sow fast path, Pass 1 (scored loop), Pass 2 (fallback loop).

### Issue 2: Bootstrap softlock — no tool material without a tool

**Symptom:** In Hardcore mode, plant cutting requires a tool. Without a tool, you can't cut plants to get wood. Without wood, you can't craft tools. Deadlock.

**User suggestion:** Add a primitive "stone flake" — a scavenged piece of sharp stone that can be found on the map and used for basic work, but breaks quickly and is extremely slow. Not craftable. Acts as the zero-tier tool that makes the bootstrap loop possible.

---

## Fix 1: Rescue Option Truthfulness

### What changes

Three sites in `RightClickRescueBuilder.cs` currently add an enabled option regardless of `canUpgrade`:

| Location | Line approx | Path |
|---|---|---|
| Sow fast path | ~282 | `sowContext` early return |
| Pass 1 loop | ~406 | Scored entry loop |
| Pass 2 loop | ~470 | Fallback scanner loop |

**Change at each site:** When `canUpgrade == false`, instead of adding an enabled float menu option, set `_feedbackThisClick.noToolsAvailable = true` and call `AddDisabledFeedbackOption` (which already exists and produces the correct disabled row: *"No suitable tools are available for X to pick up..."*). Skip adding the action option entirely.

The existing `AddDisabledFeedbackOption` method at the bottom of the builder already handles this case. No new UI infrastructure needed.

**Behavior when `canUpgrade == true`:** Unchanged. Enabled option is added, `ExecuteRescue` runs normally.

### Safety notes
- All three sites are inside try/catch blocks already.
- No new state introduced — just a branch change at the point of option creation.
- The `_feedbackThisClick` struct already tracks `noToolsAvailable`; we just set it earlier and skip adding the action option.

---

## Fix 2: ExecuteRescue Early-Return Guard

### What changes

In `ExecuteRescue`, after `TryUpgradeFor` returns:

```csharp
bool upgradeQueued = AssignmentSearch.TryUpgradeFor(...);
```

Add an early return when no upgrade was queued **and** the pawn doesn't already have a tool meeting the stat:

```csharp
if (!upgradeQueued)
{
    // Check if pawn currently satisfies the stat (has a usable tool in inventory)
    bool alreadySatisfied = pawn.MeetsWorkGiverStatRequirements(stat);
    if (!alreadySatisfied)
    {
        Messages.Message("ST_NoToolAvailable".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.RejectInput);
        return;
    }
    // If already satisfied, fall through — the job may succeed with the current tool.
}
```

**Why the current tool check matters:** There is a valid case where `TryUpgradeFor` returns false because the pawn already *has* a good-enough tool and no upgrade was needed. In that case we must still enqueue the forced job. The guard only bails when the pawn has *neither* a queued upgrade *nor* an existing sufficient tool.

**Safety notes:**
- `MeetsWorkGiverStatRequirements` already exists and is used elsewhere in the codebase (it's the same check JobGate uses). Wrap in try/catch as a fallback to `true` to ensure we never silently eat a valid rescue.
- The new `ST_NoToolAvailable` translation key needs to be added to `Keys.xml`.
- This is the safety net. Fix 1 (UI truthfulness) should prevent users from ever reaching this in normal flow.

---

## Stone Flake: Primitive Zero-Tier Tool

### Design Spec

**DefName:** `SurvivalTools_StoneFlake`  
**Label:** `stone flake`  
**Category:** Item (not a weapon, not apparel)  
**Parent:** `BaseSurvivalTool` (inherits SurvivalTool thingClass)  
**Tech level:** None (no research required)  
**Craftable:** No — no `recipeMaker`, no `stuffCategories`  
**Equippable:** Yes (same as other tools — equip slot Primary)  
**Quality comp:** No — stone flakes don't have quality variance  

**Stats:**
- `MaxHitPoints`: 15 (breaks after ~3-8 uses depending on task)
- `WorkToMake`: irrelevant (not crafted)
- `DeteriorationRate`: 8 (degrades quickly on the ground too)
- `Mass`: 0.3

**Tool stat factors (SurvivalToolProperties):**
- All gated stats at `0.25` factor (25% of normal speed)
- Covers: `TreeFellingSpeed`, `PlantHarvestingSpeed`, `DiggingSpeed`, `ConstructionSpeed`, `SowingSpeed`
- Low enough that a player immediately wants a real tool, high enough to unblock the gate

**Flavor text:** `"A sharp flake of hard stone, knapped from a larger rock. Crude but just barely functional as a cutting or digging implement. It won't last long."`

**Melee tools block:** Minimal — a poke attack only, very weak, so it doesn't become a default weapon preference.

**thingCategories:** `SurvivalTools` (so it shows in the survival tools category/filter already in use).

**Not included:**
- No `recipeMaker` — cannot be crafted intentionally
- No `stuffCategories` — no "make from X" at any bench
- No `smeltable` — cannot be smelted back
- No `researchPrerequisite` — unrestricted by tech

---

### Spawning Method 1: Map Generation Scatter

**Approach:** A new `GenStepDef` using `GenStep_ScatterThings` added to the base generator via XML patch.

**Target:** Patch `MapGeneratorDef[defName="Archipelago" or defName="Base_Player" ...]` to add the GenStep. Since we want this on *all* player starts, the safest approach is a `PatchOperationAdd` targeting all `MapGeneratorDef` entries that include the `GenStep_Scatterer_Buildings_PlayerStart` step (which is present in all standard player starts).

**Count:** `countPer10kCellsRange`: `2~4` (2-4 per 10,000 cells — approximately 3-6 total on a standard 250x250 map). Small enough to feel like scavenging, large enough to reliably find at least one near starting position.

**Spacing:** `minSpacing`: `8` (don't pile them up)

**Terrain:** `terrainValidationDisallowed` includes water — flakes only spawn on walkable solid ground.

**GenStep order:** High number (e.g. `750`) so it runs after terrain and buildings are placed but before pawns.

**Why not C#:** `GenStep_ScatterThings` is a built-in class that takes a ThingDef and a count range. Pure XML, zero runtime risk, no Harmony patches needed.

---

### Spawning Method 2: Rock Chunk / Rubble Drop (Surgical C#)

**Approach:** A Harmony Postfix on `JobDriver_CleanFilth.MakeNewToils` is **not** the right hook — cleaning filth doesn't involve rock chunks.

The correct hook is on the job that removes rock chunks from the map. In RimWorld, hauling a rock chunk away is handled by standard haulage jobs — `JobDriver_HaulToCell` / `JobDriver_HaulToContainer`. When a chunk is *cleaned* (i.e., hauled off the play area as part of rubble clearing), the chunk is destroyed or placed in storage.

**Safer approach — `killedLeavings` patch:**  
Patch all `ThingDef` entries whose `defName` contains `"Chunk"` and are in the `StoneChunks` category to add `SurvivalTools_StoneFlake` as a killed leaving with count `0~1` (random — 0 = no drop, 1 = drop). This fires when a chunk is destroyed by any means (mining, hauling off-map, explosions). The leaving logic is built into the engine with no C# required.

However this is *very* broad (fires on explosions too). To be surgical:

**Preferred C# approach — Postfix on `Mineable.DestroyedBy` path or chunk haulaway:**  
Actually, rock chunks don't have a "destroyed when hauled" callback that's clean to hook. The cleanest existing hook in vanilla is the `Thing.Destroy` path.

**Final decision — use `killedLeavings` XML patch with low probability:**
- Patch all `ThingDef[thingCategories[li="StoneChunks"]]` to add `<killedLeavings>` with `<SurvivalTools_StoneFlake>1</SurvivalTools_StoneFlake>`.
- The probability is controlled by the engine's `leavingsChance` field on the ThingDef, which defaults to 1.0 for explicit leavings. To add randomness, use `GenLeaving` approach or simply accept that 1 flake per chunk destroyed is fine — chunks are rare enough and the flake is worthless enough that it won't be exploitable.
- Scope: only `StoneChunks` category things. Wrap in a `PatchOperationConditional` that checks the category exists before patching.

**Why not a C# Postfix on job driver hauling:**  
Hauling hooks are heavily patched by storage mods (LWM Deep Storage, etc.). A Postfix on `JobDriver_HaulToCell.Toils_Haul` would need to distinguish between "chunk being cleaned" vs "chunk being moved to stockpile" — fragile and unnecessary when `killedLeavings` already handles the intent cleanly.

---

## File Change Summary

| File | Type | Change |
|---|---|---|
| `Source/UI/RightClickRescue/RightClickRescueBuilder.cs` | C# edit | 3 sites: add `canUpgrade == false` branch → disabled feedback instead of enabled option |
| `Source/UI/RightClickRescue/RightClickRescueBuilder.cs` | C# edit | `ExecuteRescue`: add early-return guard when `!upgradeQueued && !alreadySatisfied` |
| `1.6/Defs/ThingDefs/Tools.xml` | XML add | `SurvivalTools_StoneFlake` ThingDef |
| `1.6/Defs/MapGeneration/StoneFlake_MapGen.xml` | XML new | `GenStepDef` for scatter spawn + `PatchOperation` to inject into player start generators |
| `1.6/Patches/Core/StoneChunk_StoneFlakeLeavings.xml` | XML new | `killedLeavings` patch on all `StoneChunks` category ThingDefs |
| `1.6/Languages/English/keyed/Keys.xml` | XML edit | Add `ST_NoToolAvailable` and `SurvivalTools_StoneFlake_Label` etc. |

---

## What Does NOT Change

- `JobGate.ShouldBlock` — no changes. The gate itself is correct.
- `AssignmentSearch.TryUpgradeFor` — no changes to the search logic.
- `AssignmentSearchPreview.CanUpgradePreview` — no changes. Already returns the correct boolean.
- Existing tool tiers — stone flake is completely separate and does not participate in the upgrade/downgrade hysteresis system.
- Normal mode behavior — all changes are inside the Hardcore/Nightmare rescue builder path or in the `ExecuteRescue` function which only fires when rescue is triggered.
- Other mods' hook points — no changes to WorkGiver patches, float menu patches, or job driver patches.

---

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| `MeetsWorkGiverStatRequirements` throws on edge case pawn | Wrapped in try/catch; fallback returns `true` (preserves existing behavior) |
| `killedLeavings` fires on chunk explosions or edge cases | Acceptable — flake is near-worthless and won't accumulate meaningfully |
| Stone flake picked up by auto-equip over real tools | `SurvivalToolProperties` stat factors are explicitly low; scoring system already prefers higher-factor tools. Real tools will always outscore flakes. |
| Map gen scatter lands in impassable terrain | `ScattererValidator_Buildable` and terrain checks in `GenStep_ScatterThings` prevent this. |
| PatchOperationAdd on MapGeneratorDef breaks modded starts | Wrapped in `PatchOperationConditional` checking for the standard GenStep marker. Modded generators without that marker are left alone. |
| `canUpgrade == false` branch skips valid cases | Only skips when `CanUpgradePreview` returns false AND no tool is currently held. Both conditions must be true to show disabled feedback. |
