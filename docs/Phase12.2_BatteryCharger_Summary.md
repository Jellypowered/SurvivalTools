# Phase 12.2: Battery Charging System

**Date**: 2025-10-04  
**Status**: ✅ Complete  
**Build**: Success (0 errors, 0 warnings)

## Overview

Implemented a battery charging system that allows rechargeable batteries (Basic, Advanced, Industrial) to be restored to full power using a grid-connected Battery Charger building. Nuclear batteries cannot be recharged as they generate power through radioactive decay.

## Features Implemented

### Battery Charger Building

- **Power Grid Connection**: Requires 200W base power consumption
- **Capacity**: Can charge up to 4 batteries simultaneously
- **Charge Rate**: 50 charge units per tick when powered
- **Tech Level**: Industrial (unlocked with ST_BatteryTech research)
- **Size**: 2×1 building with interaction cell
- **Components**: Power connection, flickable switch, breakdownable

### Battery Compatibility

✅ **Rechargeable**:

- ST_Battery_Basic (6,000 capacity)
- ST_Battery_Advanced (10,000 capacity) [Legacy]
- ST_Battery_Industrial (15,000 capacity)

❌ **Non-Rechargeable**:

- ST_Battery_Nuclear (40,000 capacity) - Self-powered through radioactive decay

### User Interface

#### Inspector Tab (`ITab_BatteryCharger`)

- Shows all batteries currently charging
- Displays charge percentage for each battery with color-coded bars
- Individual "Eject" button for each battery
- Cyan bar color while charging, green when full
- Status display (Charging, No power, Powered off)
- Scrollable list for multiple batteries

#### Gizmos

- **Insert Battery**: Opens float menu to select nearby batteries (within 15 tiles)
  - Shows battery name, charge percentage, and distance
  - Filters out nuclear batteries and fully-charged batteries
  - Automatically queues haul job for selected pawn
- **Eject All**: Removes all batteries and drops them near the charger

### Job System

#### JobDriver_ChargeBattery

- Pawn goes to battery location
- Picks up battery (using carry system)
- Travels to charger interaction cell
- Inserts battery into charger
- Provides feedback message on completion

### Charging Logic

#### CompBatteryCharger

- **Tick-based charging**: Adds charge every game tick when powered
- **Power requirements**: Only charges when building has power and is switched on
- **Smart management**:
  - Skips nuclear batteries (can't be charged)
  - Skips fully-charged batteries (100%)
  - Automatic null/destroyed battery cleanup
- **Persistence**: Batteries and charge state save/load correctly

### Charge Rate Calculation

```
Charge per second = 50 (charge/tick) × 60 (ticks/second) = 3,000 charge/sec

Charge times (from 0% to 100%):
- Basic Battery (6,000):      2.0 seconds
- Advanced Battery (10,000):  3.3 seconds
- Industrial Battery (15,000): 5.0 seconds
```

## Files Created

### Definitions

1. **1.6/Defs/ThingDefs/Building_BatteryCharger.xml** (95 lines)

   - Battery charger building definition
   - Power connection setup
   - Inspector tab configuration

2. **1.6/Defs/JobDefs/Jobs_Misc.xml** (Updated)
   - Added ST_ChargeBattery job definition

### Code Components

3. **Source/Power/CompBatteryCharger.cs** (354 lines)

   - Main charging component with tick-based charging
   - Battery insertion/ejection management
   - Float menu for battery selection
   - Power and flick state checking

4. **Source/Jobs/JobDriver_ChargeBattery.cs** (93 lines)

   - Haul battery to charger
   - Insert into charging slots
   - Reservations and fail conditions

5. **Source/UI/ITab_BatteryCharger.cs** (150 lines)

   - Inspector tab UI showing all charging batteries
   - Charge bars with color coding
   - Individual eject buttons
   - Scrollable list view

6. **Source/DefOfs/ST_JobDefOf.cs** (Updated)
   - Added ST_ChargeBattery JobDef reference

### Translations

7. **1.6/Languages/English/Keyed/ST_BatteryCharger.xml** (20 lines)
   - UI labels for tab and gizmos
   - Error/status messages
   - User feedback strings

## Integration Points

### Power System

- Uses `CompPowerTrader` for grid connection
- Respects `CompFlickable` switch state
- Compatible with `CompBreakdownable`

### Battery System (Phase 12)

- Reads/writes charge via `CompBatteryCell`
- Respects `BatteryTier` enum for nuclear filtering
- Uses existing `AddCharge()` method

### Job System

- Standard RimWorld job driver pattern
- Haul/carry integration
- Reservation system for preventing conflicts

### Settings

- Respects `enablePoweredTools` setting
- No additional settings needed

## Building Requirements

### Construction

- **Cost**: 100 Steel + 6 Industrial Components
- **Work**: 3,500 work units
- **Skill**: Construction 6
- **Research**: ST_BatteryTech (Industrial tier)

### Placement

- Must be connected to power grid
- Requires 200W constant power draw
- Can be minified (moved/sold)
- Passable (pawns can walk through interaction area)

## User Experience

### Workflow

1. Build battery charger and connect to power
2. Click "Insert battery" gizmo
3. Select battery from float menu
4. Pawn automatically hauls battery to charger
5. Battery charges at 3,000 units/second
6. When full, click "Eject" or "Eject all"
7. Ejected batteries drop nearby for pickup

### Quality of Life

- **Visual feedback**: Cyan bars show active charging
- **Status display**: Clear power/switch state
- **Smart filtering**: Only shows rechargeable batteries
- **Distance display**: Shows how far away batteries are
- **Bulk operations**: Eject all batteries at once
- **Auto-cleanup**: Removes destroyed batteries automatically

## Balance Considerations

### Charge Speed

Current rate (50/tick) means batteries charge very quickly:

- Basic: ~2 seconds
- Industrial: ~5 seconds

This is intentionally fast to avoid tedious micromanagement. Players can slow this down by:

- Limiting power availability
- Using power switches strategically
- Adjusting `chargeRate` in building props

### Power Cost

200W per charger is moderate:

- About the same as a research bench (150W)
- Much less than a fabrication bench (350W)
- Can support 4 batteries charging simultaneously

### Strategic Decisions

- Players must choose between:
  - Many chargers (parallel charging, high power cost)
  - Few chargers (sequential charging, lower power cost)
- Power management becomes important in early game
- Late game: abundant power makes this trivial

## Technical Notes

### Nuclear Battery Handling

Nuclear batteries are explicitly blocked from charging:

- Float menu shows them as disabled
- Insertion attempts show error message
- Charging logic skips them even if inserted via dev mode

### Save/Load Safety

- Batteries save as references (Scribe_Collections)
- Null/destroyed batteries cleaned up on load
- Charge state persists correctly
- No data loss if charger destroyed

### Performance

- Only ticks when powered and has batteries
- No unnecessary calculations for empty chargers
- Efficient null checks prevent errors

## Testing Checklist

✅ Building construction works
✅ Power connection required
✅ Flick switch turns charging on/off
✅ Insert battery float menu shows nearby batteries
✅ Pawn hauls battery to charger
✅ Battery charges at correct rate
✅ Charge bar updates in inspector tab
✅ Nuclear batteries rejected with message
✅ Fully charged batteries skip charging
✅ Individual eject works
✅ Eject all works
✅ Batteries drop correctly when ejected
✅ Destroyed charger drops all batteries
✅ Save/load preserves batteries and charge
✅ No errors in logs

## Future Enhancements

### Potential Improvements

1. **Tiered chargers**: Fast chargers (industrial tech, higher power cost)
2. **Wireless charging**: Charge batteries in nearby storage
3. **Solar chargers**: Slow charging without power grid
4. **Charge priorities**: Prioritize certain battery types
5. **Auto-eject**: Eject fully charged batteries automatically
6. **Batch charging**: Insert multiple batteries at once
7. **Charge efficiency**: Tech upgrades improve charge rate
8. **Battery health**: Track charge cycles, degradation over time

### Balance Adjustments

If players find charging too fast/slow, adjust `chargeRate` in:

- `CompProperties_BatteryCharger.chargeRate`
- Default: 50 per tick (3,000/second)
- Suggested range: 10-100 per tick

## Related Systems

- **Phase 12**: Battery System v2 (battery items, insertion/ejection)
- **CompPowerTool**: Powered tools that use batteries
- **Auto-swap system**: Automatic battery replacement
- **Power grid**: Building power system

---

## Build Status

✅ **SUCCESS**: 0 errors, 0 warnings  
✅ **Files**: 7 created/modified  
✅ **Tested**: In-game functionality verified  
✅ **Documentation**: Complete
