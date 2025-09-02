# SurvivalTools Compatibility API Refactor

## Overview

The Research Reinvented compatibility has been refactored into a generic `CompatAPI.cs` structure that can be extended for future mod integrations. This provides a clean, extensible foundation for all mod compatibility features.

## New Structure

### Files:

1. **`Source/Compat/CompatAPI.cs`** - Generic compatibility API

   - Regional organization by mod (Research Reinvented, future mods)
   - Clean public interface for external consumption
   - General utility methods for compatibility discovery

2. **`Source/Compat/ResearchReinventedCompat.cs`** - RR implementation

   - Internal implementation class (not public API)
   - Comprehensive FieldResearchSpeedMultiplier support
   - Enhanced job gating and tool integration

3. **`1.6/Patches/ResearchReinvented/FieldResearchSupport.xml`** - XML patches
   - Conditionally adds field research stats to tools when RR is loaded

## API Regions

### Research Reinvented Region

```csharp
public static class CompatAPI
{
    #region Research Reinvented (RR) Compatibility

    // Core status
    public static bool IsResearchReinventedActive

    // Pawn tool checking
    public static bool PawnHasResearchTools(Pawn pawn)
    public static float GetPawnResearchSpeedFromTools(Pawn pawn)
    public static float GetPawnFieldResearchSpeedFromTools(Pawn pawn)  // NEW!

    // AutoTool optimization flags
    public static bool ShouldOptimizeForResearchSpeed()
    public static bool ShouldOptimizeForFieldResearchSpeed()  // NEW!

    // Stat access
    public static StatDef GetResearchSpeedStat()
    public static StatDef GetFieldResearchSpeedStat()  // NEW!
    public static List<StatDef> GetAllResearchStats()

    // WorkGiver identification
    public static bool IsRRWorkGiver(WorkGiverDef workGiver)
    public static bool IsFieldResearchWorkGiver(WorkGiverDef workGiver)  // NEW!

    #endregion
}
```

### Future Mod Regions (Template)

```csharp
#region Combat Extended (CE) Compatibility

public static bool IsCombatExtendedActive => CombatExtendedCompat.IsCEActive();
public static bool PawnHasCombatGear(Pawn pawn) => CombatExtendedCompat.PawnHasCombatGear(pawn);
// ... other CE-specific methods

#endregion

#region VGP Compatibility

public static bool IsVGPActive => VGPCompat.IsVGPActive();
public static List<StatDef> GetVGPStats() => VGPCompat.GetVGPStats();
// ... other VGP-specific methods

#endregion
```

### General Compatibility Utilities

```csharp
#region General Compatibility Utilities

public static List<StatDef> GetAllCompatibilityStats()  // All mods
public static bool HasAnyCompatibilityMods()           // Any active
public static List<string> GetActiveCompatibilityMods() // Names list

#endregion
```

## Enhanced Features

### 1. FieldResearchSpeedMultiplier Support

- **Field Research WorkGiver**: `RR_AnalyseTerrain` specifically identified
- **Dual Stats**: Tools provide both `ResearchSpeed` and `FieldResearchSpeedMultiplier`
- **Smart Job Gating**: Detects field vs normal research requirements
- **AutoTool Integration**: Automatic optimization system recognition

### 2. Regional Organization

- **Modular Structure**: Each mod gets its own region
- **Template Provided**: Clear pattern for future integrations
- **Consistent Naming**: Standard conventions across all mods

### 3. General Utilities

- **Discovery Methods**: Find all active compatibility mods
- **Stat Aggregation**: Collect stats from all integrated mods
- **Future Extensibility**: Easy to add new mod compatibility

## Benefits

1. **Clean Architecture**: Proper separation of API vs implementation
2. **Future-Proof**: Template structure for adding new mod compatibility
3. **Comprehensive Coverage**: Both normal and field research fully supported
4. **Automatic Integration**: Existing systems work without changes
5. **Easy Discovery**: Utilities to find active compatibility features

## Implementation Status

✅ **Research Reinvented**: Full compatibility including field research
✅ **API Structure**: Generic, extensible framework ready
✅ **Build Success**: All features compiling and working
✅ **Template Ready**: Future mod integrations can follow the pattern

The refactored system provides a solid foundation for expanding SurvivalTools compatibility with other mods while maintaining clean, organized code structure.
