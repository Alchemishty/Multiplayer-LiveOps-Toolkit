# testing.md

## Test Infrastructure

**Framework:** NUnit via Unity Test Framework 1.3.9. Tests use `[TestFixture]` and `[Test]` attributes.
**Location:** portable SDK tests live in `Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Tests/`; demo tests live in `Unity/Assets/Tests/EditMode/` and `Unity/Assets/Tests/PlayMode/`. Run both package and demo assemblies for each mode.

## Existing coverage

EditMode covers Core lifecycle, Identity/proofs, Wallet, Inventory, Shop, Equipment and Achievements, plus remaining pure sample catalog/display logic. Replaced loadout/preview and client achievement evaluator tests were removed with their implementations. PlayMode covers explicit Django adapters, session renewal, sample UI owners, account switches, Netcode identity/equipment gates and material separation. Django tests cover account authority and atomic mechanics. Native smoke runs qualify specific macOS flows; they do not establish the full release platform matrix.

## Current testability

Mechanics live in engine-free assemblies and have deterministic EditMode tests. MonoBehaviour/UI/session/Netcode integration belongs in PlayMode tests. Continue testing concrete lifecycle/authority regressions rather than library internals or abandoned legacy paths.

## Testing Standards

Every code change — bug fix, feature, or refactor — must include tests.

**EditMode tests** go in the package Tests/EditMode folder for portable SDK behavior, or Unity/Assets/Tests/EditMode for demo behavior. Test pure C# logic only — no `MonoBehaviour`, no `GameObject`, no `ScriptableObject.CreateInstance`. Use AAA pattern. Name tests `MethodUnderTest_Condition_ExpectedResult`.

**PlayMode tests** go in the corresponding package or demo Tests/PlayMode folder. Use `[UnityTest]` with `IEnumerator` for anything requiring frames. Clean up spawned GameObjects in `[TearDown]`.

**Extraction rule:** If business logic lives inside a MonoBehaviour/ScriptableObject and can't be EditMode-tested, extract it into a pure C# class. Completed extractions:

1. `AccessorySO` → `AccessoryLookup(AccessoryItemData[])` ✓
2. `PlayerAccessory` → Equipment module (canonical loadout and slot/conflict rules) ✓
3. `ShopCatalog` — pure C#, tested ✓

**Coverage priorities:** final consumer/platform qualification. Equipment no longer accepts arbitrary loadout ServerRpc input.

**What NOT to test:** Unity serialization of primitives, `UnityWebRequest` internals, Netcode transport, third-party JSON library correctness.

## Running current checks

Run both `EditModeTests` and `MultiplayerLiveOpsToolkit.Tests.EditMode`, and both `PlayModeTests` and `MultiplayerLiveOpsToolkit.Tests.PlayMode`. Refresh Unity and inspect the console after returning to Edit Mode. Commands and tool names are recorded in [harness.yaml](../harness.yaml); Unity MCP availability depends on the agent environment.

From the repository root, run `python3 tools/test_package_layout.py` and `python3 tools/sync_full_demo.py --check`. Backend checks run from backend/ using `.venv/bin/python`: `manage.py check`, `manage.py makemigrations --check --dry-run` and `manage.py test shop`. Fresh package/native qualification uses tools/verify_package_consumer.py and tools/verify_netcode_consumer.py when installation or integration changes require it.

Test counts below describe recorded runs, not a new verification of the current checkout. The last recorded full root Unity run is 267 EditMode / 146 PlayMode after the Unity directory relocation; backend mechanics last recorded 103 Django tests. Current support limits are maintained in the [README](../README.md#current-support).

Unity Test Framework can leave `Assets/InitTestScene*.unity` after interrupted runs. These generated scenes are ignored and can be deleted with their `.meta` files once no tests are running. Keep screenshots outside `Assets/` to avoid importing diagnostic images as game assets.

## Historical verification records

The entries below preserve evidence from successive implementation stages. References to generators, retired samples, temporary paths and older test counts describe those stages; they are not current setup instructions.

### PlayMode runner cleanup compatibility

Test Framework 1.1.33 reproduced a `NullReferenceException` in `PlaymodeLauncher.BackgroundWatcher` after the nameplate test passed. Its runtime controller called `Cleanup()` immediately after the run-finished event, clearing `m_Runner` before the editor exit callback read `m_Runner.LoadedTest`. The project pins 1.3.9, whose runtime controller leaves cleanup to the exit lifecycle. Do not patch `Library/PackageCache` or suppress this exception in game code.

Verify runner changes by running the PlayMode test, waiting until the editor is idle in Edit Mode, and checking the console after that transition. A successful test result alone does not cover runner teardown. When changing the test-framework/NUnit package versions, restart the editor before verification so old assembly types are not retained during a hot reload.

Verification after upgrading to 1.3.9: the reproducing PlayMode test passed on two consecutive runs, both returned to idle Edit Mode with zero console errors, and all 198 EditMode tests passed. Original Enter Play Mode options were preserved.

## Core lifecycle foundation

`LiveOpsRuntimeTests` adds 38 EditMode cases for explicit module selection, dependency ordering/validation, deferred and concurrent initialization, nested coroutine failure, rollback, routine disposal, cancellation, callback exceptions, shared dependencies, and idempotent/reentrant shutdown. All 34 initial cases failed against the compiling skeleton before implementation. A later reentrant-initializer regression failed before its disposal fix.

Verification for this increment: all 236 EditMode tests pass (198 existing + 38 Core). The existing nameplate-material PlayMode regression remains preservation coverage for the sample. No backend behavior changes in this increment.


## Identity and sample bootstrap

`IdentityModuleTests` adds 14 EditMode cases covering credential validation, stable backend identity, required bindings, provider/backend failures, cancellation, late callbacks, rollback and session cleanup. The initial run failed seven lifecycle cases against the compiling skeleton before implementation; value-validation cases were already passing.

`IdentityBootstrapTests` adds three editor-hosted PlayMode tests using a local HTTP stub: valid login uses the backend-returned name, missing stable identity rejects the response before publishing tokens, and disabling/destroying the bootstrap clears the private session. The initial two integration cases failed against the old bootstrap; the disable regression also failed before its cleanup fix. Tests isolate their endpoint, token manager, UI objects and listener lifecycle.

Verification: **250 EditMode**, **4 PlayMode** (including nameplate preservation), and **65 Django tests** pass. Existing Django auth tests now check the stable development player ID for both new and returning accounts. The editor returned to idle with zero console errors. No full host/client or dedicated-server verification was performed for this slice.


## Django-managed password login

The current suite passes **264 EditMode**, **6 PlayMode**, and **73 backend tests**. Seven password-provider cases failed against the compiling skeleton before implementation. The sample integration tests first failed for the old development endpoint, missing password gating and rejection behavior before migration. Adapter preflight tests additionally cover unsafe URLs, unsupported credentials and idempotent cleanup.

The five login PlayMode cases now cover password dispatch/clearing, blank-password gating, generic 401 handling, malformed identity responses, and shutdown cleanup. The sixth preserves nameplate material isolation. Backend `shop.test_identity` adds eight tests for active-user password verification with DEBUG false, usable access/refresh tokens, stable identity after rename, existing-profile preservation, transaction rollback, malformed requests, generic rejection, rate limiting and blocked development bypass. All eight failed before the endpoint/security change. Django check and migration-drift checks pass without a schema migration.

Full host/client and dedicated-server identity proof flows are not covered by these login tests. Password reset remains Django admin functionality; self-service registration/recovery is not implemented.


## Session renewal

Renewal verification (2026-10-24): **270 EditMode**, **19 PlayMode**, **76 backend tests** passed before the review remediation below. Six `TokenSessionTests` failed against the compiling skeleton before implementation; they cover expiry thresholds, malformed JWT expiry, rotation, generation preservation and stale-account rejection. HTTP-stub `SessionRenewalTests` cover shared refresh, malformed responses, late responses after account replacement, proactive renewal, terminal notifications, transient recovery, bounded server failures, 401 replay, rejection after replay and renewal before gameplay dispatch. The duplicate-refresh and second-401 regressions failed before their fixes. Bootstrap coverage includes terminal renewal failure after a successful login.

Three backend renewal tests cover expired, inactive and deleted accounts; the deleted-account case reproduced a server exception before the serializer fix. Existing password-login tests exercise successful refresh. Renewal tests use synthetic unsigned JWT-shaped values only as client scheduling inputs; backend tests use actual SimpleJWT tokens. No proof-verification claim follows from client expiry parsing. HTTP tests do not replace full multi-process Netcode validation.

Final renewal review added two failing PlayMode regressions before their fixes: replacing an account inside a renewal completion callback must not replay the original request under the new account; disabling the token owner must clear credentials and complete pending waiters once. The final suite includes both guards.


## Fresh-context review remediation

Latest verification (2026-10-24): **271 EditMode**, **27 PlayMode**, **76 backend tests** pass. The review's refresh-endpoint leak, host teardown leak and subscriber starvation were reproduced with failing tests before fixes. Two HTTP stubs prove both mutable-global configuration and explicit adapter URLs cannot send a session's refresh credentials to another backend. A further failing regression reproduced access-token dispatch to a foreign origin; the request now fails before dispatch, clears the session and reports a configuration error.

Real Netcode/UnityTransport tests cover host teardown on bootstrap disable/destruction, cancellation while a client is connecting and prevention of host fallback after authentication loss. These are local lifecycle tests, not a full host-plus-remote-clients or dedicated-server conformance suite. The subscriber test deliberately logs one expected exception through `LogAssert.Expect`, then checks that later delivery and panel hiding still occur. Development-provider tests cover credential delivery, invalid usernames and missing callbacks. The PlayMode assembly explicitly references Unity.Networking.Transport for the new transport tests.

The independent reviewer verified the original findings resolved. Renewal endpoint/API changes use existing types; generator cleanup renames local variables only and does not change saved scene wiring.

## Identity proof foundation

Verification (2026-10-24): **304 EditMode**, **31 PlayMode**, **84 Django tests** pass. `IdentityProofVerifierTests` adds 32 cases, including a Django/PyJWT-generated signed vector verified by Unity, tampering, algorithm/type confusion, malformed/duplicate fields, wrong context, expiry/lifetime, replay/concurrent consumption, key rotation and immutable public-key bindings. Test signing keys are ephemeral; the committed cross-language vector contains only a public key and a fixed non-production assertion.

The verifier initially failed against a compiling skeleton. Four HTTP-stub PlayMode cases likewise failed before adapter implementation and now cover request shape/authentication, missing assertion, typed backend configuration failure and an account switch during dispatch. An EditMode adapter case validates required inputs before dispatch. Eight Django cases cover scoped issuance, account authority, rejected caller claims, invalid context, profile absence, configuration, throttling and deterministic vector timestamps. Fractional lifetime configuration reproduced a failing test before strict validation.

Unity compilation and final console inspection are required. The existing subscriber-isolation test deliberately expects its subscriber exception; MCP can emit a WebSocket closure exception during domain reload. Neither is a compiler failure. These tests do not qualify the pending multi-process Netcode handshake, standalone crypto or dedicated-server integration.

## Verified Netcode integration

Latest verification (2026-10-24): **311 EditMode**, **44 PlayMode**, **85 Django tests**. Seven public-trust parser tests cover the exported schema, cross-language verification and invalid bindings. Thirteen `NetcodeIdentityGateTests` use real Unity Transport: host proof validation, invalid proof rejection, stalled backend timeout/disposal, server-only startup, disable cleanup, remote success/failure isolation, verified spawn/server-owned names, Unity yield delay, observer gating before/after verification, bounded transient retry, replay rejection and throwing-disposer shutdown. Missing behavior, skipped WaitForSeconds, visibility leakage and disposal blocking shutdown were reproduced before fixes.

A temporary standalone smoke scene/harness (removed after verification) used the production gate, verifier, Django adapter and verified spawner. A macOS development build passed against an isolated Django database/ephemeral signing key: separate host+client and server-only+two-client processes, checking verified public-player replication. The server-only process used `-batchmode -nographics`, had no account login, and held public trust only. Test account passwords/private keys were not committed; the temporary private key/database were deleted. This is macOS server-role evidence, not Windows/Linux or dedicated-server-subtarget release qualification. The initial threaded SQLite run hit its concurrent first-login write-lock limitation; the passing local smoke backend ran with `--nothreading`.

The saved example scene and generator both include the gate/spawner. Missing trust/signing configuration is intentionally fatal with no insecure fallback. Standalone sample deployments must provision public trust and backend-only signing configuration as documented in the protocol spec. Expected subscriber/disposal test exceptions are asserted; inspect the console for unexpected errors after tests.

## Explicit session ownership

Verification for this slice: **320 EditMode**, **48 PlayMode**, **85 Django tests**. Nine PlayerContext cases cover isolated owners, initialization reservation, failed-login retry, cancellation, identity cleanup, disposal idempotence, exception-safe cleanup and terminal subscriber isolation. The initial seven cases failed against a compiling stub before implementation. Existing renewal, bootstrap and proof HTTP cases now create/inject their session explicitly and retain their behavioral assertions.

New HTTP coverage proves two real Django session instances dispatch their own credentials and disposal of one leaves the other active. A delayed-request test reproduced missing abort on disposal; pending requests now abort within the next native completion frames without waiting for the delayed backend. Wallet regression coverage reproduced conversion using the global shop owner instead of the injected wallet owner; conversion now dispatches through the wallet session using JSON. Reset coverage proves its endpoint, method, injected owner and response propagation. Unity objects are tested in PlayMode; pure context ownership uses EditMode stubs.

No backend endpoint/schema changes or new retry policy are introduced by this refactor. Idle monitoring/general recovery remains deferred. The saved scene and generator require the explicit authBootstrap binding and Django session component; there is no token/session singleton to recreate after teardown.

The ownership refactor also passed macOS standalone smoke scenarios against an isolated Django backend: host+client and a server-only process+two clients. Player processes constructed PlayerContext with explicit Django sessions/proof adapters; the server-only process created no player context/session. The temporary Unity harness and ephemeral backend private key/database were removed after verification.

## Independent Wallet verification (2026-10-24)

Full regression passed: **332 EditMode**, **56 PlayMode**, **92 Django tests**. Wallet tests cover independent account state, immutable whole-snapshot publication, confirmed integer conversions, business rejection, unknown outcomes without automatic retry, refresh gating, shutdown/cancellation and subscriber isolation. PlayerContext coverage verifies optional Wallet selection, Identity-first initialization and cleanup. Adapter/UI cases exercise explicit session ownership, account replacement, JSON conversion, malformed responses, typed rejection, a TMP view without sample login/Netcode, control teardown and coordinated sample reset.

Tests first reproduced notification-triggered shutdown reporting both cancellation and success, cancelled conversions retaining stale balances, enabled controls on disabled views, and reset missing its post-reset Wallet refresh. Those regressions pass after the fixes. Required fresh-context review found the first two lifecycle issues and reset overlap; targeted follow-up confirmed their resolution.

An isolated macOS standalone consumer passed real Django password login, Wallet initialization, conversion, refresh and account teardown without any Netcode or avatar component. Its temporary scene/script and isolated backend database were removed afterward. No backend model/migration was added; migration drift check passed. This qualifies the tested macOS path, not other desktop platforms. The sample backend does not promise production transaction history, receipts or replay guarantees.

The normal ExampleScene macOS development build also passed with strict build mode (zero errors). Build artifact: `/tmp/liveops-wallet-sample/MultiplayerLiveOpsToolkit.app`. Saved sample Wallet bindings were checked after loading the generated scene.

## Test cleanup (2026-10-24)

The EditMode suite now has 323 cases (previously 332). Removed the unused nameplate UTF-8 truncation helper and four tests, plus the unused catalog/registry gold-cost wrapper chain and three tests. Currency pricing used by purchases remains covered through GetCostForCurrency tests. Consolidated eight DailyLogin DTO cases into three contract cases covering nested fields, claimed/unclaimed days, null rewards, claim responses and empty past-month calendars; dropped the generic full-month JSON array check. Parameterized bone mapping retains all ten original slot assertions as individually reported cases (previously seven methods). The 13 rewritten DTO/bone cases passed against unchanged runtime code before dead-code removal. Full EditMode verification: 323 passed.

Cleanup regression: all 56 PlayMode cases also passed. Fresh-context review confirmed no remaining callers of deleted methods and identified set-price mapping coverage to retain; the existing valid-set catalog test now asserts the mapped currency cost directly. Backend code was unchanged; Django tests were not rerun for this cleanup.

## Independent cosmetic Inventory verification (2026-10-24)

Full regression passed: **328 EditMode**, **67 PlayMode**, **97 Django tests**. Inventory adds eight module/model cases and an Identity-only composition case; four old shop-filter tests were removed with the replaced filter. Adapter/UI coverage checks injected account ownership, stale responses, malformed/unowned set members, configuration errors, request disposal, rendering without Shop/avatar, row replacement, shutdown clearing and disable/re-enable. Bootstrap HTTP coverage selects Inventory without Wallet; reset coverage verifies waiting for pending ownership reads and refreshing after reset.

Backend tests first failed for the missing endpoint; module and adapter behavior failed against compiling stubs. A separate delayed-refresh regression failed before the bootstrap-owned command fix, then passed with its view disabled. Fresh-context review identified the tab lifetime issue; targeted follow-up confirmed the durable owner and matching subscription cleanup. The reference endpoint uses existing tables with no migration drift.

A macOS standalone Inventory consumer passed real Django login, owned item/set reads, development reset, refresh and teardown with no Shop, Wallet, Netcode or avatar component. Its temporary scene/script and isolated database were removed. This qualifies the tested macOS path; other desktop platforms remain pending.

The normal ExampleScene strict macOS development build passed with zero errors: `/tmp/liveops-inventory-sample/MultiplayerLiveOpsToolkit.app`.

## Independent Shop verification (2026-10-24)

EditMode regression: **318 passed**. Shop adds nine module/model cases plus a full-context dependency-order case. The slice removes twenty obsolete tests with their replaced legacy price helpers, local ownership mutation methods and display-name fallback; catalog price-mapping assertions are retained directly. Remaining formatting/button-state tests exercise the service-bound UI. Coverage includes all-currency snapshots, coherent Wallet/Inventory observation, overlapping account-operation rejection, unknown outcomes without retry, business rejection, pending/unstarted cancellation and exact-once completion during shutdown notifications.

Django regression: **100 passed**. Replaced legacy shop listing/purchase tests/routes were removed; retained authentication/throttle checks target the new routes. New cases cover offers, all-currency debit/grant atomicity, rollback, duplicate rejection, full-price partial sets, strict requests, metadata validation, set-only parts and account snapshot consistency. Two interleaved request threads first reproduced the inconsistent read; the fixed catalog uses the purchase account transaction/locks. SQLite may abort a competing writer; only the test fixture retries that aborted transaction. This is not a production retry policy or a qualification of every database deployment.

Fresh-context review findings were reproduced and fixed: concurrent catalog snapshot mixing, invalid metadata committing a purchase, prompt retention after session end, and prompt reopening after owner disable during committed completion. A separate regression reproduced missing completion during the final idle notification; core now delivers its committed completion exactly once, while sample presentation checks its own active lifetime. Sample tests exercise no-avatar rendering, prices, set filtering, confirmed ownership, preview intent, durable purchases across tab disable, owner cancellation and prompt teardown.

Shop final regression: **318 EditMode, 80 PlayMode, 100 Django** passed. PlayMode includes prompt teardown and controller-disable during final purchase notification; completion remains exactly once while disabled presentation stays closed. `makemigrations --check --dry-run` reports no changes.

Native verification: an isolated macOS consumer passed real Django login, catalog initialization, multi-currency item purchase, full-price partially owned set purchase, duplicate rejection, refresh and teardown without Netcode/avatar/UI. Its temporary source, scene and metadata were removed.

Normal ExampleScene strict macOS development build passed with zero errors: `/tmp/liveops-shop-sample/MultiplayerLiveOpsToolkit.app`. Saved scene and scene generator both bind ShopSampleController. Other desktop platforms and final package installation remain release qualification work.

## Independent Equipment verification (2026-10-25)

EditMode regression: **280 passed**. The slice adds 23 module/proof/public-state cases and removes 61 obsolete cases with the replaced AccessoryLoadout, PreviewStateTracker and SetPreviewStateTracker implementations (34/17/10 respectively). Coverage retains the mechanics through configurable conflict/set application, ownership rejection, atomic unequip/clear, local unowned preview/restore, uncertain outcomes, stale/same-revision mismatches, unstarted/pending cancellation, immutable/public wire validation and signed proof context/replay/tampering. Full-size 32-slot stable-ID proofs are supported. Existing Identity proof tests pass after shared verification extraction.

Django regression: **103 passed**. Eleven new Equipment cases replace eight legacy equipment route tests. The new routes cover exact string-ID/revision requests, owned atomic loadouts, configured conflicts, stale revision rejection, transactional rollback and canonical signed assertions. Malformed configuration is reported explicitly. The new migration adds only PlayerProfile.equipment_revision; migration drift check passes. Local default database migration was exercised.

Fresh-context review identified publication during proof capture, reset overlap, avatar replacement subscriptions, disabled Equipment composition and missing revision parsing. Each was reproduced before its fix. A private revision ACK and deferred host follow-up proof preserve the latest publication without retrying mutations. Delayed requests prove Inventory tab changes cannot cancel account work; unknown outcomes offer explicit Equipment refresh.

Native macOS qualification passed against an isolated Django database and ephemeral signing key: Equipment+Inventory-only login/preview/item/set/conflict/unequip/clear/refresh/teardown with no Shop/Wallet/UI/avatar/Netcode; Django-generated proof verification/replay rejection; separate host/client and dedicated-server/client signed handshakes and revision updates. The dedicated verifier had no player credentials or visual dependencies. Temporary consumer source/scene/metadata, private key and isolated database were removed.

A real Unity Transport integration also verifies initial server-approved PlayerAccessory state, updates, late join, respawn and disconnect cleanup, with a local visual test double. A missing mapping stops the local session with cosmetic ID/revision diagnostics. Other desktop platforms and final package install/upgrade qualification remain pending.

Final PlayMode regression: **96 passed**. Includes rejection of an identity-only server when Equipment is selected by the client. Required-feature omission is a configuration failure, rather than silently skipping equipment verification.

Final normal ExampleScene strict macOS development build passed with zero errors: `/tmp/liveops-equipment-sample/MultiplayerLiveOpsToolkit.app`. Final full suites: **280 EditMode / 96 PlayMode / 103 Django**. The scene generator and saved scene both bind Equipment; temporary qualification assets and signing keys are removed.

## Independent Achievements verification (2026-10-25)

Full regression: **261 EditMode / 109 PlayMode / 108 Django** passed. Ten new module tests replace 29 obsolete client definition/evaluator cases; six adapter and six UI lifecycle tests cover explicit requests, account switching, malformed responses, unknown outcomes, tab hiding, toast cleanup and synchronous observer-driven cancellation. Development reset waits for achievements and refreshes claim state after reset. Fifteen Django cases cover authoritative purchase/spend/attendance/collection progress, currency/cosmetic grants, duplicate/locked/no-reward claims, private scope and rollback on invalid/unexportable result configuration. Migration drift check passed; `0007_userachievement_is_claimed` applied locally.

Fresh-context review found an operation reservation gap before the sample controller tracked its iterator. Both claim and refresh reproductions failed first, then passed after lifetime-aware synchronous tracking/disposal; the reviewer confirmed the fix. The preexisting development reset does not reserve the entire account while its HTTP call runs; this sample limitation remains documented.

Native macOS consumer passed against isolated Django: Identity + Wallet + Inventory + Achievements, with no Shop module/UI/avatar/Netcode. It verified actual Shop-backed progress, currency/cosmetic claims, reward-driven collection completion, backend duplicate rejection, authoritative refresh and private teardown. Temporary consumer source/scene/metadata and isolated database were removed. The saved sample scene has zero missing scripts and explicit panel/toast/purchase/attendance bindings.

Final ExampleScene strict macOS development build passed with zero errors: `/tmp/liveops-achievements-sample/MultiplayerLiveOpsToolkit.app`. Unity console was clear after reloading the changed scene. Other desktop platforms and final package import/upgrade remain separate release qualification.

## Independent Daily Rewards verification (2026-10-25)

Full regression: **265 EditMode / 119 PlayMode / 103 Django** passed. Seven module cases replace the three retired DailyLogin DTO cases. Five adapter cases, four calendar/controller cases and one reset case cover server-date-only requests, stale account rejection, wrong-date results, typed rollover/configuration failures, panel-hidden completion, confirmed attendance events, synchronous reservation cancellation, pending owner teardown and reset refresh. Ten backend cases replace fifteen legacy calendar/claim/bounds cases; they verify complete current-month export, leap/month boundaries, private attendance, exact displayed-date claims, duplicate rejection, attendance-only mode, invalid/overflowing rewards and transactional rollback including failure after grant.

The fresh-context review found no actionable issues. The UI tests reproduced a direct-child reparent during OnDisable; retaining the Refresh row with paired listener cleanup fixes it. Engine-free Daily Rewards requires only Identity and explicit Wallet when configured currency schedules need it. No migration changes; Django checks and migration drift check pass. Full release platform/package qualification remains separate.

Independent native macOS consumer passed against isolated Django with Identity + Wallet + Daily Rewards only, without Shop/Inventory/Equipment/Achievements/UI/avatar/Netcode. It verified backend-date calendar, atomic attendance/currency observation, backend duplicate rejection, authoritative refresh and private teardown. Temporary consumer source/scene/metadata and isolated database were removed. Saved sample scene has zero missing scripts and explicit Daily Rewards/achievement-attendance bindings.

Final normal ExampleScene strict macOS development build passed with zero errors: `/tmp/liveops-daily-rewards-sample/MultiplayerLiveOpsToolkit.app`. Removed unused ErrorResponse DTO after its last legacy caller was retired; final build/compiler verification passed.

## UPM package and clean consumer verification (2026-10-25)

Root regression: **265 EditMode / 121 PlayMode / 103 Django** passed. The two new failing-first session cases enforce explicit renewal endpoints and preservation of consuming-game PlayerPrefs. Portable tests moved with the SDK into package test assemblies; demo tests remain in Assets. Run both EditModeTests/MultiplayerLiveOpsToolkit.Tests.EditMode and PlayModeTests/MultiplayerLiveOpsToolkit.Tests.PlayMode to retain the full root suite. Five standard-library package boundary/upgrade checks pass; a new preservation test detects overwritten or missing consumer files.

Fresh Unity 2022.3.33f1 project qualification passed at `/var/folders/66/dvx3vcf57pjb1nh7rqph34l80000gn/T/liveops-package-consumer-nkp6iykt`. It installed the local SDK without TMP, passed **200 portable EditMode cases**, updated a private preview copy while building the existing scene and comparing scene/metadata/sentinel bytes, then imported the authored TMP sample through Unity's Package Manager sample API and actual TMP Essentials importer. The importer waits for importPackageCompleted before exiting batch mode.

Native SDK consumer passed all selected modules against isolated Django: login, purchase/ownership, equipment, achievement reward, daily reward and private teardown. Native custom TMP sample passed credential entry/cleanup, cancelled-login retry, claim, confirmed rendering, owner disable during reservation and re-enable/fresh sign-in. The probe reproduced disabled controls after re-enable before OnEnable rendering/state cleanup fixed it. Fresh-context review confirmed both fixes: sample re-enable and an upgrade check that previously recreated the scene it claimed to preserve. No outstanding review findings. Source digest proved the verifier did not edit the original package. Temporary database/credentials are removed; evidence/apps/project copies remain in the printed temporary directory.

The existing ExampleScene has zero missing scripts and retains its package-backed session and Netcode gate after GUID-preserving moves. Package runtime contains no TMP/uGUI/demo assembly dependency or unverified art. Final release, Windows qualification and redistribution licensing remain separate work.

Final full-demo ExampleScene strict macOS development build passed with zero errors after package extraction: `/tmp/liveops-package-sample/MultiplayerLiveOpsToolkit.app`.

## Private preview release qualification (2026-10-25)

Current full suites pass **265 EditMode / 125 PlayMode / 103 Django**, plus five package checks and 200 portable tests in a fresh consumer. The additional reset test failed before waiting for Wallet work started during reset HTTP. Fresh reviewer accepted the targeted fix. Current platform support and limitations are summarized in the [README](../README.md#current-support); dated test results remain in this document. Run both demo and package test assemblies; older counts above describe historical increments.

## Owner UI feedback (2026-10-26)

Four failing-first PlayMode regressions cover Shop category sections/name ordering, readable achievement Refresh, distinct claimed state and explicit no-reward completion. Shop/Sets reuse Inventory-style section headers and shared action buttons now default to a contrasting blue background. Achievements show muted progress, gold Claim and green Completed/Claimed states. All **129 PlayMode** cases pass; the fresh-context scoped review found no actionable issues. This increment changes demo presentation only; no module/backend contract or scene wiring changed.

## Package equipment visuals (2026-10-26)

Current regression: **261 EditMode / 141 PlayMode**, plus five package layout checks. Ten retired hard-coded AttachmentBone cases were replaced by configurable renderer tests; three pure presenter tests ship as portable EditMode coverage. Renderer/presenter initial cases failed against compiling skeletons. Review regressions failed before fixes for avatar bone-map contamination, same-frame reset with deferred destruction, and destruction of a bound avatar before network spawn. Follow-up review accepted the fixes.

The saved demo avatar renders and restores all **46 authored cosmetics** through the package renderer. Its generator authors 11 explicit slot bindings and preserves the existing prefab. Strict macOS full-demo build passed with **zero errors/warnings** at `/tmp/liveops-visuals/MultiplayerLiveOpsToolkit.app`.

Final fresh consumer: **206 portable EditMode** passed without demo/TMP dependencies, preview upgrade preserved consumer-owned files, native SDK exercised real Django modules plus package-owned mesh rendering/preview/teardown, and imported TMP sample passed login/automatic calendar refresh/day-change recovery/claim/teardown. Package source digest stayed unchanged. Evidence: `/var/folders/66/dvx3vcf57pjb1nh7rqph34l80000gn/T/liveops-package-consumer-g8y48qr3`. No backend mechanics or schema changes in this slice; backend suite was not rerun. Windows and arbitrary character/art compatibility remain unqualified.

## Complete package game starter (2026-10-26)

Current full suites: **267 EditMode / 146 PlayMode** passed; **six package/distribution checks** passed. New failing-first coverage covers session-local avatar lookup/replacement and observer isolation, registry-bound camera replacement/reenable, explicit module composition/failed-login retry, rejected concurrent composition reference preservation, missing NetworkObject diagnostics and disabled connection-owner cancellation. Existing verified spawn/late-join/respawn/equipment, UI/session and visual tests remain green. Scoped fresh review findings were reproduced and fixed, with follow-up review reporting no remaining confirmed findings.

Clean consumer at `/var/folders/66/dvx3vcf57pjb1nh7rqph34l80000gn/T/liveops-package-consumer-i_g9ojd1`: **212 portable EditMode** cases passed without demo/TMP requirements; preview upgrade preserved consumer files; both optional samples imported; the Game Starter generated and built its full placeholder scene without repository art; SDK native account composition/visuals and minimal TMP flows passed. The package source digest stayed unchanged. A first install caught an invalid declaration for a nonexistent legacy-input UPM package; it was removed before the passing qualification.

Native multiplayer at `/var/folders/66/dvx3vcf57pjb1nh7rqph34l80000gn/T/liveops-release-netcode-y6fa21ef`: separate host/client and headless server/client passed real signed Django identity/equipment with the actual packaged spawner, UI-independent identity, avatar registry/lifecycle and renderer. Client checks include local-only preview, canonical restoration and verified equipment rendering. The server has no local account/nameplate requirement. This remains macOS desktop headless-role qualification, not Linux or Unity's dedicated-server build subtarget.

Normal repository ExampleScene strict macOS build passed with **zero errors/warnings**: `/tmp/liveops-starter/MultiplayerLiveOpsToolkit.app`. Backend mechanics/schema were unchanged and the Django suite was not rerun. No new event bus, audio framework or presentation event layer was added. Owner hands-on acceptance of this extraction remains separate from automated qualification.

## Full Demo distribution (2026-10-26)

Replaced Game Starter and Minimal TMP Client with one Full Demo sample. Removed both generators; saved scene/prefabs are authoritative. Failing-first layout checks now guard sample manifest, synchronized scripts/art/scene, generator removal and serialized GUID closure. External component GUIDs are pinned in the Python test so Python-only CI requires no Unity cache. Fresh review reproduced that CI issue before the correction and verified it fixed.

Clean consumer passed **212 portable EditMode tests**, import without TMP, local upgrade with consumer files preserved, Package Manager import and strict macOS build of the saved Full Demo, plus the native SDK probe against isolated Django (all modules, visuals/preview, mutations and teardown). Root compiler has no C# errors. No backend mechanics changed. This run builds the actual imported scene; it does not assert new manual two-player acceptance of it.

## Unity project directory relocation (2026-10-26)

Moved the Unity project and its local generated state into repository `Unity/`. Assets, package metadata/GUIDs and runtime code remain unchanged. Repository backend/tools stay at root; source paths, ignores, CI triggers, harness launch paths and documentation links were updated. The new layout regression failed before the move; after relocation **8 Python layout checks, 267 EditMode and 146 PlayMode tests passed** using the main project at its new path. Shell syntax checks and local Markdown link checks passed; fresh-context review found no relocation issues. No temporary Unity project was created for this verification.
