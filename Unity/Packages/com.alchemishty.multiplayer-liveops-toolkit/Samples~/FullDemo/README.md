# Full Demo

The complete playable multiplayer demo: Django username/password login, wallet/conversion, categorized shop and sets, inventory, local preview, equipment, achievements/toasts, daily rewards and development reset. It includes the authored modular character, animated movement, cosmetic prefabs, visual catalogs and TMP UI. Reusable mechanics/avatar/rendering/control code comes from the toolkit runtime.

## Setup

First [install the toolkit and editor-specific dependencies](../../Documentation~/installation.md). Then:

1. Use a **Built-in Render Pipeline** project and enable **Input Manager (Old)** or **Both** under Active Input Handling for the supplied controls.
2. Import **Window > TextMeshPro > Import TMP Essential Resources**.
3. For UPM, import **Full Demo** from Package Manager > Multiplayer LiveOps Toolkit > Samples. The classic `.unitypackage` includes it under `Assets/MultiplayerLiveOpsToolkit/FullDemo/`.
4. Open **Scenes/ExampleScene.unity** inside the imported Full Demo folder. Add it to Build Settings (Unity 2022.3) or the active Build Profile (Unity 6) when making a player.
5. Complete the repository's [backend, accounts and public-trust walkthrough](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/blob/main/Documentation/getting-started.md#prepare-the-backend). Export public trust to this project's `Assets/StreamingAssets/LiveOpsIdentityTrust.json`.
6. Press Play and sign in. The first instance tries client then falls back to hosting; a second instance with another account joins it. A server role uses `-liveops-server` with public trust and no local account login.

The backend runs separately from the Unity package. Its default URL is `http://localhost:8000`; call `ApiConfig.SetBaseUrl` before authentication to override it. Keep backend private keys outside Unity. For custom UI and commands, see [service recipes](../../Documentation~/recipes.md).

## Android builds

Full Demo runs on the verified Android 13 ARM64 emulator configuration with Unity 2022.3.33f1, IL2CPP and OpenGL ES 3. Follow [Android setup and build](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/blob/main/Documentation/android.md) for SDK/emulator setup, backend TCP forwarding, public trust, APK settings and desktop multiplayer.

Touch account/shop/equipment UI is verified in landscape; supply your own mobile movement/camera controls. Physical devices and Unity 6 Android remain unqualified.

## Customize

Use the saved scene, prefabs and catalogs directly. Replace character/cosmetic art through the package's CosmeticVisualCatalog, renderer rig/slot bindings and animation controller; skinned replacements need compatible rigs. Replace the example views with your own UI through the service contracts. Keep credentials and private backend configuration out of sample assets.

Do not import this sample into the source repository's demo project: it already contains the same scripts and GUIDs. The sample is intended for a fresh consuming project. Reimporting with overwrite replaces imported files; keep your customizations separate or merge deliberately. For a consumer previously using Game Starter/Minimal TMP Client, remove the obsolete imported sample folders before importing Full Demo to avoid duplicate example classes.

See [demo credits](Models/CREDITS.md) for attribution and licensing.
