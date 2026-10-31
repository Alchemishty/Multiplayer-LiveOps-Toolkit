# Unity 6 setup

Unity **6.3 LTS 6000.3.27f1** is qualified on macOS Apple Silicon. The repository demo stays on Unity 2022.3; use a separate Unity 6 consuming project.

Follow the [installation guide](../Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/installation.md) for package formats and the editor-specific dependency table, then [Full Demo setup](../Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Samples~/FullDemo/README.md#setup). Unity 6 uses uGUI's bundled TextMeshPro; do not add the separate legacy TMP package.

In Unity 6, add the imported ExampleScene to the active **Build Profile** scene list. Set **Active Input Handling** to **Input Manager (Old)** or **Both** for the supplied controls. Complete [backend, account and public-trust setup](getting-started.md#prepare-the-backend) before entering Play Mode.

The Full Demo uses the Built-in Render Pipeline; URP/HDRP need material adaptation. Other Unity 6 release lines, operating systems and the dedicated-server build target need separate qualification. [Android qualification](android.md) covers Unity 2022.3.
