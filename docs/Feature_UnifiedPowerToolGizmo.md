# Feature: Unified Power Tool Manager Gizmo

## Overview

A unified gizmo UI component that displays the charge level of powered tools and provides battery management functionality. This gizmo consolidates charge display, battery ejection, and battery insertion into a single, polished interface.

**Implementation Date**: Phase 12.1  
**Replaces**: Separate charge status, eject, and insert battery gizmos

## Display Contexts

The gizmo appears when:

1. **A pawn with a powered tool equipped is selected** - Full functionality including automated battery swapping
2. **A power tool itself is selected** (on ground, in stockpile, etc.) - Limited to manual battery operations

## Visual Design

### Layout (140×105px)

```
┌─────────────────────┐
│   Power Drill       │ ← Tool name (18px)
├─────────────────────┤
│ ████████░░░░░░░░░░  │ ← Charge bar (20px)
│   150/200           │ ← Charge amount
│  75% (Li-ion)       │ ← Percentage + battery type (15px)
├─────────────────────┤
│ [Eject Battery]     │ ← Action button (24px)
└─────────────────────┘
```

### Color Coding

- **Green** (>50% charge): Optimal power level
- **Yellow** (25-50% charge): Moderate power
- **Red** (<25% charge): Low battery warning

### Dynamic Button States

- **With Battery**: Shows "Eject Battery" button
- **No Battery**: Shows "Insert Battery" button with float menu for battery selection

## Implementation Files

### Source/UI/Gizmo_PowerToolManager.cs (NEW)

Unified gizmo class combining all power tool management features:

- Charge bar visualization with color coding
- Charge amount and percentage display
- Battery information (type, charge level)
- Eject battery functionality
- Insert battery functionality with float menu
- Comprehensive tooltip with warnings
- Context-aware behavior (with/without pawn)

### Source/HarmonyPatches.cs (MODIFIED)

Integration point for pawn-equipped tools:

- `AddBatteryGizmos()` method simplified to add single unified gizmo
- Removed separate eject/insert battery button creation
- Provides pawn context for automated battery swapping via jobs

### Source/Power/CompPowerTool.cs (MODIFIED)

Integration point for directly-selected tools:

- `CompGetGizmosExtra()` override adds the unified gizmo when tool selected
- Provides limited functionality (manual operations only, no pawn jobs)

### Source/UI/Gizmo_PowerToolStatus.cs (DEPRECATED)

Original charge-only display gizmo - replaced by unified gizmo.

## Features

### 1. Charge Display

- **Real-time**: Updates as tool consumes power during work
- **Visual bar**: Immediate feedback on charge level
- **Numeric display**: Precise charge amount (current/max)
- **Percentage**: Quick reference for charge status
- **Battery info**: Shows installed battery type or "Internal" charge

### 2. Battery Ejection (When Battery Present)

- Single-click button to eject battery
- **With pawn context**: Battery goes to pawn inventory or drops nearby
- **Without pawn context**: Battery drops at tool location
- Feedback message confirms ejection

### 3. Battery Insertion (When No Battery)

- Single-click button opens float menu with available batteries
- **Sources**:
  - Pawn inventory (if pawn context available)
  - Nearby items (within 10 tiles)
- **Display format**: "Battery Name (Charge%) - Location/Distance"
- **With pawn context**: Queues jobs to retrieve and insert battery
- **Without pawn context**: Shows manual-only options with instruction message

### 4. Tooltip Information

Hover over non-button areas shows:

- Tool name
- Charge level (amount + percentage)
- Battery details (if present) or internal charge note
- Capacity information
- Warning messages:
  - Red: "Tool is out of power!"
  - Yellow: "Warning: Low battery" (below 25%)

## Settings Integration

- Respects `enablePoweredTools` setting from mod options
- Gizmo only appears when powered tools feature is enabled
- No performance impact when feature disabled

## Technical Details

### Charge Bucket System

- Uses 5% charge buckets (0-20 steps) to minimize UI updates
- Mesh updates only when crossing bucket boundaries
- Reduces performance overhead during continuous work

### Job System Integration (Pawn Context Only)

When battery insertion is triggered with a pawn:

1. Creates `TakeInventory` job to retrieve battery
2. Queues `ST_SwapBattery` job to insert after pickup
3. Jobs execute automatically in sequence

### Fallback Behavior (No Pawn Context)

When tool is selected directly (not via pawn):

- Eject: Battery drops at tool's location
- Insert: Shows informational message about needing pawn
- Prevents errors when no pawn available for job queuing

## Benefits Over Previous Implementation

### User Experience

- **Less clutter**: 1 gizmo instead of 3 separate buttons
- **Contextual**: Only shows relevant actions
- **Intuitive**: All power tool info in one place
- **Professional**: Polished, integrated appearance

### Code Quality

- **Maintainability**: Single source of truth for UI
- **Extensibility**: Easy to add features (recharge, etc.)
- **Consistency**: Unified behavior across contexts

### Performance

- **Efficient**: Single gizmo render instead of multiple
- **Smart updates**: Uses charge bucket system
- **Minimal overhead**: Only renders when tool selected

## Testing Recommendations

### Basic Functionality

1. ✅ Gizmo appears when pawn with powered tool selected
2. ✅ Gizmo appears when powered tool on ground selected
3. ✅ Charge bar displays correctly and updates during work
4. ✅ Color coding changes at correct thresholds
5. ✅ Battery info displays correctly (type and internal charge)

### Battery Operations (With Pawn)

1. ✅ Eject button appears when battery present
2. ✅ Insert button appears when no battery
3. ✅ Ejected battery goes to pawn inventory
4. ✅ Float menu shows inventory batteries
5. ✅ Float menu shows nearby batteries with distances
6. ✅ Pawn retrieves and inserts selected battery automatically
7. ✅ Messages appear confirming operations

### Battery Operations (Without Pawn)

1. ✅ Eject button drops battery at tool location
2. ✅ Insert button shows manual-only options
3. ✅ Informational message appears for manual operations

### Edge Cases

1. ✅ No duplicate gizmos if pawn and tool both selected
2. ✅ Tooltip doesn't cover button areas
3. ✅ Works with multiple power tool types
4. ✅ Respects settings toggle
5. ✅ Handles tool with 0% charge
6. ✅ Handles tool at 100% charge
7. ✅ Multiple tools selected simultaneously

## Future Enhancements

- Add recharge button (when charging stations implemented)
- Show power consumption rate in tooltip
- Display estimated runtime based on current work
- Battery swap history/analytics
- Quick-swap hotkeys for multiple batteries
- Battery health/degradation display

## Related Systems

- **CompPowerTool**: Battery management component
- **CompBatteryCell**: Individual battery charge tracking
- **ST_SwapBattery Job**: Automated battery insertion job
- **Power Tool System**: Phase 12 powered tools feature

## Migration Notes

The unified gizmo automatically replaces the previous three-gizmo system. No manual migration needed. The old `Gizmo_PowerToolStatus` class remains in the codebase but is no longer instantiated.
