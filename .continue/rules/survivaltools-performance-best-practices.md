---
description: Maintains good performance for the mod by avoiding common performance pitfalls.
---

Critical performance paths should avoid expensive operations like repeated DefDatabase lookups or complex LINQ queries. Cache frequently accessed data where appropriate, and use lazy initialization for expensive computations. All methods that might be called frequently during gameplay should be optimized for performance.