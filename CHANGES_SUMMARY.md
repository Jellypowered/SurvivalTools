# SurvivalTools Major Updates Summary

## 🚦 Unified Job Gating Logic & Config Improvements

### Major Refactor: Survival Tool Gating

- **Unified gating logic**: All job gating now uses `ShouldGateByDefault(WorkGiverDef)` for consistency.
- **Gate-eligible jobs only**: Only jobs like crafting, medical, research, butchery, etc. are blocked when pawns lack tools.
- **Non-gated jobs never blocked**: Jobs such as repair, haul, clean, rescue, tend, feed, etc. are never blocked or shown in the config UI.
- **Config window filtering**: `WorkSpeedGlobalConfigWindow` only lists gate-eligible jobs for player toggling; non-gated jobs are hidden.
- **Settings respected everywhere**: Gating always respects player toggles; jobs can be enabled/disabled in the UI.
- **WorkSpeedGlobal penalties**: `StatPart_SurvivalTool` only applies penalties for WorkSpeedGlobal if any gated job is enabled in settings.
- **Harmony patches updated**: `Patch_WorkGiver_MissingRequiredCapacity` and `Patch_WorkGiver_Scanner_ToolGate` now use unified gating logic.
- **Validation logic improved**: `SurvivalToolValidation` uses a new helper to map `JobDef` to `WorkGiverDef` for correct gating.
- **StatFilters refined**: No more unconditional gating for WorkSpeedGlobal; gating is now context-sensitive and respects job eligibility.

### Technical Improvements

- **New helper**: Added `JobDefToWorkGiverDefHelper` for robust mapping between jobs and work givers.
- **Error handling**: Improved type safety and error handling in gating logic.
- **Compatibility**: All changes maintain RimWorld 1.6 / C# 7.3 compatibility.

### User Experience Enhancements

- **No over-blocking**: Only appropriate jobs are gated, improving gameplay flow.
- **Cleaner config UI**: Only relevant jobs are shown for toggling, making configuration easier.
- **Relevant penalties/alerts**: Penalties and alerts are only applied when truly relevant, reducing confusion and spam.

## Overview

This document summarizes all the major changes made to SurvivalTools since the last commit, including new features, bug fixes, refactoring, and compatibility improvements.

## 🆕 New Features

### Normal Mode Penalty System

- **Added configurable penalty system for normal mode**
  - New setting: `enableNormalModePenalties` (default: true) - allows completely disabling penalties in normal mode
  - New setting: `noToolStatFactorNormal` (default: 0.5f) - configurable penalty severity (was hardcoded 0.3f)
  - **Smart penalty logic**: Optional stats (cleaning, research, medical) get NO penalties in normal mode
  - **Core work stats** (mining, construction, farming, crafting) still get penalties to incentivize tool use
  - **Improved UI** with explanation box showing difference between normal and hardcore modes

### WorkSpeedGlobal Stat Support

- **Added new `WorkSpeedGlobal` stat** for crafting activities (smithing, tailoring, cooking, art)
- **Added to `ST_StatDefOf.cs`** for code reference
- **Created StatDef in XML** with proper skill factors and StatPart_SurvivalTool integration
- **Integrated into StatFilters** as a core work stat with high priority (85)
- **Categorized under "Crafting"** for organization

### Enhanced Alert System

- **Smarter alert triggering** based on actual efficiency impact
- **New method `ShouldShowAlertForStat()`** checks if efficiency would meaningfully drop below penalty threshold
- **Prevents alert spam** for pawns already slow due to injuries/traits
- **Respects new penalty settings** - no alerts when penalties disabled

### Debug and Analysis Tools

- **`DebugAction_FindWorkSpeedGlobalJobs.cs`** - analyzes all work givers to identify which should/shouldn't be gated by tools
- **`DebugAction_TestNewFeatures.cs`** - quick testing for penalty systems and WorkSpeedGlobal functionality
- **`DebugAction_SpawnSurvivalToolWithStuff.cs`** - enhanced tool spawning with material categorization

## 🔧 Major Refactoring

### Helper Library Creation

Created comprehensive helper library in `Source/Helpers/` to centralize scattered utility code:

#### **`ST_Logging.cs`**

- **Centralized logging system** with debug/compat logging gates
- **Cooldown system** to prevent log spam
- **Per-pawn job logging** to avoid duplicate messages
- **Settings-aware logging** that respects user preferences

#### **`StatFilters.cs`**

- **Stat classification system** (optional vs core stats)
- **Stat grouping and categorization** (mining, construction, farming, etc.)
- **Priority scoring system** for stat importance
- **Availability checking** for tools that improve specific stats

#### **`CollectionExtensions.cs`**

- **StatModifier collection helpers** (GetStatFactorFromList, HasModifierFor)
- **Safe collection operations** (null-safe First, Count, etc.)
- **Dictionary utilities** (GetOrAdd, IncrementCount)
- **String utilities** (JoinNonEmpty, TruncateWithEllipsis)

#### **`ToolClassification.cs`**

- **Tool type detection** by stats, name patterns, and properties
- **Tool capability checking** (IsSurvivalTool, LooksLikeToolStuff)
- **Expected stats for tool kinds** (picks improve digging, axes improve tree felling)

#### **`PawnToolValidator.cs`**

- **Centralized pawn capability validation** (CanUseSurvivalTools, CanUseTools)
- **Policy validation** (ToolIsAcquirableByPolicy, AllowedToAutomaticallyDrop)
- **Forced tool checking** (IsToolForced)

#### **`ToolScoring.cs`**

- **Comprehensive tool scoring system** for AI decision-making
- **Tool comparison logic** (CompareTools, AreSameToolType)
- **Multitool detection** and quality scoring

#### **`JobUtils.cs`**

- **Job cloning utilities** (ShallowClone, CloneJobForQueue)
- **Job validation** (IsJobStillValid, RequiresSurvivalToolStats)
- **Job classification** (IsInventoryJob, IsToolManagementJob)

#### **`SafetyUtils.cs`**

- **Null-safe operations** throughout codebase
- **Safe execution wrappers** with exception handling
- **Validation helpers** (IsValidPawn, IsValidThing)

#### **`ConditionalRegistration.cs`**

- **Feature toggling system** based on settings
- **Tree felling conditional logic** for mod compatibility
- **Automatic feature disabling** when conflicts detected

### Code Modularization

- **Moved scattered utility functions** into appropriate helper classes
- **Eliminated code duplication** across multiple files
- **Standardized error handling** and logging patterns
- **Improved null safety** throughout the codebase

## 🐛 Bug Fixes

### "Hardcore Mode Appearing Enabled" Issue

- **Root cause**: Normal mode applied 30% penalty to ALL stats, including optional ones
- **Fix**: Optional stats (cleaning, research, medical) now get NO penalties in normal mode
- **Result**: Normal mode feels truly "normal" for bonus activities

### StatPart_SurvivalTool Improvements

- **Enhanced penalty logic** in `GetNoToolPenaltyFactor()` method
- **Settings integration** to use configurable penalty values
- **Optional stat filtering** to respect new behavior

### Alert System Fixes

- **Efficiency-based alerting** prevents unnecessary alerts
- **Settings integration** respects penalty disable option
- **Buffer system** (20%) to avoid alert noise

## 🔄 Compatibility System

### Research Reinvented Integration

- **`RRRuntimeIntegration.cs`** - runtime WorkGiver extension attachment
- **`RRReflectionAPI.cs`** - safe reflection-based RR integration
- **`RRSettings.cs`** - RR-specific settings integration
- **`ResearchReinventedCompat.cs`** - main compatibility module

### Separate Tree Chopping Integration

- **`SeparateTreeChoppingCompat.cs`** - conflict detection and resolution
- **Automatic conflict resolution** - disables ST tree felling when STC is active
- **User guidance system** with recommendations

### Debug Analysis Tools

- **`ResearchReinventedPatchLogger.cs`** - analyzes RR Harmony patches
- **`SeparateTreeChoppingPatchLogger.cs`** - analyzes STC conflicts
- **`PrimitiveToolsPatchLogger.cs`** - compatibility analysis for Primitive Tools
- **`ExtensionLogger.cs`** - logs tool extensions for debugging

## 📊 Settings Enhancements

### New Settings UI

- **"Normal Mode Work Speed Settings" section** with clear explanation
- **Visual explanation box** showing normal vs hardcore behavior
- **Dynamic penalty slider** showing exact percentage
- **Contextual help text** explaining which work types are affected
- **Disable option feedback** showing what happens when penalties are off

### Settings Data Structure

- **Added `noToolStatFactorNormal`** (float, default 0.5f)
- **Added `enableNormalModePenalties`** (bool, default true)
- **Proper ExposeData integration** for save compatibility
- **Settings validation** and bounds checking

## 🎯 XML and Definition Changes

### Stat Definitions

- **Added `WorkSpeedGlobal` StatDef** in `Stats_Pawn_WorkGeneral.xml`
- **Proper skill factors** (Crafting skill with 0.08 base, 0.085 bonus per level)
- **Capacity factors** (Manipulation, Sight, Consciousness)
- **StatPart_SurvivalTool integration** with configurable penalties

### Penalty Configuration

- **Updated StatPart configurations** to use new penalty factors
- **Consistency across all survival tool stats**
- **Proper hardcore mode values** (0.4-0.0 range)

## 🛠️ Technical Improvements

### Performance Optimizations

- **Caching systems** in logging to prevent repeated calculations
- **Lazy initialization** for expensive operations
- **Efficient collection operations** with LINQ optimization

### Error Handling

- **Comprehensive try-catch blocks** in critical paths
- **Graceful degradation** when components fail
- **Detailed error logging** for troubleshooting

### Code Quality

- **Consistent naming conventions** across all new code
- **Comprehensive XML documentation** for all public methods
- **Proper null checking** and defensive programming
- **Separation of concerns** between helper classes

## 📝 Debug and Testing Features

### Debug Actions (Available in Dev Mode)

- **"Find WorkSpeedGlobal Jobs"** - analyzes which jobs should/shouldn't be tool-gated
- **"Test Normal Mode Penalties"** - validates penalty system configuration
- **"Test WorkSpeedGlobal Stat"** - checks new stat integration
- **"Spawn tool with stuff..."** - enhanced tool testing with material categorization

### Logging Features

- **Debug logging toggle** in mod settings
- **Compatibility logging** for mod interaction analysis
- **Cooldown system** to prevent log spam
- **Structured logging** with consistent prefixes

### Analysis Tools

- **WorkGiver analysis** with recommendations (gate/don't gate)
- **Categorized results** by work type
- **File output** for detailed review
- **Summary statistics** for development insights

## 🎮 User Experience Improvements

### Better Understanding

- **Clear documentation** of normal vs hardcore behavior
- **Visual explanations** in settings UI
- **Contextual help** throughout the interface

### Sensible Defaults

- **50% penalty** instead of harsh 30% in normal mode
- **Optional stat immunity** for cleaning/research activities
- **Configurable everything** for user customization

### Quality of Life

- **No more confusing alerts** about optional activities
- **Reasonable penalties** that don't feel punitive
- **Clear visual feedback** about what each setting does

## 🔄 Backwards Compatibility

### Save Compatibility

- **All new settings have defaults** that maintain existing behavior
- **Proper ExposeData implementation** for save/load
- **Graceful handling** of missing settings

### Mod Compatibility

- **Safe reflection usage** for mod detection
- **Fallback behaviors** when other mods aren't present
- **Conflict resolution** without breaking existing setups

## 📈 Future Extensibility

### Plugin Architecture

- **Helper library** provides foundation for future features
- **Modular design** allows easy addition of new tool types
- **Compatibility framework** ready for new mod integrations

### Settings Framework

- **Extensible UI system** for adding new configuration options
- **Category-based organization** for complex settings
- **Validation system** for ensuring settings coherence

## 🏁 Summary

This update represents a major enhancement to SurvivalTools, addressing the core user complaint about "hardcore mode appearing enabled" while significantly improving code quality, adding new features, and laying groundwork for future development. The changes maintain full backwards compatibility while providing much more intuitive and configurable behavior for users.

### Key User Benefits:

1. **Normal mode finally feels normal** - no harsh penalties for optional activities
2. **Configurable penalty system** - users can adjust or disable as desired
3. **WorkSpeedGlobal support** - crafters now benefit from survival tools
4. **Smarter alerts** - no more spam about unimportant missing tools
5. **Better mod compatibility** - automatic conflict resolution

### Key Developer Benefits:

1. **Comprehensive helper library** - reduces code duplication and improves maintainability
2. **Robust error handling** - fewer crashes and better debugging
3. **Extensive debugging tools** - easier development and troubleshooting
4. **Modular architecture** - easier to add new features
5. **Compatibility framework** - systematic approach to mod integration

The codebase is now significantly more maintainable, extensible, and user-friendly while preserving all existing functionality.
