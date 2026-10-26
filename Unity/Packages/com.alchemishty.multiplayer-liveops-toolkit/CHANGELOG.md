# Changelog

## 0.1.0-preview.1

- Extract existing engine-free mechanics into one UPM package, preserving source metadata.
- Separate UnityIntegration, Backends.Django and Netcode assemblies and public namespaces.
- Require explicit Django renewal endpoints; consuming-game preferences are untouched.
- Include portable mechanic/adapter tests and the authored Full Demo sample.
- Keep sample art and UI outside runtime.

- Add optional EquipmentVisuals: configurable string slots, asset catalog, bone/skin attachments, base restoration, pooling and replaceable renderer/presenter. The full demo consumes this package integration.

- Package verified avatar spawning/identity/equipment/lifecycle, session-local registry and optional character controls.
- Add explicit Django module composition and client/host connection policy, consumed by the full demo.
- Ship one Full Demo sample with the saved scene, character/cosmetics, animation, catalogs and UI. Remove Game Starter, Minimal TMP Client and both scene generators.
