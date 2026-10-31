# Use services in your own game

Initialize an account using [module composition](index.md#select-modules-explicitly), then use its service interfaces from your own UI. These examples use confirmed snapshots and backend IDs; no demo registry, avatar or Netcode session is needed for account commands.

## Read snapshots and bind a view

`HasSnapshot` means state is available. `IsBusy` disables overlapping actions; `RequiresRefresh` means a read is needed before another affected mutation. Render an unavailable state when no snapshot exists rather than displaying cached balances/ownership as confirmed.

This binding renders immediately, observes subsequent changes and detaches when the view closes. `render` is your view's drawing method; its arguments are the catalog (null while unavailable), busy flag and refresh-required flag.

```csharp
using System;
using MultiplayerLiveOpsToolkit.Shop;

public sealed class ShopViewBinding : IDisposable
{
    private readonly IShopService shop;
    private readonly Action<ShopSnapshot, bool, bool> render;

    public ShopViewBinding(IShopService shop,
        Action<ShopSnapshot, bool, bool> render)
    {
        this.shop = shop ?? throw new ArgumentNullException(nameof(shop));
        this.render = render ?? throw new ArgumentNullException(nameof(render));
        shop.StateChanged += Render;
        try { Render(); }
        catch { shop.StateChanged -= Render; throw; }
    }

    private void Render() => render(
        shop.HasSnapshot ? shop.Snapshot : null,
        shop.IsBusy, shop.RequiresRefresh);

    public void Dispose() => shop.StateChanged -= Render;
}
```

Create the binding when opening the screen; dispose it when closing. Iterate `ShopSnapshot.Offers.Values` for offer names, IDs and currency costs. Read ownership from Inventory and balances from Wallet. Those services have their own notifications: `Inventory.SnapshotChanged` and `Wallet.SnapshotChanged`. Subscribe with the same lifetime pattern.

## Purchase and recover an uncertain outcome

Put purchase commands on a durable account/game owner, separate from the panel. Drive and dispose the returned iterator there. Closing a screen only disposes its view binding; it must not cancel a purchase.

```csharp
using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Shop;

public static class ShopCommands
{
    public static IEnumerator PurchaseOffer(IShopService shop, string offerId,
        Action<string> showMessage)
    {
        // offerId comes from shop.Snapshot.Offers, not a display label.
        bool needsRefresh = false;
        var purchase = shop.Purchase(offerId,
            state => showMessage("Purchased " + state.PurchasedOfferId),
            error =>
            {
                showMessage(error.Message);
                needsRefresh = error.Code == ShopErrorCode.UnknownOutcome
                    || error.Code == ShopErrorCode.RefreshRequired;
            });
        try { yield return purchase; }
        finally { (purchase as IDisposable)?.Dispose(); }

        if (!needsRefresh) yield break;
        // Read current state; do not repeat the purchase.
        var refresh = shop.Refresh(
            () => showMessage("Account refreshed. Check ownership before buying again."),
            error => showMessage(error.Message));
        try { yield return refresh; }
        finally { (refresh as IDisposable)?.Dispose(); }
    }
}
```

Check `HasSnapshot`, `IsBusy` and `RequiresRefresh` before enabling Buy. A confirmed purchase updates Wallet and Inventory through the module's state ports; do not debit currency or grant ownership in the UI. Handle typed rejections such as `InsufficientFunds` and `AlreadyOwned` as player feedback. Treat `Misconfigured` as a configuration failure. If a refresh also fails, keep affected mutations disabled and offer another explicit refresh.

The coroutine above starts its module operation when driven. If your own owner instead calls a service method before starting its coroutine, that call can reserve state and notify observers synchronously. If those observers end the owner, dispose the reserved iterator instead of starting it. On terminal owner teardown, dispose pending module iterators and the account context. See the source demo's `ShopSampleController` and `AccessoryEquipService` for owner lifecycle handling.

## Preview, equip and restore

Configure a renderer and presenter using [Equipment visuals](equipment-visuals.md), then use item IDs from the Equipment definitions or a Shop offer's `ItemIds`.

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Equipment;

public static class EquipmentCommands
{
    public static void Preview(IEquipmentService equipment,
        IEnumerable<string> itemIds) => equipment.Preview(itemIds);

    public static void EndPreview(IEquipmentService equipment) => equipment.ClearPreview();

    public static IEnumerator Equip(IEquipmentService equipment,
        IEnumerable<string> ownedItemIds, Action onSuccess,
        Action<EquipmentError> onFailure)
    {
        var operation = equipment.ApplyItems(ownedItemIds, onSuccess, onFailure);
        try { yield return operation; }
        finally { (operation as IDisposable)?.Dispose(); }
    }
}
```

Preview accepts defined unowned cosmetics and changes local presentation only. Call `ClearPreview` when leaving the preview UI. `ApplyItems` overlays the requested items and resolves configured slot conflicts; pass every member ID to apply a set in one operation. The backend enforces ownership and expected revision. `Unequip(slotIds, ...)` and `Clear(...)` persist removals. After an uncertain outcome or revision conflict, refresh before another change.

For multiplayer, a confirmed account mutation still needs signed equipment verification before remote players see it. [Avatar integration](full-demo.md) binds the gate-approved public loadout; a local preview temporarily overrides that presentation and clears back to the approved look.

## Claim achievements and daily rewards

Render achievement entries from `achievements.Snapshot.Entries.Values`; enable Claim only for `entry.CanClaim`. Completion and reward claim are separate. No-reward completions have no claim action.

Render the daily calendar from `dailyRewards.Snapshot.Days` using its backend `Today` date. Enable Claim for `CanClaimToday`; do not derive eligibility from the device clock.

```csharp
using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Achievements;
using MultiplayerLiveOpsToolkit.DailyRewards;

public static class RewardCommands
{
    public static IEnumerator ClaimAchievement(IAchievementService achievements,
        string achievementId, Action onSuccess, Action<AchievementError> onFailure)
    {
        var operation = achievements.Claim(achievementId, onSuccess, onFailure);
        try { yield return operation; }
        finally { (operation as IDisposable)?.Dispose(); }
    }

    public static IEnumerator ClaimDailyReward(IDailyRewardService dailyRewards,
        Action onSuccess, Action<DailyRewardError> onFailure)
    {
        var operation = dailyRewards.ClaimToday(onSuccess, onFailure);
        try { yield return operation; }
        finally { (operation as IDisposable)?.Dispose(); }
    }
}
```

`ClaimToday` sends the date in the displayed service snapshot. On `DayChanged` or `AlreadyClaimed`, call Daily Rewards `Refresh` and redraw. On `UnknownOutcome` or `RefreshRequired`, refresh the affected service and check confirmed state before allowing another claim. Never automatically repeat a claim. Backend results update selected Wallet/Inventory state together with the reward state.

Refresh achievements after relevant confirmed purchases or attendance when your game needs updated progress. Views subscribe to `StateChanged`; `AchievementUnlocked` is available for completion notifications. Keep these subscriptions scoped to their owners.

## Implement a backend adapter

A custom backend implements the selected mechanic's `I*Backend` interface. It supplies complete immutable snapshots, completes once, maps failures to that mechanic's typed errors, and respects cancellation/account changes. Authentication and transport belong to the game or its adapter, not the mechanic module.

For example, a **read-only wallet screen backed by an already authenticated native SDK cache** needs reads but does not offer conversion. This adapter takes a function returning that cache's confirmed snapshot and explicitly rejects conversion. The function must return null while no confirmed snapshot is available; it must never return another account's data.

```csharp
using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Wallet;

public sealed class ReadOnlyWalletBackend : IWalletBackend
{
    private readonly Func<WalletSnapshot> readConfirmedWallet;

    public ReadOnlyWalletBackend(Func<WalletSnapshot> readConfirmedWallet)
    {
        this.readConfirmedWallet = readConfirmedWallet
            ?? throw new ArgumentNullException(nameof(readConfirmedWallet));
    }

    public IEnumerator Fetch(Action<WalletSnapshot> onSuccess,
        Action<WalletError> onFailure)
    {
        var snapshot = readConfirmedWallet();
        if (snapshot == null)
            onFailure(new WalletError(WalletErrorCode.Unavailable,
                "The authenticated wallet snapshot is unavailable."));
        else
            onSuccess(new WalletSnapshot(snapshot.Balances.Values,
                Array.Empty<WalletConversionOffer>()));
        yield break;
    }

    public IEnumerator Convert(string offerId, long amount,
        Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure)
    {
        onFailure(new WalletError(WalletErrorCode.Rejected,
            "This wallet integration is read-only."));
        yield break;
    }
}
```

Pass this backend to `WalletModule` instead of `DjangoWalletBackend`, with Identity supplied by the game's composition. It exposes no conversion offers and performs no client-side grants. For a writable integration, implement conversions as authoritative backend transactions and return the complete resulting wallet; classify a dispatched request with no confirmed result as `UnknownOutcome`. Do not turn missing responses into successful snapshots or silently replay mutations.

A non-HTTP/native SDK game can compose Identity and the mechanic modules through `LiveOpsRuntime`; it does not need DjangoPlayerSession or Unity's HTTP PlayerContext. Add adapter tests for success, rejection, malformed results, stale-account delivery and cancellation against your chosen transport.
