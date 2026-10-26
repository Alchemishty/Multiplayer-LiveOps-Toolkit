# Roadmap

The current toolkit includes the account modules, cosmetic rendering, multiplayer avatar integration, Full Demo sample and Django reference backend described in the [README](../README.md). This checklist tracks remaining work, without treating completed module extractions as future features. No release dates are assigned.

## Current gaps and platform coverage

- [ ] Detect idle backend outages and disconnect gracefully. Authentication/renewal and handshake failures already disconnect; there is no separate idle-health monitor.
- [ ] Qualify Windows client builds and two-instance multiplayer execution.
- [ ] Define and qualify Linux/server operating-system targets and Unity's dedicated-server build target. Existing headless checks use a macOS desktop player with `-batchmode -nographics`.

## Planned expansion

- [ ] Add and qualify Unity 6 support. The current supported editor line is Unity 2022.3.
- [ ] Expand Inventory beyond cosmetic ownership. Define the required item mechanics before choosing their implementation.
- [ ] Expand admin/dashboard editing for backend configuration currently authored in code, including currency conversion rates and equipment slot/conflict rules. A dedicated content dashboard can build on this.

## Scope boundaries

Self-service registration, authoritative gameplay simulation and a general gameplay-event ingestion pipeline are not currently planned implementations. Add them when a concrete game requirement justifies the design.

The Django backend remains a reference implementation. Production hosting, database/load qualification and game-specific economy bookkeeping are responsibilities of the consuming game; transaction receipts/history are not implied roadmap commitments.

See [architecture decisions](architecture-decisions.md) for the reasons behind the current authority, verification and recovery choices, and [backend operations](backend-operations.md) for current content authoring.
