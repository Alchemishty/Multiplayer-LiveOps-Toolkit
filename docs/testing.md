# testing.md

## Test Infrastructure

**Framework:** NUnit via Unity Test Framework 1.3.9 on Unity 2022.3; the qualified Unity 6.3 configuration uses 1.6.0. Tests use `[TestFixture]` and `[Test]` attributes.
**Location:** portable SDK tests live in `Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Tests/`; demo tests live in `Unity/Assets/Tests/EditMode/` and `Unity/Assets/Tests/PlayMode/`. Run both package and demo assemblies for each mode.

## Existing coverage

EditMode covers Core lifecycle, Identity/proofs, Wallet, Inventory, Shop, Equipment, Achievements and Daily Rewards, plus remaining pure sample catalog/display logic. PlayMode covers explicit Django adapters, session renewal, sample UI owners, account switches, Netcode identity/equipment gates and material separation. Django tests cover account authority and atomic mechanics. Native smoke runs qualify specific macOS flows; they do not establish the full release platform matrix.

## Current testability

Mechanics live in engine-free assemblies and have deterministic EditMode tests. MonoBehaviour/UI/session/Netcode integration belongs in PlayMode tests. Continue testing concrete lifecycle/authority regressions rather than library internals or abandoned legacy paths.

## Testing Standards

Every code change — bug fix, feature, or refactor — must include tests.

**EditMode tests** go in the package Tests/EditMode folder for portable SDK behavior, or Unity/Assets/Tests/EditMode for demo behavior. Test pure C# logic only — no `MonoBehaviour`, no `GameObject`, no `ScriptableObject.CreateInstance`. Use AAA pattern. Name tests `MethodUnderTest_Condition_ExpectedResult`.

**PlayMode tests** go in the corresponding package or demo Tests/PlayMode folder. Use `[UnityTest]` with `IEnumerator` for anything requiring frames. Clean up spawned GameObjects in `[TearDown]`.

**Extraction rule:** If business logic lives inside a MonoBehaviour/ScriptableObject and can't be EditMode-tested, extract it into a pure C# class for that concrete consumer. Examples:

1. `AccessorySO` → `AccessoryLookup(AccessoryItemData[])`
2. `PlayerAccessory` → Equipment module (canonical loadout and slot/conflict rules)
3. `ShopCatalog` — pure C#, tested

**Coverage priorities:** final consumer/platform qualification. Equipment no longer accepts arbitrary loadout ServerRpc input.

**What NOT to test:** Unity serialization of primitives, `UnityWebRequest` internals, Netcode transport, third-party JSON library correctness.

## Running current checks

Run both `EditModeTests` and `MultiplayerLiveOpsToolkit.Tests.EditMode`, and both `PlayModeTests` and `MultiplayerLiveOpsToolkit.Tests.PlayMode`. Refresh Unity and inspect the console after returning to Edit Mode. Commands and tool names are recorded in [harness.yaml](../harness.yaml); Unity MCP availability depends on the agent environment.

From the repository root, run `python3 tools/test_package_layout.py` and `python3 tools/sync_full_demo.py --check`. Backend checks run from backend/ using `.venv/bin/python`: `manage.py check`, `manage.py makemigrations --check --dry-run` and `manage.py test shop`. Fresh package/native qualification uses tools/verify_package_consumer.py and tools/verify_netcode_consumer.py when installation or integration changes require it.

Test counts below describe recorded runs, not a new verification of the current checkout. The latest Unity 6 qualification records 267 EditMode / 149 PlayMode, with 149 PlayMode also passing on Unity 2022.3; the backend run records 104 Django tests. See the Unity 6.3 LTS qualification entry below. Current support limits are maintained in the [README](../README.md#current-support).

Unity Test Framework can leave `Assets/InitTestScene*.unity` after interrupted runs. These generated scenes are ignored and can be deleted with their `.meta` files once no tests are running. Keep screenshots outside `Assets/` to avoid importing diagnostic images as game assets.

## Retained qualification evidence

These are recorded results, not a fresh test run of the current checkout. Earlier incremental counts, temporary build paths and implementation-review narratives are omitted; Git history retains them.

| Qualification | Recorded result | What it establishes |
|---|---|---|
| Unity 6.3 LTS, 2026-10-30 | 267 EditMode / 149 PlayMode; 104 Django; 11 Python layout/distribution checks | macOS Apple Silicon on 6000.3.27f1, NGO 2.13.3, Transport 2.7.4, Collections 2.7.1, JSON 3.2.2, Test Framework 1.6.0 and uGUI 2.0.0 |
| Unity 2022.3 compatibility, 2026-10-30 | 149 PlayMode on 2022.3.33f1 / NGO 1.9.1 | Current multiplayer/UI lifecycle regression compatibility with the repository baseline |
| Fresh UPM consumer, 2026-10-30 | 212 portable EditMode; SDK and imported Full Demo builds; native account operations | Import without TMP, upgrade preservation of consumer-owned files, sample/TMP import and package-source immutability |
| Native multiplayer, 2026-10-30 | Separate host/client and headless server/client probes | Real signed Django identity/equipment, existing-player visibility, local preview/public appearance, teardown, owner movement and Animator replication |
| Classic archive, 2026-10-30 | Export validation, Unity reimport, missing-script checks and strict macOS Full Demo build | Archive contents/GUIDs and buildability of the imported saved scene |
| Android, 2026-10-30 | ARM64 Android 13 emulator, Unity 2022.3.33f1 / IL2CPP / OpenGLES3 | Account mechanics, landscape touch UI and Android client/desktop host, Android client/headless desktop server, Android host/desktop client roles; see [Android guide](../Documentation/android.md) |

The repository's consumer tools reproduce desktop checks with isolated databases and ephemeral signing keys. They print evidence locations; use `--evidence-root` on the package verifier to retain artifacts on a chosen volume. Test reports and build logs belong with the run artifacts, outside Unity Assets. The Android checks are separate from those macOS automation tools.

The recorded native SDK probes do not establish manual acceptance of every Full Demo UI flow. Headless runs use a macOS desktop player with `-batchmode -nographics`, not Unity's dedicated-server subtarget. Windows, Linux, physical Android, Unity 6 Android, other render pipelines and arbitrary replacement character rigs require their own checks. See [current support](../README.md#current-support).

## Compatibility and regression notes

Keep these findings in mind when changing the affected systems:

- **Test runner teardown:** Unity 2022.3 Test Framework 1.1.33 has a reproduced runner-cleanup `NullReferenceException` after a passing PlayMode test. The repository uses 1.3.9. Verify return to idle Edit Mode and inspect the console; a green test result alone does not cover teardown. Restart the editor after changing test-framework versions. Do not patch Library/PackageCache.
- **NGO 2 visibility:** spawning a player can add observer entries without sending previously hidden players. VerifiedPlayerSpawner reveals existing verified avatars before spawning the new one. Preserve real-transport late-join tests on both NGO versions.
- **Account isolation:** regression coverage includes refresh/access-token origin binding, account replacement during renewal callbacks, cancellation/credential clearing and terminal host teardown. These prevent stale work or credentials crossing account/session boundaries.
- **Operation reservation:** synchronous state notifications can disable an owner before coroutine startup. Preserve tests for disposing reserved but unstarted iterators, panel-hidden command completion and owner teardown.
- **Full Demo quit ordering:** a Unity 6.3 standalone recording (2026-10-31) completes capture, then logs a null-reference in `DevAuthBootstrap.OnSessionEnded` at `loginPanel.SetActive(true)` during `DjangoPlayerSession.OnDisable`. The sample login panel can already be destroyed during application quit. Include application-exit log inspection when qualifying the demo; successful capture/process exit alone does not establish clean teardown. This is an open runtime finding.
- **Consumer preservation:** upgrade checks compare existing scene/configuration/sentinel bytes rather than recreating the files they claim to preserve. Full Demo checks import the actual saved sample through Unity's sample API.

Unity 6 emits an existing TMP `enableWordWrapping` deprecation warning in demo presentation; the recorded compilation/builds have no C# errors. Unity MCP availability is environment-dependent; batch compilation, XML test results and native builds supply independent verification when the bridge is unavailable.
