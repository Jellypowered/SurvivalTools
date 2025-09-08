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