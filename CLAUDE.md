# MultiplayerLiveOpsToolkit

Unity multiplayer live-ops toolkit. Accessory system, shop, wallet, real-time avatar sync over Netcode for GameObjects.

Unity project root: `Unity/`. Use it for Unity Hub/CLI; Unity asset paths remain relative to that folder.

## Quick Reference

- **Config:** `harness.yaml` — all commands, thresholds, rules
- **Project guides:** `Documentation/README.md` — reader-facing setup, integration and backend guides
- **Agent guide:** `AGENTS.md` — read this first every session
- **Architecture:** `docs/architecture.md` — layers, bootstrap ordering, singletons
- **Conventions:** `docs/conventions.md` — the authority on coding standards
- **Domain:** `docs/domain.md` — multiplayer domain, Netcode rules, API contracts
- **Testing:** `docs/testing.md` — test strategy, patterns, coverage priorities
- **Enforcement:** `enforcement/` — non-negotiable rules checked after writing code

## Non-Negotiable Rules

1. Never silently consume null — throw when non-null is required
2. SOLID principles are mandatory — enforced, not aspirational
3. Fully descriptive variable names — no abbreviations
4. Every code change must include tests — bug fix, feature, or refactor
5. Fix adjacent enforcement violations when working nearby
6. Commit/push/PR actions require a user request; no force-push or history rewriting without explicit authorization
7. Read `docs/` before making changes — understand the project's architecture first
8. Bug fixes are not refactoring opportunities — minimal changes only
9. **Saved scene/prefabs are authoritative.** Generators have been removed. Update authored demo assets directly, then run `python3 tools/sync_full_demo.py` and verify with `--check` to synchronize the Full Demo sample.

## Unity MCP Tools

You have access to a live Unity editor via MCP tools. Use them:
- After writing/modifying .cs files, use `refresh_unity` to trigger recompilation
- Use `read_console` to check for compilation errors — fix all errors before proceeding
- Use `run_tests` to run EditMode and PlayMode tests
- If MCP tools fail after a domain reload, wait 10 seconds and retry — the bridge reconnects automatically

## Code Review

When reviewing code, check the diff against every file in `enforcement/`. Each rule is a hard gate — violations must be fixed before the change is complete.

## Stack

- Unity (Netcode for GameObjects 1.9.1)
- C# with NUnit tests (Unity Test Framework)
- Newtonsoft.Json for serialization
- REST API backend at localhost:8000
- MCP tools for Unity Editor automation

## Backend

The Django REST backend lives at `backend/` in this repository. It is versioned alongside the Unity client in this repository. Run it with `.venv/bin/python manage.py runserver`. Seed data with `.venv/bin/python manage.py seed`. When adding new features that touch both client and server, update both codebases in the same session.

## Documentation

Read the maintained agent/contributor guidance in `docs/` and the reader-facing guides in `Documentation/`. Update these documents directly alongside project changes. Unity lives under `Unity/`; use the Unity MCP tools or Unity Test Runner for compilation and tests. Harness bootstrap, automatic documentation generation and benchmark runner scripts are not part of this repository.
