# Import the .unitypackage

Download the `.unitypackage` from [GitHub Releases](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/releases).

The classic asset package includes the toolkit runtime and Full Demo, with their documentation, license and art credits. It imports into `Assets/MultiplayerLiveOpsToolkit/`.

## Before importing

Use Unity **2022.3** or the qualified **Unity 6.3 LTS 6000.3.27f1** with the Built-in Render Pipeline. Install these dependencies through Package Manager (Add package by name):

| Package | Unity 2022.3 | Unity 6.3 |
|---|---|---|
| `com.unity.netcode.gameobjects` | `1.9.1` | `2.13.3` |
| `com.unity.nuget.newtonsoft-json` | `3.2.1` | `3.2.2` |
| `com.unity.textmeshpro` | `3.0.6` | Included in uGUI; do not install separately |
| `com.unity.ugui` | `1.0.0` | `2.0.0` |

Import **Window > TextMeshPro > Import TMP Essential Resources**. Keep the standard Animation and UnityWebRequest engine modules enabled.

Choose one installation format: the UPM package or this `.unitypackage`. They contain the same runtime assemblies and asset GUIDs; install only one in a project.

## Import and run

1. Choose **Assets > Import Package > Custom Package** and select the `.unitypackage`.
2. Import its contents and allow compilation to finish.
3. Open `Assets/MultiplayerLiveOpsToolkit/FullDemo/Scenes/ExampleScene.unity`.
4. Set up the separately hosted Django reference backend, accounts and public verification trust using the repository's [demo setup](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/blob/main/Documentation/getting-started.md). Default backend URL: `http://localhost:8000`.
5. Press Play and sign in. Run a second instance with another account to test multiplayer.

For an Android APK, follow [Android setup and build](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/blob/main/Documentation/android.md) after import. The verified Android configuration uses Unity 2022.3.33f1, ARM64 IL2CPP and OpenGL ES 3.

The backend and private signing keys are not included in the asset package. Art/character replacement is described in `Documentation/equipment-visuals.md` inside the imported folder.

## Build the distribution from source

From the repository root, run:

```sh
python3 tools/build_unitypackage.py
```

Requires an installed macOS Unity editor; the default is 2022.3.33f1. The tool exports through Unity, checks the archive contents, reimports into an isolated project and builds the imported demo. It removes the temporary project afterward. Output and verification logs are under `Unity/Builds/Distribution/`; `--output` and `--unity` override the archive/editor paths.
