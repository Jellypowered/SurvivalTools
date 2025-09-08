---
description: This rule ensures consistent logging practices across the mod and
  proper routing of non-essential logs through the centralized system.
---

All non-essential logging in the Survival Tools mod must use the centralized ST_Logging system. Debug logs should be wrapped with IsDebugLoggingEnabled checks, and compatibility logs with IsCompatLogging() checks. Essential operational messages should use Log.Message(), Log.Warning(), or Log.Error() directly.