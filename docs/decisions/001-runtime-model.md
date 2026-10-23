# 001: Runtime Model — Coroutines over ECS

**Date:** 2026-10-15
**Status:** Decided
**Scope:** Entire toolkit

## Context

The toolkit needs an async execution model for backend API calls, event processing, and UI lifecycle management. Unity offers three main options: coroutines (built-in), async/await (C# native), and ECS (Entity Component System via Unity DOTS).

## Options Considered

### A. Coroutines (Chosen)

Unity's built-in `IEnumerator` + `yield return` pattern. Already used throughout the codebase for all API calls (`SendWithTokenRefresh`), UI wait-and-populate patterns, and timed operations.

**Strengths:**
- Zero additional dependencies
- Familiar to any Unity developer
- Natural integration with `MonoBehaviour` lifecycle
- All existing code uses this pattern — consistency

**Weaknesses:**
- No return values from coroutines (callback pairs required)
- Error handling requires manual try/catch patterns
- Coroutine lifecycle tied to `MonoBehaviour` — destroyed object kills its coroutines
- No composition (can't easily chain or combine coroutines)

### B. Async/Await (UniTask)

C# native async/await, typically via the UniTask library for Unity-compatible scheduling.

**Strengths:**
- Return values, exception propagation, `Task.WhenAll` composition
- Cleaner code for sequential async operations

**Weaknesses:**
- Adds a package dependency (UniTask)
- Mixing with existing coroutine code creates two async patterns in one codebase
- Would require rewriting all existing API clients

### C. ECS (Unity DOTS)

Data-oriented design with Systems processing component arrays.

**Strengths:**
- High performance for thousands of entities
- Natural fit for "process all achievements every frame" pattern
- Clean separation of data and behavior

**Weaknesses:**
- Massive architectural shift — incompatible with existing `MonoBehaviour`-based codebase
- Overkill for the scale of this toolkit (dozens of achievements, not thousands of entities)
- Steep learning curve for toolkit consumers
- DOTS packages still evolving

## Decision

**Coroutines.** The entire codebase already uses this pattern. Introducing ECS or async/await for a single subsystem would create an architectural inconsistency that hurts toolkit consumers more than coroutine limitations hurt us. The achievement system processes events synchronously (pure C# evaluation) and only uses coroutines for API calls — the same pattern every other system in the toolkit already follows.

## Consequences

- API calls use callback pairs: `Action<T> onSuccess, Action<string> onError`
- Timed operations (toast display, progress sync debounce) use `WaitForSeconds`
- Error handling in async flows requires explicit try/catch or state-safety patterns (try/finally on flags)
- If the toolkit eventually migrates to async/await, the coroutine-to-async migration is mechanical (one pattern replacement), not architectural

## When to Revisit

If the toolkit adopts UniTask or async/await for other systems, migrate achievement API calls to match. The pure C# evaluation layer (`AchievementProgressTracker`, evaluators) is async-agnostic and requires no changes.
