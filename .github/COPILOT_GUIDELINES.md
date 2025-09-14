# Copilot Guidelines for SurvivalTools

This file is a short, focused summary of the project's coding and repository rules so GitHub Copilot and Copilot Chat can pick them up as repository context. The full, formal rules are in `.continue/rules/` — see that folder for detailed standards.

Key points (short):

- File header: every C# source file under `Source/` must start with the single-line header:
  `// RimWorld 1.6 / C# 7.3`

- Second-line path comment: the second line must be a single-line comment with the repository-relative path (forward slashes), for example:
  `// Source/Compatibility/PrimitiveTools/PTHelpers.cs`

- Debug-only actions: any Dev/debug-only actions (DebugAction methods) must be gated with `#if DEBUG` so they are excluded from Release builds.

- Centralization: cross-cutting behaviors (for example: pacifist equip logic) are centralized in core patches and compat modules should call into the core helpers rather than reimplementing behavior.

- Harmony patches: keep Harmony patches defensive, wrapped in try/catch where appropriate, and do no-ops when the external mod is absent.

- Performance: avoid LINQ in hot loops; prefer simple for-loops in performance-sensitive code.

Useful commands (local):

Run the header/second-line insertion script (applies changes):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ensure-second-line.ps1
```

Dry-run (check only):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ensure-second-line.ps1 -WhatIf
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\fix-missing-system-usings.ps1 -WhatIf
```

CI: this repository contains a rules-check workflow (`.github/workflows/check-rules.yml`) which runs the two scripts in check/dry-run mode and fails the run if either script would modify files. Running that workflow on PRs prevents merges that violate the header/pattern rules.

If you're using Copilot Chat: open this file before asking for changes so Copilot Chat picks up these guidelines as local context. For larger or ambiguous changes, reference the full rules in `.continue/rules/`.

For stricter enforcement (optional): add a Roslyn analyzer and reference it in the solution, or have the CI run `dotnet build` with analyzers enabled.

---

Full rules are in `.continue/rules/` (human readable):

- `.continue/rules/survivaltools-code-structure-standards.md`
- `.continue/rules/survivaltools-harmony-patching-standards.md`
- `.continue/rules/survivaltools-logging-standards.md`
- `.continue/rules/survivaltools-debug-logging-best-practices.md`
- `.continue/rules/survivaltools-mod-compatibility-standards.md`
- `.continue/rules/survivaltools-performance-best-practices.md`
- `.continue/rules/build-rules.md`

If you want me to expand any section into a set of automated checks (Roslyn, CI, or editorconfig rules), tell me which area to prioritize.

---

Full rule documents (copied verbatim) from `.continue/rules/` follow. Keep these as authoritative text instructions.

---

## CONTINUE.md

```markdown
## Survival Tools Reborn

### Project Overview

Survival Tools Reborn is a comprehensive mod for RimWorld that introduces a progression-based tool system with 18 different tools spanning from primitive bone implements to advanced glitterworld technology. The mod enhances colonist efficiency across all work types through a balanced progression system, flexible crafting, and intelligent tool assignment.

**Key Technologies**

- RimWorld (1.6+) with Harmony integration
- C# 7.3 with modern OOP patterns
- Modular design with clear separation of concerns
- Centralized logging and tool scoring system

**High-Level Architecture**

1. Tool classification system
2. Work stat management
3. Tool assignment and validation
4. Debug and analysis utilities
5. Performance optimization

### Getting Started

**Prerequisites**

- RimWorld 1.6+ (minimum)
- Harmony mod (required dependency)
- .NET Framework 4.8

**Installation**

1. Subscribe to the mod on Steam Workshop
2. Ensure Harmony is installed
3. Add to your mod list and enjoy enhanced colony efficiency

**Basic Usage Examples**

- Early game: Use primitive tools (axe, pickaxe) for tree felling and mining
- Mid-game: Switch to industrial tools (steel knife, crosscut saw) for improved construction and butchery
- Late-game: Utilize precision tools (power drill, precision scalpel) for specialized operations

**Running Tests**

- Dev mode: Enable debug logging in mod settings to view detailed tool analysis
- Test tools: Use the built-in debug actions to validate tool behavior

### Project Structure

**Main Directories**

- `Source/Helpers/`: Centralized utility classes for tool classification, scoring, and validation
- `Source/Helpers/WorkSpeedGlobalHelper.cs`: Support for crafting work speed
- `Source/Helpers/ToolScoring.cs`: Comprehensive tool scoring and comparison logic
- `Source/Helpers/StatFilters.cs`: Stat classification and grouping
- `Source/Helpers/PawnToolValidator.cs`: Pawn capability and policy validation
- `Source/Helpers/ToolClassification.cs`: Tool type detection and capability
- `Source/Helpers/ST_Logging.cs`: Centralized logging system with deduplication and cooldown
- `Source/Helpers/CollectionExtensions.cs`: Safe collection operations
- `Source/Helpers/SafetyUtils.cs`: Null-safe operations
- `Source/Helpers/ConditionalRegistration.cs`: Feature toggling system

**Key Files**

- `Source/Helpers/ST_Logging.cs`: Centralized logging with debug/compat logging, cooldown system, and per-pawn-per-job gating
- `Source/Helpers/ToolScoring.cs`: Tool scoring and comparison logic with quality and condition weighting
- `Source/Helpers/StatFilters.cs`: Stat classification system with priority scoring and grouping
- `Source/Helpers/PawnToolValidator.cs`: Pawn capability validation for tool use and assignment
- `Source/Helpers/ToolClassification.cs`: Tool type detection based on name patterns and stat improvements
- `Source/Helpers/CollectionExtensions.cs`: Safe collection operations and dictionary utilities
- `Source/Helpers/SafetyUtils.cs`: Null-safe operations and exception handling
- `Source/Helpers/ConditionalRegistration.cs`: Feature toggling based on user settings

**Important Configuration Files**

- `CHANGES_SUMMARY.md`: Major update documentation
- `README.md`: Project overview and installation
- `RR_Implementation_Summary.md`: Research Reinvented integration details

### Development Workflow

**Coding Standards**

- Clear, descriptive method names
- Consistent null checks and defensive programming
- Comprehensive XML comments for public APIs
- Separation of concerns between helper classes
- Centralized logging with deduplication

**Testing Approach**

- Unit tests for core logic (tool scoring, classification)
- Integration tests for tool assignment and validation
- Debug mode analysis tools for edge case validation

**Build and Deployment**

- Built as a standard RimWorld mod with zip distribution
- Versioning follows SemVer
- No complex deployment pipeline required

**Contribution Guidelines**

- Submit issues and suggestions on Steam Workshop
- PRs welcome with clear descriptions
- Follow existing code patterns and style

### Key Concepts

**Domain-Specific Terminology**

- Tool classification: How tools are categorized by function
- Work stat: Performance metric for each work type
- Survival tool: Any tool that improves work efficiency
- Tool assignment: How tools are assigned to pawns
- Quality category: Tool durability and performance tier

**Core Abstractions**

- Tool classification system
- Work stat management
- Tool scoring and comparison
- Pawn capability validation

**Design Patterns Used**

- Strategy pattern (tool classification)
- Factory pattern (tool type detection)
- Observer pattern (logging system)
- Decorator pattern (tool quality weighting)

### Common Tasks

**Step-by-Step Guides**

1. **Tool Classification**: Identify which tools improve which work types

- Use `ToolClassification.ClassifyToolKind()` with `Thing.def` or `ThingDef`
- Check for specific stat improvements (digging, tree felling, etc.)

2. **Tool Scoring**: Calculate tool effectiveness for a pawn

- Use `ToolScoring.CalculateToolScore()` with `SurvivalTool`, `Pawn`, and relevant `StatDef`s
- Tool score is based on actual improvement over no-tool baseline
- Quality and condition have multiplicative effects

3. **Pawn Validation**: Check if a pawn can use tools

- Use `PawnToolValidator.CanUseSurvivalTools()`
- Returns true if pawn is humanlike, not dead, and in player faction

4. **Work Stat Grouping**: Categorize work stats by function

- Use `StatFilters.GroupStatsByCategory()`
- Returns dictionary with categories like Mining, Construction, Farming, etc.

5. **Debug Analysis**: Test tool behavior in development mode

- Enable debug logging in mod settings
- Use debug actions like `FindWorkSpeedGlobalJobs` to analyze work givers

### Troubleshooting

**Common Issues and Solutions**

- "Hardcore mode appears enabled" → Fixed by normal mode penalty system
- Tool assignment not working → Check pawn validation and tool classification
- No tool alerts → Verify alert system settings and work stat configurations

**Debug Tips**

- Enable debug logging in mod settings to see detailed messages
- Use the debug actions in dev mode to validate tool behavior
- Check tool properties in the database for correct stat factors

### References

**Documentation**

- [RimWorld Documentation](https://rimworld.fandom.com/wiki/RimWorld)
- [Harmony API](https://github.com/Unity-Technologies/Harmony)
- [RimWorld Modding Guide](https://rimworld.fandom.com/wiki/Modding)

**Key Resources**

- `ST_Logging.cs`: Central logging system
- `ToolScoring.cs`: Tool scoring and comparison logic
- `StatFilters.cs`: Stat classification and grouping
- `PawnToolValidator.cs`: Pawn capability validation
- `ToolClassification.cs`: Tool type detection

**Next Steps**

- Add new tool types by following existing patterns
- Extend work stat support
- Improve tool assignment algorithms
- Add new debug features

> Transform your colony's efficiency with the right tools for every job!
```

## survivaltools-code-structure-standards.md

```markdown
---
description: Ensures consistent code organization and maintainability across the mod.
---

All new code should follow the existing project structure patterns. Files should be organized by feature area (AI, Harmony, Helpers, etc.) and class names should clearly indicate their purpose. Public APIs should be well-documented with XML comments, and internal methods should use clear naming conventions to distinguish them from public members.
```

## survivaltools-harmony-patching-standards.md

```markdown
---
description: Ensures consistent and reliable Harmony patching across the mod.
---

All Harmony patches should be properly documented with clear purpose statements. Patches should be minimal and focused, avoiding unnecessary code changes. Use appropriate prefix/postfix/transpiler patterns, and ensure all patches are properly registered in the HarmonyPatches.cs file. All patch methods should be clearly marked and tested for compatibility.
```

## survivaltools-logging-standards.md

```markdown
---
description: This rule ensures consistent logging practices across the mod and
  proper routing of non-essential logs through the centralized system.
---

All non-essential logging in the Survival Tools mod must use the centralized ST_Logging system. Debug logs should be wrapped with IsDebugLoggingEnabled checks, and compatibility logs with IsCompatLogging() checks. Essential operational messages should use Log.Message(), Log.Warning(), or Log.Error() directly.
```

## survivaltools-debug-logging-best-practices.md

```markdown
---
description: Ensures debug logging is used appropriately without performance
  impact or information leakage.
---

Debug logging should be used sparingly and only for development purposes. All debug logs should be wrapped with appropriate IsDebugLoggingEnabled checks. Avoid logging sensitive information or excessive detail that could impact performance. Use descriptive log keys for deduplication and cooldown functionality.
```

## centralized-logging-rule.md

```markdown
---
description: Ensure all debug and informational logging goes through the
  ST_Logging system instead of direct Log calls for better control over output
  levels and performance
---

All non-essential logging in the Survival Tools mod must be routed through the centralized ST_Logging system to ensure proper message filtering, deduplication, and performance management.
```

## survivaltools-mod-compatibility-standards.md

```markdown
---
description: Maintains good compatibility practices with other mods.
---

All compatibility checks should be implemented using the existing ModCompatibilityCheck system. When adding new mod compatibility, use the appropriate patterns from CompatibilityRegistry.cs and ensure proper fallback behavior when mods are not present. All compatibility code should be wrapped in appropriate conditional logic.
```

## survivaltools-performance-best-practices.md

```markdown
---
description: Maintains good performance for the mod by avoiding common performance pitfalls.
---

Critical performance paths should avoid expensive operations like repeated DefDatabase lookups or complex LINQ queries. Cache frequently accessed data where appropriate, and use lazy initialization for expensive computations. All methods that might be called frequently during gameplay should be optimized for performance.
```

## build-rules.md

```markdown
Always use .vscode/build.bat Debug to build so user does not have to click unless user specifically requests a release build, or has the environment set to Release.
```

## survivaltools-setting-management.md

```markdown
---
description: Ensures consistent and proper management of mod settings.
---

All mod settings should be properly initialized and managed through the SurvivalToolsSettings system. Settings should have appropriate default values, and all access to settings should go through the centralized settings accessor. Avoid direct access to Prefs or other global settings where possible.
```
