# SurvivalTools - Recent Changes Summary

Generated: September 8, 2025

This document summarizes all significant changes made to the SurvivalTools mod since the last git commit. Focus areas: normal mode behavior, WorkSpeedGlobal support, compatibility framework, AI and job logic, validation, debug tooling, and logging system refactor.

**Note:** All source files have been manually reviewed and refactored for consistency with the new gating, validation, and logging standards. This includes helpers, AI, Harmony patches, debug tools, and compatibility modules.

## Key Improvements Overview

### 1. Normal Mode Penalty System Overhaul

- Fixed “hardcore mode appearing enabled” issue in normal games
- More intuitive penalties: default 50% speed (from 30%) when no tool
- Optional stat exemption: cleaning, research, and medical unaffected in normal mode
- Configurable penalties: sliders/toggles to adjust or disable penalties
- Clearer settings UI: explanations for differences between modes

### 2. WorkSpeedGlobal Support

- New stat `WorkSpeedGlobal` for crafting/production activities
- Classified as a core work stat with high priority (85)
- Integrated into `StatFilters` and penalty/alert logic

### 3. Compatibility Framework

- Research Reinvented (RR): runtime detection and gated integration
- Separate Tree Chopping (STC): conflict detection and auto-disable of overlapping systems
- Centralized compatibility registry and API (no compile-time deps)
- Harmony patch logging and targeted debug tools

### 4. Debugging and Validation

- Job analyzer and penalty test actions
- Structured logging with cooldowns to avoid spam
- New world/game components for safe, delayed validation

### 5. Logging System Refactor

- All debug/info/warning/error/compat logs now use deduplicated, quiet, and author-informative patterns
- IDE0002 “name can be simplified” fixes: static import for all logging calls (e.g., `LogDebug`, `LogError`)
- Removed redundant debug gating (`if (ST_Logging.IsDebugLoggingEnabled)`)
- Log spam reduction: all source files and folders scanned, spammy logs silenced, cooldowns enforced
- Consistent logging style: deduplication and cooldown standard applied everywhere

---

## Files Modified/Added

### Core Systems

- `Source/Stats/StatPart_SurvivalTool.cs`: Added `GetNoToolPenaltyFactor()`; respects optional stats; configurable normal-mode factor; improved logging
- `Source/SurvivalToolsSettings.cs`: New settings (`enableNormalModePenalties`, `noToolStatFactorNormal`); expanded settings UI and help text
- `Source/DefOfs/ST_StatDefOf.cs`: Added `WorkSpeedGlobal` def refs
- `Source/SurvivalToolUtility.cs`, `Source/SurvivalTool.cs`: Updated gating/utility helpers for new logic

### Helpers (Expanded)

- `Source/Helpers/JobDefToWorkGiverDefHelper.cs`: Improved job-to-workgiver mapping logic for gating and validation
- `Source/Helpers/SurvivalToolValidation.cs`: Refactored validation logic, improved error handling and logging
- `Source/Helpers/ST_Logging.cs`: Further deduplication, cooldown logic, and static import fixes
- `Source/Helpers/CollectionExtensions.cs`, `StatFilters.cs`, `SafetyUtils.cs`, `ConditionalRegistration.cs`, `JobUtils.cs`, `PawnToolValidator.cs`, `PawnToolValidator_Fixed.cs`, `ToolClassification.cs`, `ToolScoring.cs`, `WorkSpeedGlobalHelper.cs`: Refactored for maintainability and consistency

### AI and Jobs

- `Source/AI/AutoToolPickup_UtilityIntegrated.cs`: Integrated smarter auto-pickup with utility checks
- `Source/AI/JobGiver_OptimizeSurvivalTools.cs`: Improved tool optimization/scoring
- `Source/AI/JobDriver_FellTree.cs`, `JobDriver_HarvestTree.cs`, `JobDriver_PlantWork.cs`, `JobDriver_DropSurvivalTool.cs`: Stability and gating improvements

### Debug Tools

- `Source/DebugTools/ExtensionLogger.cs`, `PrimitiveToolsPatchLogger.cs`, `ResearchReinventedPatchLogger.cs`: New/refined debug loggers for patch analysis and output

### Alerts

- `Source/Alerts/Alert_ColonistNeedsSurvivalTool.cs`: Added `ShouldShowAlertForStat()`; respects optional stats and settings; 20% efficiency buffer
- `Source/Alerts/Alert_SurvivalToolNeedsReplacing.cs`: Polishing and accuracy improvements

### Harmony Patches

- `Source/Harmony/Patch_WorkGiver_*` and related: Improved tool gating for sowing, plant cutting, capacity checks, hauling to inventory, grower jobs
- `Source/Harmony/Patch_EquipmentUtility_CanEquip_PacifistTools.cs`: Pacifist equip logic fixes
- `Source/Harmony/Patch_ThingDef_SpecialDisplayStats.cs`, `Patch_ITab_Pawn_Gear_DrawThingRow.cs`: Display and UI polish
- `Source/HarmonyPatches.cs`: Registration updates for new modules

### Tool Assignments

- `Source/ToolAssignments/Pawn_SurvivalToolAssignmentTracker.cs`, `Source/ToolAssignments/SurvivalToolAssignmentDatabase.cs`: Consistency and performance updates
- `Source/ToolResolver.cs`: Resolution tweaks for new stat and gating rules

### Validation Components

- `Source/GameComponent_SurvivalToolsValidation.cs`: Game-start validation of defs and settings
- `Source/WorldComponent_DelayedValidation.cs`: Safe, delayed validation hooks in long-running games

### Compatibility (New Structure)

- `Source/Compatibility/CompatAPI.cs`, `CompatibilityRegistry.cs`: Centralized compatibility entrypoints
- `Source/Compatibility/ResearchReinvented/`: `RRRuntimeIntegration.cs`, `RRReflectionAPI.cs`, `RRSettings.cs`, `RRAutoToolIntegration.cs`, `RRGatedPatches.cs`, `ResearchReinventedCompat.cs`
- `Source/Compatibility/SeparateTreeChopping/`: `SeparateTreeChoppingCompat.cs`
- Removed old Compat folder files: `Source/Compat/CompatAPI.cs`, `RR_*` (superseded by new structure)

### XML/Defs

- `1.6/Defs/Stats/Stats_Pawn_WorkGeneral.xml`: Added `WorkSpeedGlobal`; tuned factors and capacities
- `1.6/Defs/ThingDefs/Tools.xml`: Balance/tag updates aligned with gating rules
- `1.6/Patches/Core/ThingDefs_Items/Items_Resource_Stuff.xml`: Stuff compatibility adjustments
- `1.6/Patches/Anomaly/ThingDefs_Items/Items_Resource_Stuff.xml`: New Anomaly patch
- `1.6/Patches/Odyssey/ThingDefs_Items/Items_Resource_Stuff.xml`: New Odyssey patch

### Localization

- `1.6/Languages/English/DefInjected/JobDef/Jobs_Work.xml`: Updated job strings
- `1.6/Languages/English/keyed/Keys.xml`: New keys for settings, alerts, and debug tools

### Binaries

- `1.6/Assemblies/SurvivalTools.dll`: Rebuilt with all changes

## Technical Implementation Details

### Penalty System Logic (excerpt)

```csharp
// Before: All stats got 30% penalty in normal mode
val *= 0.3f; // Always applied

// After: Smart penalty based on stat type and settings
private float GetNoToolPenaltyFactor()
{
    if (SurvivalToolUtility.IsHardcoreModeEnabled)
        return 0f; // Hardcore: 0% speed

    if (!settings.enableNormalModePenalties)
        return 1.0f; // No penalties when disabled

    if (StatFilters.IsOptionalStat(parentStat))
        return 1.0f; // Optional stats unaffected

    return settings.noToolStatFactorNormal; // Configurable (default 50%)
}
```

### Stat Classification System (excerpt)

```csharp
// Core stats (penalized): mining, construction, farming, crafting
// Optional stats (no penalty): cleaning, research, medical

public static bool IsOptionalStat(StatDef stat)
{
    return stat == ST_StatDefOf.ResearchSpeed ||
           stat == ST_StatDefOf.CleaningSpeed ||
           stat == ST_StatDefOf.MedicalOperationSpeed ||
           stat == ST_StatDefOf.MedicalSurgerySuccessChance ||
           stat == ST_StatDefOf.ButcheryFleshEfficiency;
}
```

### Compatibility Detection Pattern (excerpt)

```csharp
// Safe, runtime-only compatibility detection
public static bool IsModActive(string[] packageIds)
{
    return ModLister.AllInstalledMods.Any(mod =>
        mod.Active && packageIds.Any(id =>
            mod.PackageId.Contains(id, StringComparison.OrdinalIgnoreCase)));
}
```

### Logging System Refactor (excerpt)

```csharp
// Logging calls now use static import for maintainability
using static SurvivalTools.ST_Logging;

// Example:
LogDebug("Message", "Tag");
LogError("Error message");
```

---

## User Experience Improvements

- Settings UI: visual explanation box (normal vs hardcore), dynamic slider, helper tooltips, color-coded sections
- Alerts: smarter filtering, reduced spam, efficiency thresholds (20% buffer)
- Normal mode: configurable penalties on core work; optional work full speed; hardcore unchanged

---

## Debug and Analysis Tools

- WorkSpeedGlobal Job Analyzer: identify which jobs should/shouldn’t be gated
- Penalty System Tester: real-time validation on pawns
- Compatibility Loggers: Harmony patch analysis with safe reflection and detailed output
- All debug loggers and analysis tools have been updated for improved output, cooldowns, and compatibility with the new framework.

## Performance and Safety

- Error handling: safe execution wrappers and null-safe operations
- Memory/perf: logging cooldowns, cache invalidation, collection reuse
- Compatibility safety: runtime-only detection, try/catch fallbacks, feature isolation
- All files now use safe execution wrappers, null-safe operations, and improved memory/performance patterns.

---

## Migration and Compatibility Notes

- Settings: new options default sensibly; UI adapts to available features
- Mod load order: no strict requirements; features activate automatically
- Saves: no breaking changes; new features optional and can be toggled
- Compat folder: old files removed in favor of `Source/Compatibility` structure

---

## Summary of Benefits

### For Players

- Intuitive normal mode: optional work doesn’t require tools
- Configurable difficulty: adjust or disable penalties
- Better mod compatibility: conflict detection and auto-resolution
- Cleaner alerts: less spam, more meaningful notifications

### For Mod Authors

- Compatibility framework: simple integration points and helpers
- Debug tools: analysis utilities and patch loggers
- Safe APIs: resilient reflection and guarded code paths
- Reference examples: clear integration patterns

### For Developers

- Refactored codebase: centralized helpers and validation
- Comprehensive logging: faster troubleshooting
- Test tools: in-game debug actions
- Extensible design: modular compatibility registry

---

This summary covers all major changes currently staged or untracked since the last commit, preserving backward compatibility while improving UX and mod interoperability.
