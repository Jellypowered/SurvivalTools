---
description: Maintains good compatibility practices with other mods.
---

All compatibility checks should be implemented using the existing ModCompatibilityCheck system. When adding new mod compatibility, use the appropriate patterns from CompatibilityRegistry.cs and ensure proper fallback behavior when mods are not present. All compatibility code should be wrapped in appropriate conditional logic.