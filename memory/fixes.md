# Fixes — Cross-Session Memory

Entries are added by `/retrospective` after completing features and debugging sessions.
Entries marked [SUPERSEDED] are kept for historical context.

---

## 2026-10-23 — Overlay text disappears after avatar spawn

`TextMeshPro` nameplates and `TextMeshProUGUI` overlay labels referenced the default font material. TMP's world-space initialization changes depth testing on that shared material, affecting the side panel and wallet bar. Give nameplates a dedicated material (`Assets/Prefabs/Player/Nameplate.mat`) and assign it in both `PlayerAvatar.prefab` and `SetupExampleScene.CreatePlayerPrefab`. Keep default UI materials separate from world-space text. `NameplateMaterialTests` guards prefab and instantiated material isolation.

## 2026-10-23 — Test runner throws after successful PlayMode test

Test Framework 1.1.33 clears `PlaymodeTestsController.m_Runner` in `TestRunnerCoroutine` before `PlaymodeLauncher.BackgroundWatcher` uses it on exit. Reproduced with the nameplate regression test, including with scene reload enabled. Update the pinned package to 1.3.9, which removes that premature cleanup, and let Unity resolve the lock file/NUnit dependency. Restart after replacing test-framework assemblies; verify both the test result and the console after returning to idle Edit Mode. Game-code null guards and edits to generated package cache files do not fix this lifecycle issue.
