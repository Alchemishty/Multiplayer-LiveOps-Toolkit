# Changelog

## 0.1.0

- Qualify Unity 6.3 LTS 6000.3.27f1 with NGO 2.13.3; reveal existing verified avatars before spawning a new player so late joiners receive them on NGO 2.
- Document Unity 6 bundled TextMeshPro/uGUI setup while retaining the Unity 2022.3 minimum.

- Extract existing engine-free mechanics into one UPM package, preserving source metadata.
- Separate UnityIntegration, Backends.Django and Netcode assemblies and public namespaces.
- Require explicit Django renewal endpoints; consuming-game preferences are untouched.
- Include portable mechanic/adapter tests and the authored Full Demo sample.
- Keep sample art and UI outside runtime.

- Add optional EquipmentVisuals: configurable string slots, asset catalog, bone/skin attachments, base restoration, pooling and replaceable renderer/presenter. The full demo consumes this package integration.

- Package verified avatar spawning/identity/equipment/lifecycle, session-local registry and optional character controls.
- Add explicit Django module composition and client/host connection policy, consumed by the full demo.
- Ship one Full Demo sample with the saved scene, character/cosmetics, animation, catalogs and UI. Remove Game Starter, Minimal TMP Client and both scene generators.
