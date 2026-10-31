# Multiplayer LiveOps Toolkit

Development preview for Unity **2022.3** and qualified **Unity 6.3 LTS 6000.3.27f1**. One package supplies selectively initialized Identity, Wallet, Shop, cosmetic Inventory, Equipment, Achievements, Daily Rewards, Unity session/context integration, reference Django adapters and a Netcode identity/equipment gate.

Runtime does not depend on a demo scene, UI, avatar, prefabs, fonts, matchmaking or a running backend at import time. Only explicitly selected account modules initialize and make requests. The host game owns scene transitions, presentation and connection destinations.

Start with [installation](Documentation~/installation.md), then [composition and contracts](Documentation~/index.md) and [service recipes](Documentation~/recipes.md). The optional [Full Demo](Samples~/FullDemo/README.md) includes the working scene, modular character, cosmetics, catalogs and UI. Its backend runs separately from the package.

Demo art is confined to the optional sample; see [third-party notices](Samples~/FullDemo/Models/CREDITS.md). TMP Essentials/fonts are imported separately from Unity.

[Equipment visuals](Documentation~/equipment-visuals.md) provides configurable mesh/skinned cosmetics, avatar bindings, pooling and local preview/public equipment presentation. Supply your own character/cosmetic assets or use the Full Demo sample.

[Full Demo setup](Samples~/FullDemo/README.md) describes importing the complete playable example. [Avatar integration](Documentation~/full-demo.md) covers the reusable package pieces.

Android ARM64 builds are verified with Unity 2022.3.33f1, NGO 1.9.1, IL2CPP and OpenGL ES 3 on an Android 13 emulator, including account mechanics and desktop multiplayer. See [Android setup and build](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/blob/main/Documentation/android.md). Physical devices, Unity 6 Android and iOS remain unqualified.

Toolkit code is licensed under [MIT](LICENSE.md). Demo art and dependencies retain their own licenses.
