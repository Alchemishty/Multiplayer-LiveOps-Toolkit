# Full Demo

The complete playable multiplayer demo: Django username/password login, wallet/conversion, categorized shop and sets, inventory, local preview, equipment, achievements/toasts, daily rewards and development reset. It includes the authored modular character, animated movement, cosmetic prefabs, visual catalogs and TMP UI. Reusable mechanics/avatar/rendering/control code comes from the toolkit runtime.

## Setup in Unity 2022.3 or Unity 6.3 LTS

1. Use a Built-in Render Pipeline project. On Unity 2022.3, install TextMeshPro **3.0.6** and uGUI **1.0.0**. On Unity 6.3, use uGUI **2.0.0**, which includes TextMeshPro, without the separate TextMeshPro package. Enable **Input Manager (Old)** or **Both** under Active Input Handling for the supplied character controls.
2. Import **Window > TextMeshPro > Import TMP Essential Resources**.
3. For UPM, import **Full Demo** from Package Manager > Multiplayer LiveOps Toolkit > Samples. For the classic `.unitypackage`, Full Demo is already included under `Assets/MultiplayerLiveOpsToolkit/FullDemo/`.
4. Open **Scenes/ExampleScene.unity** inside the imported Full Demo folder. Add this scene to Build Settings when making a player.
5. Run the reference Django backend from the toolkit repository's `backend/` folder. Migrate/seed the reference content and create accounts through its admin/management commands. See repository `Documentation/backend-operations.md`. Default URL: `http://localhost:8000`; use the sample's `ApiConfig.SetBaseUrl` to override it.
6. Configure backend proof signing and public verification trust as described in repository `Documentation/multiplayer-identity-verification.md`. Put public trust at `Assets/StreamingAssets/LiveOpsIdentityTrust.json`, or set `LIVEOPS_IDENTITY_TRUST_FILE` before starting Unity/player. Never copy the backend's private signing key into Unity.
7. Press Play and sign in. The first instance tries client then falls back to hosting; a second instance, signed in with a different account, joins it. A server role uses `-liveops-server` with public trust and no local account login.

The backend is a separately hosted sample, not a process installed or started by the Unity package. Source repository: https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit.

## Android builds

Full Demo runs on the verified Android 13 ARM64 emulator configuration with Unity 2022.3.33f1, IL2CPP and OpenGL ES 3. Follow [Android setup and build](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/blob/main/Documentation/android.md) for SDK/emulator setup, backend TCP forwarding, public trust, APK settings and desktop multiplayer.

Touch account/shop/equipment UI is verified in landscape; supply your own mobile movement/camera controls. Physical devices and Unity 6 Android remain unqualified.

## Customize

Use the saved scene, prefabs and catalogs directly. Replace character/cosmetic art through the package's CosmeticVisualCatalog, renderer rig/slot bindings and animation controller; skinned replacements need compatible rigs. Replace the example views with your own UI through the service contracts. Keep credentials and private backend configuration out of sample assets.

Do not import this sample into the source repository's demo project: it already contains the same scripts and GUIDs. The sample is intended for a fresh consuming project. Reimporting with overwrite replaces imported files; keep your customizations separate or merge deliberately. For a consumer previously using Game Starter/Minimal TMP Client, remove the obsolete imported sample folders before importing Full Demo to avoid duplicate example classes.

See [demo credits](Models/CREDITS.md) for attribution and licensing.
