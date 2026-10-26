# Install and compose

Add this package.json through Package Manager > + > Add package from disk, or use a local `file:` manifest dependency. Target Unity 2022.3; this preview was exercised with 2022.3.33f1. Runtime dependencies are NGO 1.9.1, Newtonsoft JSON 3.2.1 and UnityWebRequest. Installation does not start a backend, authenticate, build UI or start networking.

## Assemblies and namespaces

| Assembly/namespace suffix | Purpose |
|---|---|
| Core | Dependency resolution, lifecycle, typed initialization failures |
| Identity | Provider/backend contracts and verified public identity |
| Wallet | Confirmed balances and backend conversions |
| Inventory | Cosmetic ownership independent of Shop/Equipment |
| Shop | Backend offers and atomic purchases; explicit Wallet/Inventory state |
| Equipment | Canonical backend loadout, configured slots/conflicts, local preview and signed assertions |
| Achievements | Backend-confirmed progress/completion and explicit reward claims |
| DailyRewards | Backend-date monthly attendance/calendar and explicit claims |
| UnityIntegration | PlayerContext, IAuthenticatedSession, ApiResponse; no Django dependency |
| Backends.Django | Password provider, concrete HTTP adapters, in-memory token session |
| Netcode | Identity/equipment verification gate, with public trust material |

All suffixes follow `MultiplayerLiveOpsToolkit.`. The eight mechanic assemblies have no Unity references. Unity/Django/Netcode integration names moved from the old demo's global namespace; update using directives in an existing source consumer. Existing repository script GUIDs are preserved. This preview does not offer compatibility wrappers for the retired globals.

## Select modules explicitly

`LiveOpsRuntime` accepts explicit ILiveOpsModule instances and validates their declared dependencies before initialization. Module IDs: `identity`, `wallet`, `inventory`, `shop`, `equipment`, `achievements`, `daily-rewards`. Disabled modules are never inferred or initialized. Wallet/Inventory/Equipment/Achievements/Daily Rewards expose immutable snapshots and typed operations; no Unity UI type belongs in their backend/service contracts.

For the supplied Django integration, create an enabled DjangoPlayerSession and pass its URL/session explicitly into Django adapters. Create PlayerContext with that session and DjangoIdentityProofAPI, then initialize with a DjangoPasswordIdentityProvider, DjangoIdentityBackend and selected mechanics. PlayerContext supplies Identity. Do not also add a second IdentityModule. Currency reward schedules require an explicitly supplied Wallet state port; collections/cosmetic achievement rewards require Inventory. Shop requires both; Equipment requires Identity and Inventory. Missing required capabilities fail initialization.

A small Wallet composition in an owned coroutine:

```csharp
var session = owner.AddComponent<DjangoPlayerSession>();
var wallet = new WalletModule(new DjangoWalletBackend(backendUrl, session), Debug.LogException);
var context = new PlayerContext(session, new DjangoIdentityProofAPI(backendUrl, session));
yield return context.Initialize(
    new DjangoPasswordIdentityProvider(username, password),
    new DjangoIdentityBackend(backendUrl, session),
    () => { /* bind the game's view to wallet */ },
    error => { /* show the typed lifecycle error */ },
    new ILiveOpsModule[] { wallet });
```

Use Core, Wallet, UnityIntegration and Backends.Django namespaces, plus UnityEngine. Keep credentials out of scene/source files. Dispose context on terminal owner teardown. Create a new context/session for another account. Scope view event subscriptions independently from durable account command ownership. If a service reserves state before returning its IEnumerator, dispose the iterator if reservation observers stopped the owner; do not start a coroutine on a disabled GameObject.

DjangoPlayerSession.SetTokens requires its explicit renewal endpoint; DjangoIdentityBackend supplies it on login. There is no ApiConfig fallback or host PlayerPrefs deletion. Tokens stay in memory, cannot be sent across the authenticated origin boundary, and renewal is owned by the session rather than a panel.

## Backend replacement and authority

A custom backend implements the relevant I*Backend interfaces and returns complete validated immutable snapshots. For non-HTTP/native SDK integrations, compose Identity/Core directly through LiveOpsRuntime if the provided Unity HTTP PlayerContext is not appropriate. There is no requirement to instantiate Django adapters.

Purchases and claims are backend transactions, not client debit/grant sequences. Reject unowned/unknown IDs and raw client counters. Unknown mutation outcomes require authoritative refresh; never automatically retry a mutation. No optimistic account grant is allowed. Deliver callback completion once, cancel stale-account responses and clear private state on teardown. Consuming games decide transaction history, receipts and bookkeeping.

Daily claims send the displayed backend ISO date, preventing a stale midnight click from claiming a different day. Achievement completion is distinct from claiming a configured reward; no-reward completions need no claim. Equipment previews are local; only backend-approved canonical revisions may be publicly replicated.

## Multiplayer

NetcodeIdentityGate exposes verified peer identity/equipment state and rejects invalid proofs before visibility. Configure public proof trust material, game/environment/session/content policy and the required equipment phase. Clients own their private account operations. The game supplies transport destinations, spawn/visual integration and public replication; never replicate credentials, wallets, complete ownership or achievement progress. Dedicated verifiers need no local player credentials or visual assets. A required visual mapping failure is a fatal consuming-game configuration error.

## Tests and upgrades

Add the package name to the project's `testables` and install Test Framework 1.3.9 to run package tests. Test assemblies are `MultiplayerLiveOpsToolkit.Tests.EditMode` and `.Tests.PlayMode`. Demo tests remain outside the package. Test dependencies do not become production startup requirements.

The repository's `tools/verify_package_consumer.py` exercises a fresh project without TMP, portable tests, a local preview-version update preserving consumer scene/file bytes, and native SDK/custom UI consumers against isolated Django. This qualifies the current installation/update flow; future API-breaking upgrades need explicit migration guidance. Never edit installed package source to configure a game. Imported Samples belong in consumer Assets; reimport with overwrite can replace sample customizations.

Additional platform and security qualification remains pending. Unity 6 and non-cosmetic inventory are outside this preview.

## Optional visual customization

[Equipment visuals](equipment-visuals.md) provides package-owned attachment, skinned replacement, rig/slot bindings, pooling and local/public presentation. Character and cosmetic art remain consuming-game assets.

## Complete multiplayer demo

[Avatar and demo integration](full-demo.md) covers the packaged avatar registry/spawner, public identity/equipment, optional character controls and account composition. Import Full Demo and open its saved ExampleScene for the working scene/art/UI. See [setup](../Samples~/FullDemo/README.md). No scene generation is required.
