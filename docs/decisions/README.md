# Architecture Decisions & Deferred Work

Each file documents one decision tier — what we chose, what we deferred, and why. Files are ordered from foundational (runtime model) up to specific subsystem choices (authority model).

## Decision Index

| Tier | Decision | Status | File |
|---|---|---|---|
| Runtime Model | Coroutines over ECS | **Decided** | [001-runtime-model.md](001-runtime-model.md) |
| Event Architecture | Centralized service over distributed observers | **Decided (stage 1)** | [002-event-architecture.md](002-event-architecture.md) |
| Achievement Authority | Backend confirmation through an authority interface | **Client implemented; backend verification not audited** | [003-authority-model.md](003-authority-model.md) |

## Deferred Work Summary

| Item | Blocked by | Effort | File |
|---|---|---|---|
| Observer pattern for multi-subsystem event reactions | Need 2+ independent subsystems consuming same events | Medium | [002-event-architecture.md](002-event-architecture.md) |
| Backend achievement validation audit and rejection reconciliation | Inspect backend condition checks and add integration coverage | To assess | [003-authority-model.md](003-authority-model.md) |
| Server-authoritative evaluation pipeline | Backend architecture decision + event tracking infrastructure | Large | [003-authority-model.md](003-authority-model.md) |
