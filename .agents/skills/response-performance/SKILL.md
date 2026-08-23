---
name: response-performance
description: Investigate or improve perceived response time across the TkpSalaryCalculator Android app. Use when users report slow operations, or when working on startup, navigation, data access, calculation, saving, rendering, profiling, or performance regressions.
---

# Improve application responsiveness

Use this skill for performance investigation, optimization, and performance-related review.

## Required context

1. Follow the `tkp-project-docs` skill and read the specifications relevant to the affected behavior.
2. Read [response_performance_procedure.md](../../../docs/response_performance_procedure.md) before investigating or changing performance-sensitive code.
3. Treat existing requirements, accepted ADRs, and test specifications as constraints. Performance work does not authorize changing documented behavior.

## Rules

- Reproduce and measure the reported delay before choosing an optimization whenever practical.
- Distinguish measured bottlenecks from suspected bottlenecks.
- Prefer the smallest change supported by measurement evidence.
- Do not optimize unrelated code merely because it appears inefficient.
- Preserve salary-calculation correctness, settings-history reproducibility, data integrity, cancellation behavior, and offline-only operation.
- Do not weaken tests or documented behavior to improve measured response time.
- Do not introduce telemetry, analytics SDKs, cloud dependencies, or network requirements.
- Use caching only when invalidation and reconstruction from source data are explicit and tested.
- Re-measure affected operations after a performance change and run the relevant correctness tests.
- If representative-device measurement cannot be performed from the current environment, clearly separate provisional investigation results from measurements that still require real-device verification.