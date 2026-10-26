# Multiplayer avatar and game starter integration

The package supplies reusable account modules, cosmetic rendering and verified multiplayer avatar integration. Movement, animation replication and camera controls are optional. Import **Full Demo** for the complete saved scene, character/cosmetics and UI, or compose the runtime directly with your own presentation. See [sample setup](../Samples~/FullDemo/README.md). No generator or minimal sample is shipped.

## Runtime building blocks

| Assembly/type | Responsibility |
|---|---|
| Netcode.Avatars / AvatarRegistry | One session's avatar lookup and local-avatar change observation; no global SDK locator |
| NetworkPlayerIdentity | Server-written public identity from the verified account binding; no TMP dependency |
| VerifiedPlayerSpawner | Gated player creation/visibility and approved equipment publication |
| PlayerAvatarSetup | Register/despawn the avatar through its NetworkManager's AvatarRegistry |
| PlayerAccessory | Replicated canonical equipment and local preview presentation through IEquipmentRenderer |
| ClientHostConnection | Optional client-first/host-fallback connection policy with account-lifetime checks |
| CharacterControls | Optional client-authoritative movement, transform/animation replication and registry-bound orbit camera |
| Backends.Django / DjangoPlayerModules | Select and compose reference-backend account services through an owned PlayerContext |

## Compose an avatar/session

Put NetworkManager, the desired transport, NetcodeIdentityGate, AvatarRegistry and VerifiedPlayerSpawner on one session object. Set NetworkConfig.PlayerPrefab to a prefab containing NetworkObject, NetworkPlayerIdentity and PlayerAvatarSetup. For equipment-enabled sessions add PlayerAccessory and a configured CosmeticRenderer (or your own IEquipmentRenderer). Configure identity/equipment trust and account proof providers on the gate before starting it.

VerifiedPlayerSpawner uses the gate's approved identity/equipment. Unverified peers cannot observe its players; verified peers receive current players, and new equipment is published through the existing public state. Server roles can run without a local account or any nameplate UI. The gate itself remains usable without player spawning; the prefab requirement belongs to avatar spawning.

For the local player, bind its equipment service through PlayerAccessory.BindPresentation. Public remote state remains canonical while local previews temporarily override it. Unbind/dispose on lifetime changes. The sample's equipment command owner performs this binding when AvatarRegistry reports a local replacement. Registry removal identifies the old avatar, so its late despawn cannot remove a newer replacement.

Add ClientNetworkTransform/SimplePlayerMovement if using the example movement model. Set an Animator/controller and ClientNetworkAnimator for animated models. Bind ThirdPersonCamera to the session AvatarRegistry; it follows replacements and detaches across disable/reenable. Character controls use Unity's legacy Input API and are optional; authoritative movement/combat or another input system can replace them.

For explicit roles call the gate's StartClient, StartHost or StartServer. ClientHostConnection.Connect offers the example's local client-first fallback when desired. Its owner supplies a live authentication/owner predicate, drives/disposes the coroutine and handles failures. Disabling the owner must invalidate that predicate. A verified connection belongs to the gate/session; reconnect/matchmaking/navigation policy belongs to the game.

## Compose account modules

Create a DjangoPlayerSession and supply it as both IAuthenticatedSession and ITokenStore to DjangoPlayerModules. Select explicit DjangoModules flags (Identity is always present); Shop requires Wallet/Inventory and Equipment requires Inventory. Initialization validates additional content-dependent reward requirements. A failed initialization can be retried with fresh module instances. A rejected concurrent initialization preserves the already accepted services.

```csharp
account = new DjangoPlayerModules(url, session, session, DjangoModules.All, ReportSubscriberException);
yield return account.Initialize(provider, OnReady, OnFailure);
// account.Context owns session lifetime; account.Wallet, Inventory, Equipment, etc. expose selected services.
// Dispose account during owner teardown; create a new account/session after termination.
```

This composer is a Django convenience, not a required backend abstraction. Other backends can compose their own supplied module adapters with PlayerContext/LiveOpsRuntime. UI and network startup are not dependencies of the account composer.

## Events and extension

State owners expose their existing service/network events. Views, camera bindings and preview owners subscribe for their own lifetime and unsubscribe during matching teardown. Observer exceptions are isolated where notifications are independent; verification/spawning failures still follow the gate's terminal rules. Audio or other game integrations can observe applicable service events or their own successful command completions. No global event bus or audio framework is required or included.

The full sample supplies its working TMP panels, toasts, login UI, preview/command owners and development reset. These are optional presentation/composition examples; the backend mechanics, avatar verification/replication, renderer and controls remain in runtime assemblies. Art, backend hosting, game navigation and additional gameplay remain consuming-game responsibilities.
