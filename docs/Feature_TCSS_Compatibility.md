# TCSS (Tree Chopping Speed Stat) Compatibility Integration

## Overview

Added full compatibility support for the Tree Chopping Speed Stat (TCSS) mod, following the same pattern as our existing STC (Separate Tree Chopping) integration. TCSS and STC are mutually exclusive, with STC taking priority if both are detected.

## Implementation Details

### New Files Created

1. **Source/Compatibility/TreeChoppingSpeedStat/TCSS_Helpers.cs**
   - Detection and authority helpers
   - `IsActive()` - Detects TCSS via type probe (TreeChopSpeed.WorkTypeDefOf) or packageId fallback
   - `IsAuthorityActive()` - Returns true when TCSS is active AND STC is not (TCSS has authority)
   - Includes one-time smoke logging for compat detection

2. **Source/Compatibility/TreeChoppingSpeedStat/TCSS_Debug.cs**
   - Debug and logging helpers proxying to ST_Logging
   - `IsCompatLogging()` - Check if compat logging enabled
   - `LogCompat()` / `LogCompatWarning()` - Cooldown-safe logging

3. **Source/Compatibility/TreeChoppingSpeedStat/TCSS_Patches.cs**
   - Harmony patch initialization
   - `Init(Harmony)` - Main entry point, early exits if TCSS not detected or STC has authority
   - `StripSTFellTreeWorkGivers()` - Removes ST_FellTrees and ST_FellTreesDesignated from all WorkTypeDefs
   - Does NOT patch vanilla WorkGiver_PlantsCut (unlike STC - TCSS relies on vanilla path)
   - Does NOT bypass TreeFellingSpeed gating

### Modified Files

1. **Source/Compatibility/CompatAPI.cs**
   - Added `TCSS_Patches.Init(primaryHarmony)` call after STC initialization

2. **Source/Compatibility/TreeStack/TreeSystemArbiter.cs**
   - Added `TreeChoppingSpeedStat` to TreeAuthority enum
   - Updated authority resolution logic: STC > TCSS (standalone) > PT+TCSS > Internal
   - Logs authority decision with all mod detection states

3. **Source/UI/RightClickRescue/FloatMenu_PrioritizeWithRescue.cs**
   - Added `IsTCSSAuthorityActive()` helper to check TCSS authority
   - Updated `HeuristicModFromOption()` to tag TCSS chop entries with "(TCSS)" mod name
   - Extended tree felling rescue purge logic to also remove ST felling options when TCSS active
   - Both STC and TCSS now suppress ST "Fell tree" right-click entries

## Authority Hierarchy

When multiple tree-handling mods are present, the authority hierarchy is:

1. **STC (Separate Tree Chopping)** - Highest priority
2. **TCSS (Tree Chopping Speed Stat)** - Standalone
3. **PT+TCSS (Primitive Tools + TCSS)** - Combined authority
4. **Internal (ST native)** - Default fallback

If both STC and TCSS are detected:
- STC takes authority
- TCSS helpers return inactive
- Single warning log: "Both TCSS and STC found; STC takes authority"

## Behavioral Changes

### When TCSS is NOT detected
- No change in behavior
- ST FellTree system operates normally

### When TCSS is detected (STC not present)
- ST FellTree WorkGivers are stripped from all WorkTypeDefs
- No "Fell tree" options appear in work priorities or right-click menus
- Vanilla "Chop tree" path remains available via TCSS
- Right-click "Prioritize chopping..." shows "(TCSS)" tag instead of "(Core)"
- TreeFellingSpeed gating continues to apply (no bypass like STC had)
- Tool rescue functionality works for vanilla chopping jobs

### When both STC and TCSS are detected
- STC behavior unchanged
- TCSS patches do not apply
- Single warning logged about conflict resolution

## Key Differences from STC Integration

| Aspect | STC | TCSS |
|--------|-----|------|
| WorkGiver_PlantsCut patch | ✅ Patches to redirect | ❌ No patch (vanilla path used) |
| TreeFellingSpeed bypass | ✅ Bypasses gating | ❌ Continues gating |
| WorkGiver stripping | ✅ Strips ST FellTree WGs | ✅ Strips ST FellTree WGs |
| Right-click rescue | ✅ Removes ST felling | ✅ Removes ST felling |
| Mod tag display | "(Separate Tree Chopping)" | "(TCSS)" |
| Conflict resolution | STC wins vs TCSS | TCSS defers to STC |

## Testing Checklist

### TCSS Off (baseline)
- [ ] Game unchanged
- [ ] ST FellTree shows in work priorities
- [ ] Right-click "Fell tree" options available
- [ ] No TCSS-related log messages

### TCSS On, STC Off
- [ ] No "Fell tree" options in work priorities
- [ ] No ST felling right-click entries
- [ ] Vanilla "Chop tree" available via TCSS
- [ ] Right-click "Prioritize chopping..." shows "(TCSS)" tag
- [ ] TreeFellingSpeed still gates chop jobs
- [ ] Tool rescue works for chopping
- [ ] Log shows: "[TCSS] Detected, applying compatibility patches"
- [ ] Log shows: "[TCSS] Removed ST FellTree WGs under TCSS authority: ..."

### Both STC and TCSS On
- [ ] STC behavior unchanged (STC owns trees)
- [ ] TCSS patches do not apply
- [ ] Log shows: "[TCSS] Both TCSS and STC found; STC takes authority"
- [ ] No double handling or conflicts

### Edge Cases
- [ ] No errors in log
- [ ] Minimal compat smoke messages (only when compat logging enabled)
- [ ] Save/load works correctly
- [ ] Mod load order variations handled correctly

## Performance Impact

- **One-time cost at startup**: WorkGiver stripping (same as STC)
- **Runtime overhead**: Negligible (authority checks cached, no per-tick operations)
- **Memory**: Minimal (a few cached booleans, string caches for logging)

## Compatibility Notes

- **Mutually exclusive with**: Separate Tree Chopping (STC wins)
- **Compatible with**: All other ST features (gating, powered tools, rescue system)
- **Safe with**: Primitive Tools (PT+TCSS authority recognized)
- **RimWorld versions**: 1.6+ (follows existing ST architecture)

## Logging & Debug

Enable compat logging in ST settings to see:
- Detection smoke test: "[TCSS] Detected: Active=true, Authority=true"
- Patch application: "[TCSS] Detected, applying compatibility patches"
- WorkGiver removal summary: "[TCSS] Removed ST FellTree WGs under TCSS authority: PlantCutting(2->1)"
- Conflict warnings: "[TCSS] Both TCSS and STC found; STC takes authority"

All logging respects cooldown limits to avoid spam.

## Future Enhancements

Potential improvements if needed:
- [ ] Add TCSS-specific stat mapping for chopping speed bonuses
- [ ] Coordinate with TCSS on shared research/tech gates
- [ ] Optimize authority detection (currently requires multiple enum checks)
- [ ] Consider unified tree authority API for cross-mod coordination

## References

- TCSS Package ID: `TreeChoppingSpeed.velcroboy333`
- TCSS Type Probe: `TreeChopSpeed.WorkTypeDefOf`
- ST Tree Authority System: `Source/Compatibility/TreeStack/TreeSystemArbiter.cs`
- Integration Pattern: Mirrors STC implementation in `Source/Compatibility/SeparateTreeChopping/`
