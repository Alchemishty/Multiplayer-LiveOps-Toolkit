# Multiplayer LiveOps Toolkit

Development preview for Unity **2022.3**. One package supplies selectively initialized Identity, Wallet, Shop, cosmetic Inventory, Equipment, Achievements, Daily Rewards, Unity session/context integration, reference Django adapters and a Netcode identity/equipment gate.

Runtime does not depend on a demo scene, UI, avatar, prefabs, fonts, matchmaking or a running backend at import time. Only explicitly selected account modules initialize and make requests. The host game owns scene transitions, presentation and connection destinations.

See [integration and contracts](Documentation~/index.md). The optional **Full Demo** sample includes the working scene, modular character, cosmetics, catalogs and UI. It requires TMP 3.0.6, uGUI 1.0.0 and TMP Essentials; those are sample dependencies, not runtime requirements. Open its saved ExampleScene after import. The Django server stays in the source repository.

Demo art is confined to the optional sample; see [third-party notices](Samples~/FullDemo/Models/CREDITS.md). TMP Essentials/fonts are imported separately from Unity.

[Equipment visuals](Documentation~/equipment-visuals.md) provides configurable mesh/skinned cosmetics, avatar bindings, pooling and local preview/public equipment presentation. Supply your own character/cosmetic assets or use the Full Demo sample.

[Full Demo setup](Samples~/FullDemo/README.md) describes importing the complete playable example. [Avatar integration](Documentation~/full-demo.md) covers the reusable package pieces.

Toolkit code is licensed under [MIT](LICENSE.md). Demo art and dependencies retain their own licenses.
