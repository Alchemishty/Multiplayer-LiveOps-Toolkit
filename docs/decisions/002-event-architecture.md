# 002: Event Architecture — Centralized Service (Stage 1)

**Date:** 2026-10-15
**Status:** Decided (stage 1) — Observer pattern deferred to stage 2
**Scope:** Achievement system, extensible to future event-driven subsystems

## Context

Game systems (shop, equip, login) produce events. The achievement system needs to consume them. The architectural question: how do producers and consumers connect?

## Options Considered

### A. Centralized Achievement Service (Chosen — Stage 1)

Game systems call `AchievementService.Instance.ReportEvent(type, context)`. The service owns all evaluation logic. Producers know nothing about achievements — they just report events.

**Strengths:**
- One integration point per producer (single `ReportEvent` call)
- All evaluation logic in one place — easy to debug, test, and reason about
- Producers don't need to know about consumers
- Matches existing project patterns (`AccessoryRegistry`, `AccessoryEquipService`)

**Weaknesses:**
- Only one consumer per event (achievements). If analytics, audio, or another system also needs to react to "purchase" events, they'd each need their own `ReportEvent` call at the same callsite, or the service becomes a god object routing events to multiple subsystems.
- Adding a new consumer means modifying every producer that should emit to it

### B. Distributed Observer Pattern (Deferred — Stage 2)

Each consumer subsystem registers its own listeners on game events. Achievements subscribe to `AccessoryRegistry.AccessoryOwnershipChanged`, analytics subscribe to the same event, etc.

**Strengths:**
- Multiple independent consumers react to the same event without producers knowing about any of them
- Adding a new consumer doesn't touch any producer code
- Each consumer owns its own reaction logic

**Weaknesses:**
- Each consumer needs to know every event source's API surface
- Event signatures become contracts — changing them breaks all subscribers
- Evaluation scattered across listeners — harder to debug "why did this achievement unlock?"
- For a single consumer (achievements only), this is over-engineered

### C. Event Bus / Message Broker

A central bus where producers publish typed events and consumers subscribe by event type.

**Strengths:**
- Full decoupling — producers and consumers share only the event type definitions
- Scales to many producers and consumers

**Weaknesses:**
- Adds infrastructure (event bus implementation, event type registry)
- Debugging requires tracing through the bus
- For the current toolkit scale, the indirection cost exceeds the decoupling benefit

## Decision

**Centralized service (Stage 1).** We currently have one consumer (achievements) for game events. The centralized pattern is the simplest correct solution and matches existing project patterns.

**This is not a permanent architecture.** It's the right choice for the current scale. When the toolkit grows to have 2+ independent subsystems consuming the same events (e.g., achievements + analytics + audio feedback), the architecture should evolve.

## Deferred: Stage 2 — Observer/Event Bus

**When to build:** When a second independent subsystem needs to react to game events (purchase, login, etc.) alongside achievements. At that point:

1. Extract `GameEventType` + `GameEventContext` into a shared event contract (already done — they're in the Data layer)
2. Create `IGameEventListener` interface with `OnGameEvent(GameEventType, GameEventContext)`
3. Create `GameEventBus` that replaces direct `AchievementService.ReportEvent` calls
4. `AchievementService` implements `IGameEventListener` and registers with the bus
5. New subsystems implement `IGameEventListener` and register independently
6. Producers call `GameEventBus.Publish(type, context)` instead of `AchievementService.Instance?.ReportEvent`

**Estimated effort:** Medium. The Data-layer types already exist. The migration is mechanical — replace `AchievementService.Instance?.ReportEvent` calls with `GameEventBus.Publish`, and have `AchievementService` subscribe instead of being called directly.

**Why not build it now:** YAGNI. The bus infrastructure adds complexity with zero current benefit. Building it before the second consumer exists means guessing at the bus's API without a real use case to validate it.

## Consequences

- Integration points use `AchievementService.Instance?.ReportEvent` (null-conditional — achievements are optional)
- The `GameEventType` enum and `GameEventContext` struct are already in the Data layer, ready for the bus when it's needed
- `IAchievementConditionEvaluator` strategy pattern keeps evaluation extensible regardless of how events arrive
- Adding a second event consumer is the trigger to migrate from centralized to bus
