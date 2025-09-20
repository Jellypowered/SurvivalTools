# Tool Quirk System

The tool quirk system provides a flexible way to apply custom modifications to tool stat factors during resolution. Quirks are applied after the normal stat inference hierarchy but before the final clamping step.

**Key Features:**

- **Deterministic Ordering**: Quirks are processed in exact registration order
- **No Allocations**: Zero allocations during quirk application
- **Safe API**: Validated modifications with debug tracking
- **Performance**: Efficient predicate checking and minimal overhead

## Registration Order

Quirks are processed in **exact registration order** for deterministic behavior. Each quirk receives a monotonic sequence number when registered. This ensures consistent behavior across game sessions and mod load orders.

## Usage

### Register a Quirk

```csharp
using SurvivalTools.Compat;

// Register quirk in your mod's initialization
CompatAPI.RegisterToolQuirk(
    predicate: toolDef => toolDef.label?.Contains("axe") == true,
    action: applier =>
    {
        if (applier.Stat == ST_StatDefOf.TreeFellingSpeed)
        {
            applier.MultiplyFactor(1.15f, "quality axe");
        }
    }
);
```

### Legacy Overload (Obsolete)

````csharp
// Legacy string-based registration (forwards to new system)
CompatAPI.RegisterToolQuirk("MasterworkAxe", "masterwork tool");
```### Predicate Examples

```csharp
// By tool name/label
toolDef => toolDef.label?.ToLowerInvariant().Contains("masterwork") == true

// By tech level
toolDef => toolDef.techLevel >= TechLevel.Spacer

// By mod extension
toolDef => toolDef.GetModExtension<MyCustomToolExtension>() != null

// By stuff category
toolDef => toolDef.MadeFromStuff &&
           toolDef.stuffCategories?.Contains(StuffCategoryDefOf.Precious) == true
````

### Applier Methods

```csharp
// Multiply factor
applier.MultiplyFactor(1.2f, "quality bonus");

// Add flat bonus
applier.AddBonus(0.1f, "skill bonus");

// Set to specific value
applier.SetFactor(1.5f, "override");

// Conditional modifications
applier.MultiplyIf(applier.IsTechLevel(TechLevel.Spacer), 1.3f, "spacer tech");
applier.AddIf(applier.StuffLabelContains("plasteel"), 0.2f, "plasteel bonus");

// Clamping
applier.ClampMax(2.0f, "balance limit");
applier.ClampMin(0.5f, "minimum effectiveness");
applier.ClampRange(0.8f, 1.8f, "balanced range");
```

### Helper Methods

```csharp
// Tool checks
applier.ToolLabelContains("masterwork")
applier.IsTechLevel(TechLevel.Industrial)
applier.IsTechLevelAtLeast(TechLevel.Medieval)
applier.HasModExtension<SurvivalToolProperties>()

// Stuff checks
applier.StuffLabelContains("steel")
applier.StuffHasModExtension<MyStuffExtension>()

// Current values
float currentFactor = applier.Factor;
StatDef currentStat = applier.Stat;
ThingDef tool = applier.ToolDef;
ThingDef stuff = applier.StuffDef;
```

## Example Quirks

### Quality Tool Bonus

```csharp
CompatAPI.RegisterToolQuirk(
    toolDef => toolDef.label?.ToLowerInvariant().Contains("masterwork") == true,
    applier => applier.MultiplyFactor(1.25f, "masterwork quality")
);
```

### Material-Specific Bonuses

```csharp
CompatAPI.RegisterToolQuirk(
    toolDef => toolDef.MadeFromStuff,
    applier =>
    {
        if (applier.StuffLabelContains("plasteel"))
            applier.MultiplyFactor(1.2f, "plasteel efficiency");
        else if (applier.StuffLabelContains("steel"))
            applier.MultiplyFactor(1.1f, "steel durability");
    }
);
```

### Stat-Specific Modifications

```csharp
CompatAPI.RegisterToolQuirk(
    toolDef => toolDef.techLevel >= TechLevel.Spacer,
    applier =>
    {
        switch (applier.Stat?.defName)
        {
            case "TreeFellingSpeed":
                applier.MultiplyFactor(1.3f, "advanced cutting");
                break;
            case "DiggingSpeed":
                applier.MultiplyFactor(1.4f, "powered digging");
                break;
            case "ConstructionSpeed":
                applier.MultiplyFactor(1.2f, "precision tools");
                break;
        }
    }
);
```

## Debug Testing

⚠️ **Dev Mode Required**: Debug actions only function when `Prefs.DevMode` is enabled.

Use the debug action "Test tool quirk system" to register example quirks and save results to Desktop.

Use "Dump resolver comparison" to see quirk applications in the generated report file (limited to 5 sample entries).

All debug output is saved to Desktop via `ST_FileIO` for examination.

## Integration Tips

1. Register quirks in your mod's initialization (e.g., static constructor or patch)
2. Use descriptive tags to help with debugging
3. Predicates should be fast since they're called frequently
4. Quirk actions should be safe and handle edge cases
5. Multiple quirks can apply to the same tool - they stack in registration order
6. **Deterministic Order**: Quirks are processed in exact registration order
7. **No Allocations**: Quirk application uses pre-allocated lists for zero GC pressure
8. **Internal API**: Only use `CompatAPI.RegisterToolQuirk()` - resolver methods are internal
