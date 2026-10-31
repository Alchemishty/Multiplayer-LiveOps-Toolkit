# Install the toolkit

Use this guide for a fresh consuming project. To run the source repository's existing demo, use [Run the demo](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/blob/main/Documentation/getting-started.md).

## Choose an installation format

Use **Package Manager (UPM)** for a package dependency with an optional Full Demo sample. Use the **classic `.unitypackage`** for an asset import that includes Full Demo. Install only one format in a project: both contain the same runtime assemblies and asset GUIDs.

Minimum editor: Unity **2022.3**. Qualified editors are **2022.3.33f1** and **Unity 6.3 LTS 6000.3.27f1** on macOS. Use a **3D Built-in Render Pipeline** project for Full Demo; URP/HDRP require material adaptation. See the [support summary](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit#current-support) for platform limits.

## Dependencies

| Package | Unity 2022.3 | Qualified Unity 6.3 |
|---|---|---|
| `com.unity.netcode.gameobjects` | `1.9.1` | `2.13.3` |
| `com.unity.nuget.newtonsoft-json` | `3.2.1` | `3.2.2` |
| `com.unity.textmeshpro` — Full Demo only | `3.0.6` | Included in uGUI; do not install separately |
| `com.unity.ugui` — Full Demo only | `1.0.0` | `2.0.0` |

Keep the standard Animation and UnityWebRequest engine modules enabled. UPM declares runtime dependencies in `package.json`; Unity 6 resolves the qualified newer versions. For the classic asset import, install the matching runtime packages through Package Manager **Install package by name** before importing. Install the UI dependencies only if using Full Demo.

## Install through Package Manager

1. Clone the [source repository](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit) with Git LFS; run `git lfs pull` for demo art.
2. In the consuming project's Package Manager, choose **+ > Install package from disk** (called **Add package from disk** in Unity 2022.3).
3. Select `Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/package.json` in the cloned repository. Allow compilation to finish.

A manifest dependency can instead use `"com.alchemishty.multiplayer-liveops-toolkit": "file:/absolute/path/to/package"`. Keep machine-specific absolute paths out of shared manifests unless all contributors use that layout. Copying the complete package into the consuming project's `Packages/` folder provides an embedded dependency.

The runtime imports without demo assets, TMP or uGUI. Installation does not start a backend or network session. To use your own UI, continue with [account composition](index.md#select-modules-explicitly) and [service recipes](recipes.md).

## Import the classic .unitypackage

1. Download the archive from [GitHub Releases](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/releases).
2. Install the dependencies above for your editor, including the demo UI dependencies.
3. Choose **Assets > Import Package > Custom Package**, select the archive and import its contents.
4. Allow compilation to finish. The toolkit and demo are under `Assets/MultiplayerLiveOpsToolkit/`.

The Django backend is separate and stays in the source repository. The archive contains public toolkit assets and documentation, not a configured backend or private signing key. For distribution tooling, see [build the archive from source](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/blob/main/Documentation/unitypackage.md#build-the-distribution-from-source).

## Run Full Demo

Follow the [Full Demo setup](../Samples~/FullDemo/README.md#setup) for sample import, TMP Essentials, input settings and scene selection. Then complete the [backend, accounts and public-trust walkthrough](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/blob/main/Documentation/getting-started.md#prepare-the-backend) before pressing Play. Export trust to the consuming project's `Assets/StreamingAssets/` directory.

## Updates and tests

Imported samples belong to the consuming game's Assets. Reimporting with overwrite replaces sample files; preserve customizations separately or merge deliberately. Do not import Full Demo into the source repository's own Unity project, which already contains the same scripts and GUIDs. Never edit installed runtime source to configure your game.

To run portable tests through UPM, add `com.alchemishty.multiplayer-liveops-toolkit` to the manifest's `testables` list. Use Test Framework **1.3.9** on Unity 2022.3; the qualified Unity 6.3 configuration resolves **1.6.0**. Run `MultiplayerLiveOpsToolkit.Tests.EditMode` and `.Tests.PlayMode`. Current installation/update qualification covers the preview version; future breaking APIs require migration guidance.
