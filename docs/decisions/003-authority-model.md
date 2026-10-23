# 003: Achievement Authority — Backend Confirmation through an Interface

**Original decision:** 2026-10-15
**Current implementation checked:** 2026-10-23
**Status:** `IAchievementAuthority` and `ServerValidatedAuthority` implemented in the Unity client; backend condition verification not audited here.
**Scope:** Achievement unlock confirmation and progress persistence.

## Current implementation

`AchievementService` evaluates events locally through `AchievementProgressTracker`. At startup it looks for an `IAchievementAuthority` component on its GameObject. If absent, it adds `ServerValidatedAuthority`.

```text
AchievementService → IAchievementAuthority → ServerValidatedAuthority
                                           → ApiWriteQueue → AchievementAPI → backend
```

`RequestUnlock` queues a POST to `/api/achievements/unlock/` with `achievement_id` and `current_count`. A successful response invokes the confirmation callback; only then does the service emit `AchievementUnlocked`. After the queue exhausts its attempts, the rejection callback logs a warning.

`ReportProgress` queues a POST to `/api/achievements/progress/update/`. Dirty progress is flushed every 30 seconds while pending, on application pause, and during service destruction after loading.

## Authority boundary

The class name `ServerValidatedAuthority` describes the intended backend boundary. The Unity implementation forwards claims and interprets HTTP success/error; it does not independently validate achievement conditions. Backend source and tests must establish whether each condition is verified against authoritative records.

Local evaluation marks progress unlocked before confirmation. The rejection callback currently does not roll back that local state. Confirmation gates the notification, but rejection does not restore eligibility in the local tracker.

The earlier trust-client implementation and proposed `ValidatedAuthority` described in this decision have been superseded in the current client: there is no `TrustClientAuthority` class, and the default is `ServerValidatedAuthority`.

## Consequences and remaining work

- Keep evaluation separate from transport/authority through `IAchievementAuthority`.
- Verify backend checks before relying on achievements to grant economic or competitive rewards.
- Verify rejection/retry behavior with integration tests, including local progress reconciliation.
- `ApiWriteQueue` provides three attempts and in-memory ordering, not durable delivery.
- A fully server-driven event evaluation pipeline is not implemented by this client architecture.
