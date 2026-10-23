# MultiplayerLiveOpsToolkit — Agent Guide

Unity multiplayer live-ops toolkit: accessory system, shop, wallet, real-time avatar sync over Netcode for GameObjects, REST API backend.

## Agent Rules

1. **Never silently consume null.** If a value must be non-null for the mechanism to work, throw an `InvalidOperationException` or `ArgumentNullException` with a message identifying what's missing and where. `if (x == null) return;` is forbidden when x being null means the system is broken.
2. **SOLID principles are mandatory, not aspirational.** One class = one responsibility. Depend on abstractions. Prefer small interfaces over fat base classes.
3. **Variable names must immediately tell the reader what they represent.** `playerAccessory` not `pa`. `equippedItemsBySlot` not `eqMap`. `canAffordSelectedItem` not `afford`. No abbreviations except universally understood ones (`id`, `url`, `api`).
4. **Use appropriate data structures** (object pools, dictionary lookups, component caches) where they increase efficiency over naive implementations.
5. **All code changes verified through Unity compiler** — `refresh_unity` → `read_console` must show zero errors before moving on.
6. **Read `docs/` before writing any new code.** Architecture, conventions, domain, and testing docs describe this specific project.
7. **Every code change must include tests.** Bug fix? Reproduce with a test first. Refactor? Prove behavior is preserved. Feature? TDD. EditMode for pure logic, PlayMode for MonoBehaviour/NetworkBehaviour.
8. **Fix adjacent enforcement violations.** When working near code that violates enforcement rules, fix the violations if the fix is safe and obvious. If it's not safe, report it. Don't leave known broken code next to your fix.
9. **Git operations require user authorization.** Local review is allowed. Commit, branch, push, or open PRs when requested by the user; never force-push or rewrite history without explicit authorization.
10. **When in doubt, throw an error** rather than degrade silently. Fail loud, fail early.
11. **Bug fixes are not refactoring opportunities.** Fix the root cause with minimal changes. No new classes, file renames, or architectural restructuring during a bug fix. Enforcement violation fixes near the bug are the only exception.
12. **`SetupExampleScene.cs` is the scene source of truth.** Running `Tools/Setup Example Scene` rebuilds the entire scene from scratch — all GameObjects, components, serialized field references, and UI hierarchy are created programmatically. Any feature that adds a new singleton, panel, tab, accessory, or wired reference **must** update the script. Never rely on manual inspector changes surviving a scene rebuild. If you add something to the scene, add it to the script in the same change.

## Backend

The Django REST backend is at `backend/` in this repository. Run backend commands from `backend/` and use `.venv/bin/python`. Seed: `manage.py seed`. Run: `manage.py runserver`. Test: `manage.py test shop`. Features that add API endpoints must update both the backend and Unity client.

## Architecture

See [docs/architecture.md](docs/architecture.md) — layer model, bootstrap ordering, singleton policy, scene composition contracts.

## Conventions

See [docs/conventions.md](docs/conventions.md) — the authority on coding standards: null safety patterns, SOLID in Unity, data lifecycle boundaries, naming rules, performance red flags, async decision ladder.

## Domain

See [docs/domain.md](docs/domain.md) — multiplayer live-ops domain glossary, Netcode rules and pitfalls, API contracts, state ownership model.

## Testing

See [docs/testing.md](docs/testing.md) — Unity Test Framework strategy, EditMode vs PlayMode classification, what to extract for testability.

## Enforcement

See [enforcement/](enforcement/) — non-negotiable rules checked after code is written. Violations must be fixed before proceeding:
- [null-safety.md](enforcement/null-safety.md)
- [layer-imports.md](enforcement/layer-imports.md)
- [naming.md](enforcement/naming.md)
- [serialization-safety.md](enforcement/serialization-safety.md)
- [netcode-lifecycle.md](enforcement/netcode-lifecycle.md)
- [event-cleanup.md](enforcement/event-cleanup.md)
- [editor-runtime.md](enforcement/editor-runtime.md)
- [solid.md](enforcement/solid.md)
- [serialized-field-rename.md](enforcement/serialized-field-rename.md)
- [test-coverage.md](enforcement/test-coverage.md)
- [scope-discipline.md](enforcement/scope-discipline.md)
- [state-safety.md](enforcement/state-safety.md)

## Code Review

Use `/code-review` after implementation to get a fresh-context review against all enforcement rules. Every rule in `enforcement/` is a hard gate — violations must be fixed.

## Memory

Cross-session agent learning in [memory/](memory/). Read at session start, write after completing features and review cycles. Knowledge compounds across sessions.

## Harness Config

All commands, thresholds, and rules: [harness.yaml](harness.yaml)

## Skills

| Command | Purpose |
|---|---|
| `/implement-feature` | Orchestrator: plan → TDD → verify → cleanup → retrospective |
| `/create-tests` | Generate EditMode/PlayMode tests for existing code |
| `/deslop` | Code cleanup: comments, naming, dead code, simplification |
| `/garden` | Entropy scan: docs freshness, test gaps, unused code, memory freshness |
| `/fix-bug` | Systematic debugging: reproduce → root cause → test → fix → verify |
| `/retrospective` | Post-implementation learning capture to memory/ |
