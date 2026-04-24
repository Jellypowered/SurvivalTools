# FloatMenu Performance Fix Plan
**Problem**: Each right-click triggers a 26.7ms (avg) to 327ms (max) hitch in `FloatMenu_PrioritizeWithRescue:Postfix`. The user perceives this as stutter "while the menu is open" but it's actually the click frame itself.

**Goal**: Reduce per-call cost to <5ms.

---

## Evidence Summary

### Profiler Screenshot (corrected reading)
- `SurvivalTools.UI.RightClickRescue.FloatMenu_PrioritizeWithRescue:Postfix`
  - **Average per frame**: 4.114ms (amortized across all frames)
  - **Max for frame**: 327.566ms
  - **Av per call**: **26,702µs = 26.7ms per call** ← real problem
  - **Av calls per frame**: 0.154 (sparse — only on right-click)
  - **Percent**: 189.88% (suspicious; likely measurement artifact when single calls span multiple frames)

### Confirmed: NO per-frame poll
Decompile of `RimWorld.FloatMenuMakerMap.GetOptions` and `Verse.FloatMenu.DoWindowContents` confirms:
- `FloatMenuMakerMap.GetOptions` is called **once** when the menu is constructed (from `Selector.HandleMapClicks` on right-click).
- `FloatMenu.DoWindowContents` only iterates the cached `options` list — no re-evaluation.
- `FloatMenuOptionProvider.GetOptions` is called only from inside `FloatMenuMakerMap.GetOptions` (single iteration over `providers` list).

So our previous per-tick / per-cell guards already prevent multi-pawn duplicates. The remaining cost is purely **the per-call cost of one full pipeline run** when a click does happen.

### Root Cause #2: Per-call cost of `TryAddRescueOptions` pipeline
Even if we fix the per-frame issue, one call currently takes ~73µs-885ms depending on context. The cost breakdown:

**Call chain** (per invocation):
1. `TryAddRescueOptions` → iterates `Scanners` list (~12 scanners)
2. Per scanner: `scanner.CanHandle(ctx)` → cheap
3. Per scanner: `scanner.TryDescribeTarget(pawn, ctx, out desc)` → moderate (cell queries, designation lookups)
4. Per matched scanner: `JobGate.ShouldBlock(..., queryOnly: true)` 
5. Inside `ShouldBlock` → `ResolveRequiredStats()` (cached, cheap after first call)
6. Inside `ShouldBlock` → `StatGatingHelper.ShouldBlockJobForStat(stat, settings, pawn)` per stat
7. Inside `ShouldBlockJobForStat` → **`pawn.HasSurvivalToolFor(stat)`** ← **KEY HOTSPOT**
8. Inside `HasSurvivalToolFor` → **`pawn.GetAllUsableSurvivalTools()`** ← materializes new LINQ chain each call
9. Inside `GetAllUsableSurvivalTools` → calls `VirtualTool.FromThing()` for each tool-stuff item → allocates new `VirtualTool` objects
10. After `ShouldBlock`: `AssignmentSearchPreview.CanUpgradePreview(pawn, stat, out toolName)`
11. Inside `CanUpgradePreview` → **another** `pawn.GetAllUsableSurvivalTools()` call

**Total `GetAllUsableSurvivalTools()` calls per click**: 
- Up to 12 scanners × (1 call in `ShouldBlockJobForStat` via `HasSurvivalToolFor`) + 1 in `CanUpgradePreview` = **13+ materializations per click**
- Each materializes a new LINQ chain + allocates `VirtualTool` objects
- If called 26,702 times per frame × 13 = ~347,000 LINQ materializations per frame

---

## Files to Inspect / Modify

### Priority 1 — Fix the per-frame re-invocation
| File | Function | Issue |
|------|----------|-------|
| `Source/UI/RightClickRescue/Provider_STPrioritizeWithRescue.cs` | `GetOptions(FloatMenuContext)` | Called every frame by RimWorld 1.6 `FloatMenuOptionProvider` API. Currently runs full pipeline each time. |
| `Source/UI/RightClickRescue/FloatMenu_PrioritizeWithRescue.cs` | `Postfix(...)` | Also patching `FloatMenuMakerMap.GetOptions` — may be called per-frame too if `FloatMenuMakerMap.GetOptions` is what powers the live menu. |
| RimWorld 1.6 decompiled: `FloatMenuOptionProvider` | `GetOptions` / `ShouldGetOptionFor` | Confirm whether the API polls per-frame or only at construction. Check `FloatMenu.RecacheOptions`, `FloatMenu.PreOptionChosen`, `FloatMenu.DoWindowContents`. |

**Action**: Decompile RimWorld 1.6 `FloatMenuOptionProvider` and `FloatMenu` to confirm per-frame call pattern.
```
ilspycmd "path/to/Assembly-CSharp.dll" -t RimWorld.FloatMenuOptionProvider -o /tmp/rimworld_decompile/
ilspycmd "path/to/Assembly-CSharp.dll" -t RimWorld.FloatMenu -o /tmp/rimworld_decompile/
```

**Fix if confirmed per-frame**: Add a **click-time result cache** keyed by `(pawnId, cell, tick-at-click-time)`. On first call: run pipeline, cache result. On subsequent calls at same key: return cached list immediately (0 allocation). Invalidate cache when a new click happens (different cell or pawn).

The existing `_providerLastCellTick` / `_providerLastCellPos` guard only blocks same-tick duplicate calls (ReverseCommands fix). It does NOT block per-frame re-calls across multiple ticks. The key must instead be based on "was this the same click event" not "same tick".

### Priority 2 — Eliminate repeated `GetAllUsableSurvivalTools()` materializations  
| File | Function | Issue |
|------|----------|-------|
| `Source/SurvivalToolUtility.cs` | `GetAllUsableSurvivalTools(Pawn)` | Returns `IEnumerable<Thing>` with LINQ chain — materializes fresh on every `foreach`. Creates `VirtualTool` objects every call. |
| `Source/SurvivalToolUtility.cs` | `HasSurvivalToolFor(Pawn, StatDef)` | Calls `GetAllUsableSurvivalTools()` — one call per stat check in `ShouldBlockJobForStat`. |
| `Source/Helpers/StatGatingHelper.cs` | `ShouldBlockJobForStat(StatDef, settings, Pawn)` | Calls `pawn.HasSurvivalToolFor(stat)` — one call per stat per scanner in gate. |
| `Source/UI/RightClickRescue/RightClickRescueBuilder.cs` | `AssignmentSearchPreview.CanUpgradePreview` | Calls `pawn.GetAllUsableSurvivalTools()` separately from the gate checks above. |
| `Source/Gating/JobGate.cs` | `ShouldBlockInternal` | Lines ~130-140: loop over `requiredStatsPre` calling `Scoring.ToolScoring.GetBestTool(pawn, stat)` — each `GetBestTool` call also calls `GetAllUsableSurvivalTools()` internally. |

**Fix**: Pass a pre-materialized `List<Thing>` tools snapshot into `HasSurvivalToolFor`, `CanUpgradePreview`, and the `ShouldBlock` gate. Materialize once per click at the top of `TryAddRescueOptions`:
```csharp
// At top of TryAddRescueOptions, before any scanner loop:
var toolsSnapshot = pawn.GetAllUsableSurvivalTools().ToList(); // ONE allocation
// Pass toolsSnapshot into ShouldBlock and CanUpgradePreview via overloads or thread-local
```

Add overloads:
- `HasSurvivalToolFor(this Pawn pawn, StatDef stat, List<Thing> toolsSnapshot)`
- `AssignmentSearchPreview.CanUpgradePreview(Pawn, StatDef, List<Thing> toolsSnapshot, out string)`
- `JobGate.ShouldBlock(..., List<Thing> toolsSnapshot = null)`

### Priority 3 — Remove `AlreadySatisfiedThisClick()` double-call overhead
| File | Function | Issue |
|------|----------|-------|
| `Source/UI/RightClickRescue/Provider_STPrioritizeWithRescue.cs` | `AlreadySatisfiedThisClick()` | Called at start of `TryAddRescueOptions` AND inside scanner loops. If called per-frame this is harmless, but the check itself involves a `ClickKey` struct comparison — trivial but worth confirming it's not hiding a stale-key bug. |

---

## Fix Implementation Order

### Step 1: Confirm per-frame call pattern (research only, no code changes)
Run in game with dev mode + logging. Add a `Verse.Log.Message` with `Find.TickManager.TicksGame` at the top of `Provider_STPrioritizeWithRescue.GetOptions`. Open a float menu, leave it open 1 second. Check if messages appear every frame (60 per second) or just once.

**Alternative**: Decompile `FloatMenuOptionProvider` from Assembly-CSharp.dll:
```powershell
$rim = "F:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll"
ilspycmd $rim -t "RimWorld.FloatMenuOptionProvider" > "F:\Source\SurvivalTools\RimWorld Decompiled\FloatMenuOptionProvider.cs"
ilspycmd $rim -t "RimWorld.FloatMenu" > "F:\Source\SurvivalTools\RimWorld Decompiled\FloatMenu.cs"
```

### Step 2: Fix per-frame invocation
**If `FloatMenuOptionProvider.GetOptions` is called per-frame**:
- The entire `Provider_STPrioritizeWithRescue` approach may be wrong for this use case.
- **Option A**: Change `GetOptions` to return the same cached list until a new click happens. Use `_lastClickId` (monotonic int incremented by `BeginClick`) as the cache key.
- **Option B**: Unregister/remove the `FloatMenuOptionProvider` subclass entirely and rely solely on the Harmony `Postfix` patch (which fires only at menu construction, not per-frame).
- **Option B is safer**: The `Postfix` on `FloatMenuMakerMap.GetOptions` fires when the `FloatMenu` is first constructed, not when it's displayed. Provider is the wrong API for expensive work.

**If `FloatMenuMakerMap.GetOptions` itself is called per-frame** (powering the Postfix):
- The Postfix is also affected. Same result cache fix applies.

### Step 3: Add tools snapshot to eliminate repeated `GetAllUsableSurvivalTools()`
In `RightClickRescueBuilder.TryAddRescueOptions`:
```csharp
// BEFORE scanner loop, after early exits:
var toolsSnapshot = pawn.GetAllUsableSurvivalTools().ToList();
```

Add internal overloads that accept `toolsSnapshot`:
- `JobGate.ShouldBlock(..., IReadOnlyList<Thing> heldTools = null)` — pass to `ShouldBlockInternal` → `StatGatingHelper.ShouldBlockJobForStat` → `HasSurvivalToolFor`
- `AssignmentSearchPreview.CanUpgradePreview(pawn, stat, toolsSnapshot, out name)` — use snapshot instead of calling `GetAllUsableSurvivalTools()` again

This converts N×M allocations into 1 allocation per click.

### Step 4: Verify with profiler
Expected result after fixes:
- Call count drops from 26,702/frame to ~1 per click (or 0 per frame while menu is open)
- Max frame time for the method drops from 327ms to <5ms
- No visible stutter while float menu is open

---

## What NOT to change
- `queryOnly: true` in all `ShouldBlock` calls from the right-click path — this correctly prevents `TryUpgradeFor` from running (which does map-wide pathfinding). Do not remove.
- Scanner `TryDescribeTarget` logic — it's called once per scanner and already correctly cached in `scored` list.
- The `_lastCellTick` / `_lastCellPos` guards added for ReverseCommands — keep them, they still help for the multi-pawn case.
- `AssignmentSearchPreview.CanUpgradePreview` inventory-only logic — it's correct and lightweight *if* the tools snapshot is passed in.

---

## Key Invariants to Preserve
1. Options must be accurate for the **actual selected pawn** — not a cached result from a different pawn.
2. `ExecuteRescue` (when user clicks) must still call the full `TryUpgradeFor` path (not queryOnly).
3. The disabled feedback option ("No suitable tools") must still appear when there are genuinely no tools on the map.
4. RR research rescue must still work when RR is active.
