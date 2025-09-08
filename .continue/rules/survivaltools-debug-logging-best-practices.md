---
description: Ensures debug logging is used appropriately without performance
  impact or information leakage.
---

Debug logging should be used sparingly and only for development purposes. All debug logs should be wrapped with appropriate IsDebugLoggingEnabled checks. Avoid logging sensitive information or excessive detail that could impact performance. Use descriptive log keys for deduplication and cooldown functionality.