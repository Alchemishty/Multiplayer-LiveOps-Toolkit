# Install and compose

[Install the toolkit](installation.md) for a fresh Unity project, then compose only the modules your game uses. [Service recipes](recipes.md) cover UI bindings, purchases, equipment, rewards and a custom backend adapter.

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

All suffixes follow `MultiplayerLiveOpsToolkit.`. The eight Core/mechanic assemblies have no Unity references. Integration types use the UnityIntegration, Backends.Django and Netcode namespaces.

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

See [installation: updates and tests](installation.md#updates-and-tests) for portable test setup and sample ownership. The repository's [testing guide](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/blob/main/docs/testing.md) records reproducible checks and qualification evidence. See [current support](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit#current-support) for platforms and scope limits.

## Optional visual customization

[Equipment visuals](equipment-visuals.md) provides package-owned attachment, skinned replacement, rig/slot bindings, pooling and local/public presentation. Character and cosmetic art remain consuming-game assets.

## Complete multiplayer demo

[Avatar and demo integration](full-demo.md) covers the packaged avatar registry/spawner, public identity/equipment, optional character controls and account composition. Follow [Full Demo setup](../Samples~/FullDemo/README.md#setup) for the saved scene, UI and input configuration.
