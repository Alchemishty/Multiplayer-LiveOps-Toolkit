# testing.md

## Test Infrastructure

**Framework:** NUnit via Unity Test Framework 1.3.9. Tests use `[TestFixture]` and `[Test]` attributes.
**Location:** `Assets/Tests/EditMode/` — 15 test files. `Assets/Tests/PlayMode/` includes `NameplateMaterialTests`, an editor-hosted regression test that checks the player prefab and spawned nameplate use a separate material from overlay UI.
**Assembly Definitions:** EditMode tests reference the production assembly.

## Existing Coverage

**What's tested:** `AccessoryLoadout` (30+ tests — equip, unequip, conflicts, JSON round-trip, serialization), `AchievementProgressTracker` (20 tests — all evaluator types, progress, double-unlock), `PreviewStateTracker` (16 tests), `ShopCatalog` (13 tests), `ShopButtonStateResolver` (12 tests), `ShopDisplayFormatter`, `AccessoryLookup`, `AchievementDefinition`, `DailyLoginDTOs`, `ShopItemAndSet`, `AttachmentBone`, `ApiResponse`, `InventoryFilter`, `SetPreviewStateTracker`, `NameplateDisplayFormatter`. All follow AAA pattern with correct naming conventions. Quality is high.

**What's not tested:** PlayMode integration paths — `PlayerAccessory` ServerRpc JSON validation, `AccessoryPool` equip/unequip with GameObjects, `AccessoryEquipService` preview lifecycle, `AchievementService` event flow. No tests for API layer coroutines or NetworkBehaviour spawn/despawn flows.

## Current testability

`AccessoryLookup`, `AccessoryLoadout`, `ShopCatalog`, preview trackers, achievement evaluators/tracker, and display formatters contain pure logic covered by EditMode tests. These extractions already exist; they are not pending refactors.

Integration coverage remains needed for `PlayerAccessory`, `PlayerAvatarSetup`, `ClientPlayerAvatars`, `AccessoryPool`, `AccessoryEquipService`, `AchievementService`, bootstrap flows, and API coroutines. The planned host/client harness is described in `docs/superpowers/specs/2026-10-22-playmode-network-tests-design.md`; its implementation plan is not evidence of completed tests.

## Testing Standards

Every code change — bug fix, feature, or refactor — must include tests.

**EditMode tests** go in `Assets/Tests/EditMode/`. Test pure C# logic only — no `MonoBehaviour`, no `GameObject`, no `ScriptableObject.CreateInstance`. Use AAA pattern. Name tests `MethodUnderTest_Condition_ExpectedResult`.

**PlayMode tests** go in `Assets/Tests/PlayMode/`. Use `[UnityTest]` with `IEnumerator` for anything requiring frames. Clean up spawned GameObjects in `[TearDown]`.

**Extraction rule:** If business logic lives inside a MonoBehaviour/ScriptableObject and can't be EditMode-tested, extract it into a pure C# class. Completed extractions:

1. `AccessorySO` → `AccessoryLookup(AccessoryItemData[])` ✓
2. `PlayerAccessory` → `AccessoryLoadout` (conflict resolution, JSON build, equipped-state tracking) ✓
3. `ShopCatalog` — pure C#, tested ✓

**Coverage priorities:** (1) PlayMode tests for `PlayerAccessory` ServerRpc JSON validation and `AccessoryPool` equip/unequip, (2) PlayMode tests for `AccessoryEquipService` preview/equip lifecycle, (3) PlayMode tests for `ClientPlayerAvatars` registration and network spawn/despawn.

**What NOT to test:** Unity serialization of primitives, `UnityWebRequest` internals, Netcode transport, third-party JSON library correctness, editor-only `SetupExampleScene`.

Latest cleanup verification (2026-10-23): all 198 EditMode tests and the nameplate material PlayMode regression passed. Use Unity MCP `run_tests` for execution; refresh Unity and inspect the console after code changes as specified in `harness.yaml`.

Unity Test Framework can leave `Assets/InitTestScene*.unity` after interrupted runs. These generated scenes are ignored and can be deleted with their `.meta` files once no tests are running. Keep screenshots outside `Assets/` to avoid importing diagnostic images as game assets.

## PlayMode runner cleanup compatibility

Test Framework 1.1.33 reproduced a `NullReferenceException` in `PlaymodeLauncher.BackgroundWatcher` after the nameplate test passed. Its runtime controller called `Cleanup()` immediately after the run-finished event, clearing `m_Runner` before the editor exit callback read `m_Runner.LoadedTest`. The project pins 1.3.9, whose runtime controller leaves cleanup to the exit lifecycle. Do not patch `Library/PackageCache` or suppress this exception in game code.

Verify runner changes by running the PlayMode test, waiting until the editor is idle in Edit Mode, and checking the console after that transition. A successful test result alone does not cover runner teardown. When changing the test-framework/NUnit package versions, restart the editor before verification so old assembly types are not retained during a hot reload.

Verification after upgrading to 1.3.9: the reproducing PlayMode test passed on two consecutive runs, both returned to idle Edit Mode with zero console errors, and all 198 EditMode tests passed. Original Enter Play Mode options were preserved.
