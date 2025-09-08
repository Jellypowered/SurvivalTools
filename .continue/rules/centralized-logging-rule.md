---
description: Ensure all debug and informational logging goes through the
  ST_Logging system instead of direct Log calls for better control over output
  levels and performance
---

All non-essential logging in the Survival Tools mod must be routed through the centralized ST_Logging system to ensure proper message filtering, deduplication, and performance management.