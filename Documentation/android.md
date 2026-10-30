# Build and run on Android

The toolkit supports Android builds. The verified configuration is **Unity 2022.3.33f1, Netcode for GameObjects 1.9.1, ARM64 IL2CPP and OpenGL ES 3**, running on a Pixel 5 Android 13/API 33 ARM64 emulator on macOS Apple Silicon. Account mechanics, Android/desktop multiplayer and Full Demo touch interactions passed. The user approved the Full Demo visuals on October 30, 2026.

Physical Android devices, other Android versions/graphics APIs, Unity 6 Android builds and Play Store distribution still require qualification. Unity 6 is separately qualified on macOS.

## Install the tools

1. In Unity Hub, add **Android Build Support**, including **Android SDK & NDK Tools** and **OpenJDK**, to Unity 2022.3.33f1. In Unity's External Tools preferences, use the tools installed with that editor.
2. Install Android Studio's Device Manager/emulator and Android SDK Platform Tools. Create and start an **ARM64 Android 13/API 33** virtual device on Apple Silicon. The tested device profile was Pixel 5.
3. Put the selected SDK's `platform-tools` directory on your shell's PATH so `adb` is available. Run `adb devices`; the emulator must appear as `device`.
4. Open the repository's `Unity/` folder, or import Full Demo into a Built-in Render Pipeline project using [package integration](package-integration.md). Import TMP Essentials for a fresh consumer project.

Use one ADB installation consistently. Unity and Android Studio can use different SDKs; switching between their ADB versions can restart the server and remove forwarding rules.

## Configure the backend and public trust

Follow [Run the demo](getting-started.md) to migrate/seed Django, configure proof signing and create a player account. Run the backend on the development computer; it is not bundled in the APK.

With the backend's signing environment loaded, export **public** verification trust from the repository's `backend/` directory:

```sh
mkdir -p ../Unity/Assets/StreamingAssets
.venv/bin/python manage.py export_identity_trust > ../Unity/Assets/StreamingAssets/LiveOpsIdentityTrust.json
```

For a fresh consumer project, use that project's `Assets/StreamingAssets/` directory instead. Keep the private signing key on the backend. Android reads the public file from the APK through UnityWebRequest; a host shell's `LIVEOPS_IDENTITY_TRUST_FILE` variable is not automatically available inside an Android app. Rebuild after changing bundled public trust.

The sample defaults to `http://localhost:8000`. On Android, localhost means the emulator/device itself. Forward its TCP port to the backend on your computer:

```sh
adb devices
adb -s emulator-5554 reverse tcp:8000 tcp:8000
adb -s emulator-5554 reverse --list
```

Replace `emulator-5554` with the serial printed by `adb devices`. If the backend runs on host port 18000, use `reverse tcp:8000 tcp:18000`. Run these commands **after** building/installing, and repeat after an ADB restart or emulator restart. This keeps the app's loopback URL and Django's default allowed hosts intact.

For a hosted backend, configure an HTTPS URL through `ApiConfig.SetBaseUrl` before the sample's authentication bootstrap starts, or supply the backend URL explicitly when composing your own adapters. The password adapter rejects non-loopback HTTP and redirects. Do not replace localhost with an HTTP LAN address or the emulator's host alias for password login.

ADB reverse handles backend TCP traffic. Netcode uses a separate UDP connection; see the multiplayer section below. See Android's [ADB documentation](https://developer.android.com/tools/adb) for device selection, installation and forwarding commands.

## Configure and build the APK

Open **File > Build Settings**, select **Android**, and **Switch Platform**. Add only the intended ExampleScene to Scenes In Build: `Assets/Scenes/ExampleScene.unity` in the repository, or the imported Full Demo scene in a consumer project.

Use these settings to reproduce the tested build:

| Setting | Verified value |
|---|---|
| Render pipeline | Built-in |
| Scripting Backend | IL2CPP |
| Target Architectures | ARM64 |
| Minimum API Level | Android 6.0 / API 23 |
| Target API Level | Android 13 / API 33 |
| Auto Graphics API | Off |
| Graphics APIs | OpenGLES3 only |
| Default Orientation | Landscape Left |
| Active Input Handling | Input Manager (Old), or Both |
| Internet Access | Require |
| Allow downloads over HTTP | Allowed in Development Builds, for local sample backend |
| Development Build | On for this local qualification |
| Build App Bundle (Google Play) | Off; build an APK |

Choose your own valid application identifier. The retained test APK uses `com.alchemishty.liveops.qualification`. Minimum API 23 is a build setting, not evidence of execution on every Android version from API 23 onward. API 33 reproduces this test configuration; it is not a statement of current store submission requirements.

Select the running emulator under **Run Device**, then **Build And Run**, or **Build** to a local ignored build directory. See Unity's [Android Build Settings](https://docs.unity3d.com/2022.3/Documentation/Manual/android-build-settings.html). Release signing, app bundles, production HTTPS and store compliance belong to the consuming game's release setup.

## Install, sign in and verify

If you built an APK without Build And Run:

```sh
adb -s emulator-5554 install -r /path/to/demo.apk
adb -s emulator-5554 reverse tcp:8000 tcp:8000
```

Launch the installed app from the emulator launcher. Sign in with the player account you created; no shared test password ships with the toolkit. The first standalone instance falls back to hosting after its connection attempt times out.

Check the following before treating a build as ready:

- The avatar appears with readable face/body/cosmetic colors, including the side in shadow.
- Shop preview, purchase and inventory/equip actions work through touch; the balance updates and the cosmetic renders.
- Wallet, achievements and daily rewards return backend-confirmed state.
- Briefly background/resume the app, then refresh account state.
- For multiplayer, another account sees approved equipment changes while previews stay local.

The supplied movement/camera controls use keyboard input. Mobile games must supply their own touch movement/camera controls. The verified UI is landscape; portrait layouts, long suspension/process death and physical-device performance remain separate checks.

## Android and desktop multiplayer

Backend requests and game networking have separate destinations. The sample's **AutoConnect** component sets the Netcode destination; changing only UnityTransport's Inspector fields is overwritten by AutoConnect.

For an Android client joining a desktop host on the emulator's computer, set the Android scene's AutoConnect **Address** to `10.0.2.2` and **Port** to `7777` before building. Start the desktop host first with another account, then sign in on Android. Keep backend traffic on localhost with ADB reverse. This host alias is emulator-specific. If the desktop host is unavailable, restore the Android address to `127.0.0.1` for the standalone local-host flow.

For a desktop client joining an Android emulator host, build Android with AutoConnect address `127.0.0.1` and port `7777`. Let Android become host, then expose its UDP port:

```sh
adb -s emulator-5554 emu redir add udp:17779:7777
```

Set the desktop client's AutoConnect address to `127.0.0.1` and port to `17779` before starting it. Remove the mapping afterward with `adb -s emulator-5554 emu redir del udp:17779`. These are local development arrangements; games supply their own deployment networking, matchmaking/relay and connection policy. Physical devices need their own reachable host configuration.

The qualification passed Android client ↔ macOS host, Android client ↔ headless macOS server and Android host ↔ macOS client. Those SDK probes used isolated test configuration. The repository's automated consumer verification tools currently build macOS players, not Android APKs.

## Troubleshooting

- **Cannot reach backend:** check the backend process, selected device serial and `adb reverse --list`. Reapply forwarding after Unity's device scan resets ADB. An app restart requires another sign-in because tokens stay in memory.
- **Identity configuration error:** re-export matching public trust into the consuming project's StreamingAssets and rebuild. Confirm backend signer/context values match the exported trust.
- **Expired/not-yet-valid proof:** correct the emulator's date/time. The test emulator drifted after inactivity; do not relax proof expiry checks to compensate.
- **Startup hangs or system ANRs:** the tested older emulator had Vulkan hangs; keep OpenGLES3 selected. Cold-boot the AVD without restoring a snapshot if needed. Qualification used `-no-snapshot-load -no-snapshot-save -gpu host -feature -Vulkan` emulator launch options without wiping its data.
- **Desktop cannot join Android host:** confirm UDP redirection and the AutoConnect port/address on each peer. The tested AVD also required resolving competing Wi-Fi/Ethernet routes; a TCP backend connection alone does not prove UDP reachability.
