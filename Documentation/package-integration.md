# Package integration

The reusable toolkit is the UPM package at `Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/`. The repository demo under `Unity/Assets/` consumes it; the reference backend is under `backend/`.

## Install and run

Use the package's [installation guide](../Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/installation.md) for installation formats, editor dependencies and updates. The [Full Demo setup](../Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Samples~/FullDemo/README.md#setup) covers sample import and scene selection. [Run the demo](getting-started.md#prepare-the-backend) supplies backend, account and public-trust configuration.

## Build your own integration

| Task | Guide |
|---|---|
| Select modules and initialize an account | [Composition and contracts](../Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/index.md) |
| Bind UI, purchase, preview/equip or claim rewards | [Service recipes](../Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/recipes.md) |
| Replace a backend | [Backend adapter recipe](../Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/recipes.md#implement-a-backend-adapter) |
| Replace a character or add cosmetics | [Equipment visuals](../Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/equipment-visuals.md) |
| Compose verified spawning and public appearance | [Avatar integration](../Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/full-demo.md) |
| Configure signing, trust or protocol compatibility | [Verification reference](multiplayer-identity-verification.md) |

Runtime services do not depend on the sample UI. Explicitly select the modules your game needs, retain account commands in a durable owner, and dispose the account context at teardown. Alternative backends implement the mechanic interfaces; they need not use Django's HTTP session.

## Package and demo maintenance

The [technical architecture](../docs/architecture.md#assembly-and-directory-boundaries) defines assembly ownership and dependencies. Runtime assemblies must not reference demo types, TMP or uGUI. Preserve script metadata/GUIDs when moving sources.

Saved demo scenes, prefabs and catalogs are authoritative. After editing demo assets/scripts, run `python3 tools/sync_full_demo.py` and verify with `--check`. Import Full Demo only into consuming projects; the repository already contains those types/GUIDs.

For layout, consumer-installation and native integration checks, see [testing](../docs/testing.md#running-current-checks). Imported sample files belong to the consuming game; ordinary package updates must preserve consumer-owned scenes/configuration. Future API-breaking updates need explicit migration guidance.
