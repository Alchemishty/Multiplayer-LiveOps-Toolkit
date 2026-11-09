# Multiplayer LiveOps Toolkit

A modular Unity package for the live-ops layer of multiplayer games: player accounts, currencies, shops, cosmetic inventories, equipment, achievements and daily rewards, with server-verified cosmetics shared across Netcode for GameObjects sessions. Pick the modules you need, keep your own art and UI, and connect the included Django backend or your own.

https://github.com/user-attachments/assets/6a7ee96b-8809-4a31-9c5c-ef946abc443f

[![Unity 2022.3 | 6.3 LTS](https://img.shields.io/badge/Unity-2022.3%20%7C%206.3%20LTS-blue.svg)](https://unity.com/)
[![Netcode 1.9.1](https://img.shields.io/badge/Netcode-1.9.1-green.svg)](https://docs-multiplayer.unity3d.com/netcode/current/about/)
[![Version 0.1.0](https://img.shields.io/badge/version-0.1.0-orange.svg)](Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/CHANGELOG.md)
[![License: MIT](https://img.shields.io/badge/license-MIT-lightgrey.svg)](LICENSE)

---

## Table of Contents

- [Features](#features)
- [Architecture Overview](#architecture-overview)
- [Technical Highlights](#technical-highlights)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Core Systems](#core-systems)
- [Extending the Toolkit](#extending-the-toolkit)
- [Current Support](#current-support)
- [Repository Layout](#repository-layout)
- [Documentation](#documentation)
- [License](#license)

---

## Features

- **Accounts and identity** - Password sign-in, in-memory sessions with automatic token renewal, and signed public identity for multiplayer
- **Multi-currency wallet** - Backend-confirmed balances and conversion at configured rates
- **Shop** - Individual cosmetics and complete sets with backend-defined catalogs and multi-currency prices
- **Inventory** - Cosmetic ownership tracked independently of the shop
- **Equipment** - Slots, conflict rules (such as headwear hiding hair), saved loadouts and free local try-on previews
- **Cosmetic rendering** - Bone-attached accessories and skinned outfit parts, with base meshes hidden and restored automatically
- **Achievements** - Backend-observed progress with claimable currency or cosmetic rewards
- **Daily rewards** - Monthly calendars and attendance claims governed by the backend's date
- **Verified multiplayer cosmetics** - The host or dedicated server checks signed equipment proofs before other players see a look, including late joiners
- **Swappable backend** - A working Django reference backend, or your own through the module interfaces
- **Modular by design** - Initialize only the modules you use; none of them depends on the sample UI

---

## Architecture Overview

```
┌──────────────────────── Player's game client ────────────────────────┐
│                                                                       │
│   Your UI ──► Services (IShopService, IWalletService, ...)            │
│                    │                                                  │
│   ┌────────────────▼──────────────────────────────────────────────┐   │
│   │ LiveOpsRuntime (Core)                                         │   │
│   │ Identity · Wallet · Inventory · Shop · Equipment ·            │   │
│   │ Achievements · DailyRewards        (engine-free modules)      │   │
│   └────────────────┬──────────────────────────────────────────────┘   │
│                    │ I*Backend interfaces                             │
│   ┌────────────────▼─────────────┐   ┌─────────────────────────────┐  │
│   │ Backend adapters             │   │ EquipmentVisuals            │  │
│   │ (Backends.Django or yours)   │   │ CosmeticRenderer, previews  │  │
│   └────────────────┬─────────────┘   └─────────────────────────────┘  │
└────────────────────┼──────────────────────────────┬───────────────────┘
                     │ account requests             │ identity + equipment proofs
                     ▼                              ▼
        ┌─────────────────────────┐    ┌──────────────────────────────┐
        │ Django or your backend  │    │ Host or dedicated server     │
        │ prices, balances,       │    │ NetcodeIdentityGate verifies │
        │ ownership, rewards,     │    │ proofs, VerifiedPlayerSpawner│
        │ signed proofs           │    │ shares approved looks        │
        └─────────────────────────┘    └──────────────┬───────────────┘
                                                      ▼
                                              Other players
```

### Module Map

| Assembly (`MultiplayerLiveOpsToolkit.*`) | Responsibility |
| --- | --- |
| `Core` | Explicit module selection, dependency ordering, lifecycle and rollback |
| `Identity` | Provider/backend contracts and verified public identity |
| `Wallet` | Confirmed balances and backend conversions |
| `Inventory` | Cosmetic ownership, independent of Shop and Equipment |
| `Shop` | Backend offers and atomic multi-currency purchases |
| `Equipment` | Canonical loadout, slots and conflicts, local previews, signed assertions |
| `Achievements` | Backend-confirmed progress and explicit reward claims |
| `DailyRewards` | Backend-date monthly attendance and claims |
| `EquipmentVisuals` | `CosmeticRenderer`, visual catalogs and preview presentation |
| `Netcode`, `Netcode.Avatars` | Verification gate, verified spawning, approved-look replication |
| `UnityIntegration`, `Backends.Django` | Player context and session contracts; the Django HTTP adapters |

The eight Core and mechanic assemblies have no Unity engine references, so their rules are testable in isolation.

### Authority Model

| Concern | Owner |
| --- | --- |
| Prices, balances, ownership, rewards | Backend; the client never debits or grants |
| What other players see you wearing | Host or dedicated server, after verifying a signed equipment proof |
| Try-on previews | Local client only; never replicated |
| Movement and animation (optional integration) | Owning client |

Wallets, full inventories, credentials and private progress are never replicated to other players.

---

## Technical Highlights

### Explicit Module Composition

Modules are selected explicitly and their dependencies validated before anything initializes. A wallet-only account:

```csharp
var session = owner.AddComponent<DjangoPlayerSession>();
var wallet = new WalletModule(new DjangoWalletBackend(backendUrl, session), Debug.LogException);
var context = new PlayerContext(session, new DjangoIdentityProofAPI(backendUrl, session));
yield return context.Initialize(
    new DjangoPasswordIdentityProvider(username, password),
    new DjangoIdentityBackend(backendUrl, session),
    () => { /* bind your view to wallet */ },
    error => { /* show the typed lifecycle error */ },
    new ILiveOpsModule[] { wallet });
```

Missing dependencies, cycles and duplicates fail before any side effect, and a failed initialization rolls back the modules that had started.

### Backend-Confirmed Purchases

A purchase is one backend transaction. Wallet and Inventory update together from the confirmed result, so no observer ever sees currency charged without the item granted. An uncertain outcome triggers a read, never a repeated purchase:

```csharp
var purchase = shop.Purchase(offerId,
    state => showMessage("Purchased " + state.PurchasedOfferId),
    error => needsRefresh = error.Code == ShopErrorCode.UnknownOutcome
                         || error.Code == ShopErrorCode.RefreshRequired);
try { yield return purchase; }
finally { (purchase as IDisposable)?.Dispose(); }

if (needsRefresh)
    yield return shop.Refresh(onRefreshed, onRefreshFailed); // read state; do not buy again
```

### Previews Versus Approved Looks

Players can try on anything for free. A preview changes only the local presentation; equipping persists a full loadout through the backend, which enforces ownership and slot rules:

```csharp
equipment.Preview(itemIds);        // local only, unowned items allowed
equipment.ClearPreview();          // back to the approved look
yield return equipment.ApplyItems(ownedItemIds, onSuccess, onFailure); // backend-validated
```

Before remote players see a new look, the client sends a signed equipment proof. The server verifies it against the player's verified identity, session and content policy, and only then replicates the approved item IDs.

---

## Installation

### Package Manager (UPM)

1. Clone this repository with Git LFS and run `git lfs pull` for the demo art.
2. In your project's Package Manager, choose **+ > Install package from disk**.
3. Select `Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/package.json`.

The runtime imports without demo assets, TextMeshPro or uGUI, and installing it does not start a backend or network session.

### Classic .unitypackage

Download the archive from [Releases](https://github.com/Alchemishty/Multiplayer-LiveOps-Toolkit/releases), install the dependencies below, then use **Assets > Import Package > Custom Package**. Install only one format per project.

### Dependencies

| Package | Unity 2022.3 | Unity 6.3 LTS | Required |
| --- | --- | --- | --- |
| Netcode for GameObjects | 1.9.1 | 2.13.3 | Yes |
| Newtonsoft Json | 3.2.1 | 3.2.2 | Yes |
| TextMeshPro | 3.0.6 | Included in uGUI | Full Demo only |
| uGUI | 1.0.0 | 2.0.0 | Full Demo only |

Full details: [Install the toolkit](Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/installation.md).

---

## Quick Start

### 1. Run the Demo

The Unity project is in **`Unity/`**; add that folder to Unity Hub. Follow [Run the demo](Documentation/getting-started.md) to start the Django backend, create accounts, configure public verification trust and run two instances side by side.

### 2. Compose Your Own Account

Create a `PlayerContext` with the modules your game uses, as in [Explicit Module Composition](#explicit-module-composition). Shop needs Wallet and Inventory; Equipment needs Inventory.

### 3. Bind Your UI

Subscribe to service notifications such as `StateChanged` and `SnapshotChanged`, and render from confirmed snapshots. Keep purchase and claim commands on a durable owner rather than a panel, so closing a screen never cancels a transaction. See the [service recipes](Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/recipes.md).

### 4. Add Multiplayer

Start sessions through `NetcodeIdentityGate` and spawn players with `VerifiedPlayerSpawner`. See [avatar integration](Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/full-demo.md) and the [verification protocol](Documentation/multiplayer-identity-verification.md).

---

## Core Systems

| System | What it gives you | Guide |
| --- | --- | --- |
| Identity | Sign-in, renewal and verified public identity | [Verification protocol](Documentation/multiplayer-identity-verification.md) |
| Wallet, Shop, Inventory | Balances, conversions, atomic purchases and ownership | [Recipes](Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/recipes.md) |
| Equipment and visuals | Slots, conflicts, loadouts, previews and cosmetic rendering | [Equipment visuals](Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/equipment-visuals.md) |
| Achievements and Daily Rewards | Backend-observed progress, calendars and claims | [Recipes](Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/recipes.md) |
| Multiplayer avatars | Verified spawning and approved-look replication, late joiners included | [Avatar integration](Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/Documentation~/full-demo.md) |
| Backend | Django reference server with admin and seed data | [Backend operations](Documentation/backend-operations.md) |

---

## Extending the Toolkit

- **Your own backend** - Implement the relevant `I*Backend` interfaces and return complete, validated snapshots. Django adapters are optional.
- **Your own renderer** - Implement `IEquipmentRenderer`, or keep `CosmeticRenderer` and swap the character, catalogs and rig bindings.
- **Your own controls** - Keep the optional movement, animation replication and third-person camera, or replace them.
- **Your own UI** - Services expose snapshots and events only; no Unity UI type appears in their contracts.

---

## Current Support

- **Unity:** 2022.3 and Unity 6.3 LTS, tested with 2022.3.33f1 and 6000.3.27f1. See [Unity 6 setup](Documentation/unity6.md).
- **Platforms:** macOS desktop and Android, verified on an ARM64 Android 13 emulator. See [Android setup](Documentation/android.md). Physical Android devices, iOS, Windows, Linux and Unity's dedicated-server build target should work but are untested.
- **Inventory:** cosmetic ownership. Item stacks, consumables and equipment stats are out of scope.
- **Backend:** Django is a reference implementation; games choose their own hosting and economy policies.
- **Availability:** authentication, renewal and handshake failures disconnect. Detecting an idle backend outage immediately is an open gap.

See the [roadmap](Documentation/roadmap.md) for what comes next.

---

## Repository Layout

| Path | Contents |
| --- | --- |
| `Unity/Packages/com.alchemishty.multiplayer-liveops-toolkit/` | The reusable package, its guides and portable tests |
| `Unity/Assets/` | The playable Full Demo, also shipped as the package's optional sample |
| `backend/` | The Django reference backend |
| `Documentation/` | Setup, integration and backend guides |
| `docs/`, `enforcement/` | Contributor architecture, testing and development rules |

---

## Documentation

- [Project documentation index](Documentation/README.md)
- [Architecture decisions](Documentation/architecture-decisions.md)
- [Technical architecture](docs/architecture.md) and [testing](docs/testing.md) for contributors

---

## License

Toolkit code is licensed under [MIT](LICENSE). Demo art and third-party dependencies keep their own licenses; see the [demo credits](Unity/Assets/Models/CREDITS.md).
