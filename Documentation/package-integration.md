# Package integration

The toolkit is now authored as one local UPM package at `Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit`, version `0.1.0`, for Unity 2022.3. The existing game under Unity/Assets consumes that package; the backend stays at `backend/`. The Full Demo includes its art credits and license links.

## Boundaries

| Location | Responsibility |
|---|---|
| `Runtime/Core` | Explicit module selection, dependency validation, lifecycle and rollback |
| `Runtime/Identity`, `Wallet`, `Inventory`, `Shop`, `Equipment`, `Achievements`, `DailyRewards` | Engine-free mechanics and immutable service contracts |
| `Runtime/UnityIntegration` | PlayerContext, authenticated HTTP session/response contract; no Django dependency |
| `Runtime/Backends/Django` | Reference backend adapters, in-memory token session and Django password provider |
| `Runtime/EquipmentVisuals` | Optional configurable mesh/skinned renderer, catalog/rig/slot bindings, pooling and preview/public presentation |
| `Runtime/Netcode` | Verified identity/equipment gate; no player prefab, art or UI dependency |
| `Runtime/Netcode.Avatars` | Verified spawning, public identity/equipment and session-local avatar lifecycle/lookup |
| `Runtime/CharacterControls` | Optional movement, transform/animation replication and third-person camera |
| `Tests` | Portable mechanic/adapter tests, enabled through UPM testables |
| `Samples~/FullDemo` | Full playable demo scene, modular character/cosmetics, catalogs and TMP UI |
| Repository `Unity/Assets` | Existing full game demo, visual mappings, sample command controllers, composition and demo tests |

Script/folder metadata moved with sources to preserve GUID bindings. Runtime assemblies never reference the demo assembly. Unity/Django/Netcode integration types now use `MultiplayerLiveOpsToolkit.UnityIntegration`, `.Backends.Django` and `.Netcode`; mechanic namespaces stay unchanged. There are no duplicated SDK source copies in Unity/Assets.

For a classic asset import instead, use the [`.unitypackage` release and guide](unitypackage.md). Choose one installation format per project.

## Install locally

Copy the package directory to your private package location, or add it from Package Manager > + > Add package from disk and select its package.json. A manifest reference uses `"com.alchemishty.multiplayer-liveops-toolkit": "file:/absolute/path/to/package"`. Keep local paths out of a shared consuming game's manifest unless all contributors use that layout; a private Git UPM URL is an option after the repository changes are committed/pushed. This work does not publish or push anything.

Runtime dependencies are Netcode for GameObjects 1.9.1, Newtonsoft JSON 3.2.1 and UnityWebRequest/Animation engine modules. Runtime does not require TMP/uGUI or demo assets. The optional Full Demo sample contains its art, prefabs and presentation; TMP Essentials are imported separately. Installation starts no backend or network session. Only selected modules make account requests. A dedicated verifier uses the Netcode gate and public trust material without a local player account or visuals.

## Compose a consuming game

Create an enabled DjangoPlayerSession, explicitly pass its backend URL/session to adapters, and construct only the required modules. PlayerContext owns Core/Identity and the selected modules; initialize it using DjangoPasswordIdentityProvider and DjangoIdentityBackend. Pass explicitly selected Wallet/Inventory state to transaction modules. Module IDs and service interfaces are documented in package Documentation~. Initialization reports missing dependencies/configuration rather than selecting modules implicitly.

DjangoPlayerSession.SetTokens now requires an explicit renewal endpoint. It never reads ApiConfig or deletes unscoped preferences. The legacy preference migration stays only in the existing demo bootstrap. Create a fresh context/session after termination. Dispose context and owned operations on teardown; closing a view should detach view subscriptions while a durable game owner retains pending account commands.

Custom backends implement the mechanic I*Backend contracts and preserve authoritative atomic results, exact ownership, typed rejections, unknown-outcome refresh and private teardown. Core/Identity can be composed through LiveOpsRuntime without Unity/Django for games with another session integration. The provided PlayerContext/IAuthenticatedSession are the explicit Unity HTTP integration; Firebase SDK consumers need not use Django's HTTP session. Do not introduce a placeholder adapter or silently bypass session/authority semantics.

## Import the Full Demo

Install TextMeshPro 3.0.6 and uGUI 1.0.0 and import TMP Essentials through Window > TextMeshPro. In a fresh Unity 2022.3 Built-in Render Pipeline project, import **Full Demo** from Package Manager > Multiplayer LiveOps Toolkit > Samples. Open `Scenes/ExampleScene.unity` inside the imported sample, configure the reference backend and public proof trust, and press Play. The scene, character/cosmetics, visual catalogs and UI are already wired. No generator or minimal sample is shipped. Do not import the sample into this repository's Assets: it already contains the same scripts/GUIDs.

The Django backend remains at `backend/` in the repository. See [backend operations](backend-operations.md) and [proof setup](multiplayer-identity-verification.md). Private signing keys and account credentials never belong in the sample. Configure public trust in `Assets/StreamingAssets/LiveOpsIdentityTrust.json` or with `LIVEOPS_IDENTITY_TRUST_FILE`.

Imported sample sources belong to the consuming game's Assets. Reimporting with overwrite replaces sample files; keep game customizations separately or use a deliberate merge. Ordinary package updates must not rewrite consumer-owned scenes or configuration. This preview check covers version/path reinstall and stable GUID bindings, not a guarantee about future API-breaking upgrades.

Android builds are supported on the verified Unity 2022.3/ARM64 emulator configuration. After importing Full Demo, follow [Android setup and build](android.md). Unity 6 Android and physical devices remain separate qualification work.

## Verification

Run `python3 tools/test_package_layout.py` for layout/dependency/source-boundary checks. Run `python3 tools/verify_package_consumer.py` with an installed macOS Unity 2022.3.33f1 and backend/.venv for a fresh-project qualification. It creates isolated package copies/project, imports without TMP, runs portable EditMode tests, upgrades a local preview copy while comparing consumer scene/file bytes, imports the sample through Unity's sample API and builds actual SDK/UI native consumers. Django uses an isolated temporary database and ephemeral credentials. The sample is imported using Unity's Package Manager sample API; no direct copy of the source repository Assets is used. Evidence remains under the printed temporary directory; the database is removed.

Run root EditMode/PlayMode tests across both demo and package test assemblies after SDK changes. Keep the saved scene/prefabs as the full demo's composition source of truth. Windows, Asset Store packaging/licensing, final release security/platform checks and compatibility with future breaking API changes remain separate qualification work.

## Visual customization

The package supplies [Equipment visuals](../Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/equipment-visuals.md), independent of UI/backend/networking. The full demo now consumes this renderer. Character models and cosmetic prefabs remain game-owned; configure a catalog, avatar rig and string slot bindings instead of editing a humanoid enum. Custom renderers implement IEquipmentRenderer. Adding a new backend slot still requires matching backend content/configuration; the reference Django admin currently exposes its sample category choices, so custom categories require adapting that authoring configuration.

## Complete demo integration

The package now also supplies Netcode.Avatars (verified spawning/public identity/equipment/lifecycle and a session-local AvatarRegistry), CharacterControls (optional movement/animation/transform/camera) and DjangoPlayerModules (explicit UI-independent reference composition). The repository demo consumes these runtime classes. [Integration guide](../Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/full-demo.md) describes custom composition.

The optional Full Demo sample ships the existing authored scene/art/UI instead of generating placeholder assets. After editing demo assets or scripts, run `python3 tools/sync_full_demo.py` and verify with `--check`. These are example asset copies with preserved GUIDs, not duplicated SDK runtime sources.
