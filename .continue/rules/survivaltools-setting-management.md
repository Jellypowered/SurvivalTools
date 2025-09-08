---
description: Ensures consistent and proper management of mod settings.
---

All mod settings should be properly initialized and managed through the SurvivalToolsSettings system. Settings should have appropriate default values, and all access to settings should go through the centralized settings accessor. Avoid direct access to Prefs or other global settings where possible.