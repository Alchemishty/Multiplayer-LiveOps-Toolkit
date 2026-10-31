# Import the .unitypackage

The classic archive installs runtime and Full Demo into `Assets/MultiplayerLiveOpsToolkit/`. Follow the canonical [installation guide](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/blob/main/Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/installation.md#import-the-classic-unitypackage) for downloads, dependencies and import steps, then [Full Demo setup](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/blob/main/Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Samples~/FullDemo/README.md#setup).

For Android, continue with [Android setup and build](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/blob/main/Documentation/android.md) after completing backend, account and public-trust configuration.

## Build the distribution from source

From the repository root, run:

```sh
python3 tools/build_unitypackage.py
```

Requires an installed macOS Unity editor; the default is 2022.3.33f1. The tool exports through Unity, checks the archive contents, reimports into an isolated project and builds the imported demo. It removes the temporary project afterward. Output and verification logs are under `Unity/Builds/Distribution/`; `--output` and `--unity` override the archive/editor paths.
