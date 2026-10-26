# Agent and contributor documentation

This folder records how the project is built and how future changes should be made. Reader-facing project information and usage guides belong in [Documentation/](../Documentation/README.md).

| Document | Purpose |
|---|---|
| [Architecture](architecture.md) | Technical layers, state ownership, composition and implementation notes |
| [Conventions](conventions.md) | Coding standards and design rules |
| [Domain](domain.md) | Domain terminology, contracts and Netcode constraints |
| [Testing](testing.md) | Test strategy, regression practices and historical verification notes |

Start with [AGENTS.md](../AGENTS.md). [Enforcement](../enforcement/) remains contributor/agent tooling. Use [project documentation](../Documentation/README.md) for installation, backend operation, multiplayer proof setup and current qualification limits.

Keep these audiences separate: do not put user setup/product guides here, and maintain only current project guidance. Package-local guides stay under the package's Documentation~ directory so they travel with package installation.
