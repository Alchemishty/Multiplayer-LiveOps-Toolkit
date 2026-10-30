# Unity 6 setup

The toolkit supports **Unity 2022.3 and Unity 6.3 LTS**. Unity 6 has been tested with **6000.3.27f1 on macOS Apple Silicon**. The repository demo remains on Unity 2022.3; use a separate project for Unity 6.

## Set up the Full Demo

1. Create a **3D Built-in Render Pipeline** project in Unity 6.
2. Open **Package Manager**, choose **Install package from disk** and select `Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/package.json` from this repository. Alternatively, follow the [`.unitypackage` import guide](unitypackage.md). Use one installation format per project.
3. Install **uGUI 2.0.0**. It includes TextMeshPro in Unity 6; do not install the separate legacy TextMeshPro package. Choose **Window > TextMeshPro > Import TMP Essential Resources**.
4. For a Package Manager installation, import **Full Demo** from the toolkit's **Samples** section. Open its `Scenes/ExampleScene.unity`. The classic `.unitypackage` already includes this demo.
5. Set **Project Settings > Player > Other Settings > Active Input Handling** to **Input Manager (Old)** or **Both** for the supplied desktop controls. Restart Unity if prompted.
6. [Run the Django backend and create a player account](getting-started.md#run-the-backend), then [configure public verification trust](multiplayer-identity-verification.md#backend-configuration). The package does not start the backend or register accounts. Keep private signing keys outside Unity.
7. Press **Play** and sign in. To build, add the imported ExampleScene to the active Build Profile's scene list. Test multiplayer with two instances signed in to different accounts.

The reference backend defaults to `http://localhost:8000`; use the demo's `ApiConfig.SetBaseUrl` to change it. Non-loopback credential requests require HTTPS.

## Using the package in your own game

The Full Demo and its UI dependencies are optional. Follow [package integration](package-integration.md) to select modules, replace the backend or build your own UI. The reusable runtime does not require TMP or uGUI.

The demo uses the **Built-in Render Pipeline**; URP/HDRP need appropriate material setup. Unity 6 desktop testing currently covers macOS Apple Silicon. Other Unity 6 releases, operating systems and the dedicated-server build target need separate validation. [Android support](android.md) was tested with Unity 2022.3, not Unity 6.
