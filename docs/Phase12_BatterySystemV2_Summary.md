# Battery System v2 Implementation Summary

## Phase 12: Battery System for Powered Tools

### Files Created/Modified

#### Research & Recipes
1. **1.6/Defs/ResearchProjectDefs/ST_Research_Batteries.xml** ✓
   - ST_BatteryTech (Industrial, prereq: Electricity)
   - ST_AdvancedBatteryTech (prereqs: Microelectronics + ST_BatteryTech)
   - ST_IndustrialBatteryTech (prereqs: Fabrication + Microelectronics + ST_BatteryTech)

2. **1.6/Defs/RecipeDefs/Recipes_Batteries.xml** ✓
   - ST_Make_Battery_Basic (3000 work, 10 steel + 2 components)
   - ST_Make_Battery_Industrial (5000 work, 15 steel + 5 plasteel + 4 components)
   - ST_Make_Battery_Nuclear (8000 work, 10 plasteel + 5 uranium + 2 spacer components)

#### Thing Definitions
3. **1.6/Defs/ThingDefs/Items_Batteries.xml** ✓
   - Updated ST_Battery_Basic with CompBatteryCell (6000 capacity, Basic tier)
   - Updated ST_Battery_Advanced with CompBatteryCell (10000 capacity, Industrial tier)
   - Updated ST_Battery_Industrial with CompBatteryCell (15000 capacity, Industrial tier)
   - Updated ST_Battery_Nuclear with CompBatteryCell (40000 capacity, Nuclear tier)

#### Traders & Scenarios
4. **1.6/Patches/Core/TraderKindDefs/ST_BatteryTraderPatches.xml** ✓
   - Basic batteries: Industrial bulk goods trader (5-15)
   - Industrial batteries: Industrial bulk goods (2-8) + Orbital bulk goods (3-10)
   - Nuclear batteries: Orbital exotic goods (1-3, rare)

5. **1.6/Patches/Core/Scenarios_Classic_Batteries.xml** ✓
   - Crashlanded: 2× Basic batteries
   - RichExplorer: 1× Industrial battery

#### Job Definitions
6. **1.6/Defs/JobDefs/Jobs_Misc.xml** ✓
   - Added ST_SwapBattery job definition

#### Code Components

7. **Source/Power/CompBatteryCell.cs** ✓
   - New component for battery items
   - Stores charge, capacity, and tier
   - Methods: AddCharge, ConsumeCharge, SetCharge
   - CompProperties_BatteryCell for XML definition

8. **Source/Power/CompPowerTool.cs** ✓
   - Extended with batteryItem field
   - Methods: TryInsertBattery, EjectBattery, CanAcceptBattery
   - Charge reading/writing via inserted battery
   - Updated NotifyWorkTick to discharge from battery
   - Updated AddCharge/SetCharge to work with batteries

9. **Source/Jobs/JobDriver_SwapBattery.cs** ✓
   - Self-targeted job driver for battery swapping
   - Handles ejecting old battery and inserting new one
   - Manages inventory placement

10. **Source/DefOfs/ST_JobDefOf.cs** ✓
    - Added ST_SwapBattery JobDef reference

11. **Source/Assign/PreWork_AutoEquip.cs** ✓
    - Added TryAutoSwapBattery method
    - Added FindBestBattery method
    - Integrated auto-swap check before job start
    - Respects autoSwapBatteries and autoSwapThreshold settings

12. **Source/HarmonyPatches.cs** ✓
    - Added AddBatteryGizmos method
    - Eject battery gizmo (when battery present)
    - Insert battery gizmo (when no battery, opens float menu)
    - Integrated into Postfix_Pawn_GetGizmos

#### Settings

13. **Source/SurvivalToolsSettings.cs** ✓
    - Added autoSwapBatteries (default: true)
    - Added autoSwapThreshold (default: 0.15f = 15%)
    - Added settings UI with threshold slider
    - Serialization support

#### Translations

14. **1.6/Languages/English/Keyed/ST_Batteries.xml** ✓
    - Power status strings
    - Battery swap job messages
    - Settings labels and tooltips
    - Gizmo labels and descriptions
    - User messages

### Features Implemented

✅ Research gate system (3 projects, progressive unlock)
✅ Battery crafting recipes (3 tiers with appropriate costs)
✅ Battery ThingDefs with CompBatteryCell (4 battery types)
✅ Trader stock generators (industrial/spacer traders)
✅ Scenario starting items (Crashlanded, RichExplorer)
✅ Battery insertion/ejection system
✅ Battery tier validation (CanAcceptBattery)
✅ Charge reading/writing via inserted battery
✅ Auto-swap on low charge (PreWork integration)
✅ Settings: autoSwapBatteries, autoSwapThreshold
✅ Gizmos: Eject/Insert battery with float menu
✅ JobDriver_SwapBattery (self-target, inventory management)
✅ Nuclear hazard system (already implemented in CompPowerTool)
✅ HC/NM gating (empty powered tools = no tool for stat)
✅ Translation keys for all user-facing strings

### Gating Behavior

- **Normal Mode**: Empty powered tools still provide benefit (reduced from full charge)
- **Hardcore/Nightmare**: Empty powered tools treated as "no tool" for power-backed stats
- Existing StatGatingHelper.ShouldBlockJobForStat handles the mode-specific logic
- PreWork_AutoEquip respects gating and attempts battery swap when needed

### Auto-Swap Logic

1. **Trigger**: Before starting a work job with relevant stat
2. **Condition**: Current tool charge < autoSwapThreshold (default 15%)
3. **Search Order**:
   - Pawn inventory first
   - Nearby stockpiles (within assignSearchRadius)
4. **Selection**: Best charged battery (highest ChargePct > current)
5. **Execution**: Queue ST_SwapBattery at front, requeue original job
6. **Settings Gate**: Respects autoSwapBatteries flag

### Testing Checklist

□ Research unlocks recipes correctly
□ Recipes produce batteries with correct capacity
□ Traders stock batteries at appropriate tech levels
□ Scenarios include starting batteries
□ Battery insertion/ejection works via gizmos
□ Auto-swap triggers at threshold
□ Battery swap preserves charge state
□ Nuclear hazards trigger when enabled
□ HC/NM modes gate on empty powered tools
□ Settings persist across save/load
□ No errors in logs

### Notes

- Battery graphics use placeholder ComponentIndustrial textures (can be customized later)
- Nuclear hazard explosion is small (2.9 radius, flame damage)
- Battery tier validation is currently permissive (accepts any battery)
- Future: Building_BatteryCharger for recharging depleted batteries
- Future: More sophisticated tier matching (e.g., industrial tools require industrial+ batteries)

### Integration Points

- **CompPowerTool**: Battery storage, charge delegation
- **CompBatteryCell**: Per-item charge tracking
- **PreWork_AutoEquip**: Auto-swap trigger
- **StatGatingHelper**: Mode-specific gating behavior (existing)
- **ToolScoring**: Empty tool scoring (existing, no changes needed)
- **GearTab_ST**: Charge bar visualization (existing)

### Compatibility

- Respects existing enablePoweredTools setting
- Respects existing enableNuclearHazards setting
- No changes to non-battery powered tool behavior
- Safe for mid-save addition (batteries spawn empty if capacity not loaded)
