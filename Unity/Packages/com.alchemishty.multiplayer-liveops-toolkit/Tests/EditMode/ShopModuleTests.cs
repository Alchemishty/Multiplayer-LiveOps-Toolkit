using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Shop;
using MultiplayerLiveOpsToolkit.Wallet;
using MultiplayerLiveOpsToolkit.Inventory;
using NUnit.Framework;

public class ShopModuleTests
{
    private WalletModule wallet;
    private InventoryModule inventory;
    private ShopModule shop;
    private BackendStub backend;
    private static ShopState State(bool hasPurchased = false)
    {
        var offer = new ShopOffer("hat-offer", "Hat", "headwear", false, new[] { "hat" }, new[] { new ShopPrice("gold", 25) });
        return new ShopState(new ShopSnapshot(new[] { offer }), new WalletSnapshot(new[] { new WalletBalance("gold", hasPurchased ? 75 : 100) }, Array.Empty<WalletConversionOffer>()),
            new InventorySnapshot(hasPurchased ? new[] { new CosmeticItem("hat", "Hat", "headwear") } : Array.Empty<CosmeticItem>(), Array.Empty<CosmeticSet>()), hasPurchased ? "hat-offer" : null);
    }
    [SetUp]
    public void SetUp()
    {
        wallet = new WalletModule(new WalletBackend(), exception => Assert.Fail(exception.Message));
        inventory = new InventoryModule(new InventoryBackend(), exception => Assert.Fail(exception.Message));
        WalletModuleTests.Drain(wallet.Initialize());
        WalletModuleTests.Drain(inventory.Initialize());
        backend = new BackendStub();
        shop = new ShopModule(backend, wallet, inventory, exception => Assert.Fail(exception.Message));
        WalletModuleTests.Drain(shop.Initialize());
    }
    [TearDown]
    public void TearDown() { shop.Shutdown(); inventory.Shutdown(); wallet.Shutdown(); }
    [Test]
    public void Initialize_LoadsIndependentCatalog()
    {
        Assert.That(shop.Snapshot.Offers["hat-offer"].Costs["gold"], Is.EqualTo(25));
        Assert.That(wallet.IsBusy || inventory.IsBusy, Is.False);
    }
    [Test]
    public void Purchase_ObserversSeeChargeAndOwnershipTogether()
    {
        Action changed = () =>
        {
            Assert.That(wallet.Snapshot.Balances["gold"].Amount, Is.EqualTo(75));
            Assert.That(inventory.Snapshot.Items.ContainsKey("hat"), Is.True);
        };
        wallet.SnapshotChanged += changed;
        inventory.SnapshotChanged += changed;
        try
        {
            WalletModuleTests.Drain(shop.Purchase("hat-offer", state => { }, error => Assert.Fail(error.Message)));
            Assert.That(backend.PurchaseCount, Is.EqualTo(1));
        }
        finally { wallet.SnapshotChanged -= changed; inventory.SnapshotChanged -= changed; }
    }
    [Test]
    public void Purchase_BlocksConcurrentWalletAndInventoryOperations()
    {
        backend.ShouldWait = true;
        var operation = shop.Purchase("hat-offer", state => { }, error => { });
        Assert.That(operation.MoveNext(), Is.True);
        WalletError walletFailure = null;
        InventoryError inventoryFailure = null;
        WalletModuleTests.Drain(wallet.Refresh(() => Assert.Fail("Concurrent wallet request"), error => walletFailure = error));
        WalletModuleTests.Drain(inventory.Refresh(() => Assert.Fail("Concurrent inventory request"), error => inventoryFailure = error));
        Assert.That(walletFailure.Code, Is.EqualTo(WalletErrorCode.Busy));
        Assert.That(inventoryFailure.Code, Is.EqualTo(InventoryErrorCode.Busy));
        ((IDisposable)operation).Dispose();
        Assert.That(wallet.IsBusy || inventory.IsBusy || shop.IsBusy, Is.False);
    }
    [Test]
    public void Purchase_UnknownOutcome_InvalidatesBothStatesUntilRefreshWithoutRetry()
    {
        backend.ShouldLoseResponse = true;
        ShopError failure = null;
        WalletModuleTests.Drain(shop.Purchase("hat-offer", state => Assert.Fail("Unconfirmed purchase"), error => failure = error));
        Assert.That(failure.Code, Is.EqualTo(ShopErrorCode.UnknownOutcome));
        Assert.That(wallet.HasSnapshot || inventory.HasSnapshot, Is.False);
        WalletModuleTests.Drain(shop.Purchase("hat-offer", state => Assert.Fail("Second purchase"), error => failure = error));
        Assert.That(failure.Code, Is.EqualTo(ShopErrorCode.RefreshRequired));
        Assert.That(backend.PurchaseCount, Is.EqualTo(1));
        backend.ShouldLoseResponse = false;
        WalletModuleTests.Drain(shop.Refresh(() => { }, error => Assert.Fail(error.Message)));
        Assert.That(shop.RequiresRefresh, Is.False);
        Assert.That(wallet.HasSnapshot && inventory.HasSnapshot, Is.True);
    }
    [Test]
    public void Purchase_BusinessRejection_KeepsConfirmedStates()
    {
        backend.ShouldReject = true;
        ShopError failure = null;
        WalletModuleTests.Drain(shop.Purchase("hat-offer", state => Assert.Fail("Rejected purchase"), error => failure = error));
        Assert.That(failure.Code, Is.EqualTo(ShopErrorCode.InsufficientFunds));
        Assert.That(wallet.Snapshot.Balances["gold"].Amount, Is.EqualTo(100));
        Assert.That(inventory.Snapshot.Items, Is.Empty);
    }
    [Test]
    public void Shutdown_PendingPurchase_CancelsOnceAndReleasesBothReservations()
    {
        backend.ShouldWait = true;
        int failures = 0;
        var operation = shop.Purchase("hat-offer", state => Assert.Fail("Stopped shop completed"), error => failures++);
        Assert.That(operation.MoveNext(), Is.True);
        shop.Shutdown();
        ((IDisposable)operation).Dispose();
        Assert.That(failures, Is.EqualTo(1));
        Assert.That(wallet.IsBusy || inventory.IsBusy || shop.IsBusy, Is.False);
        Assert.That(shop.HasSnapshot, Is.False);
    }
    [Test]
    public void Purchase_DisposedBeforeExecution_ReleasesReservationsAndCancelsOnce()
    {
        int failures = 0;
        var operation = shop.Purchase("hat-offer", state => { }, error => failures++);
        ((IDisposable)operation).Dispose();
        Assert.That(failures, Is.EqualTo(1));
        Assert.That(wallet.IsBusy || inventory.IsBusy || shop.IsBusy, Is.False);
        Assert.That(backend.PurchaseCount, Is.Zero);
    }
    [Test]
    public void Models_RejectDuplicatePricesEmptyOffersAndNegativeAmounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShopPrice("gold", -1));
        Assert.Throws<ArgumentException>(() => new ShopOffer("offer", "Hat", "headwear", false, Array.Empty<string>(), Array.Empty<ShopPrice>()));
        Assert.Throws<ArgumentException>(() => new ShopOffer("offer", "Hat", "headwear", false, new[] { "hat" }, new[] { new ShopPrice("gold", 1), new ShopPrice("gold", 2) }));
    }
    [Test]
    public void Purchase_FinalIdleNotificationStopsShop_StillCompletesExactlyOnce()
    {
        int successes = 0;
        int failures = 0;
        Action stopWhenCompleted = () => { if (!shop.IsBusy) shop.Shutdown(); };
        shop.StateChanged += stopWhenCompleted;
        try
        {
            WalletModuleTests.Drain(shop.Purchase("hat-offer", state => successes++, error => failures++));
            Assert.That(successes + failures, Is.EqualTo(1));
        }
        finally { shop.StateChanged -= stopWhenCompleted; }
    }
    private sealed class WalletBackend : IWalletBackend
    {
        public IEnumerator Fetch(Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure) { onSuccess(State().Wallet); yield break; }
        public IEnumerator Convert(string offerId, long amount, Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure) { throw new NotSupportedException(); }
    }
    private sealed class InventoryBackend : IInventoryBackend
    {
        public IEnumerator Fetch(Action<InventorySnapshot> onSuccess, Action<InventoryError> onFailure) { onSuccess(State().Inventory); yield break; }
    }
    private sealed class BackendStub : IShopBackend
    {
        public bool ShouldWait;
        public bool ShouldLoseResponse;
        public bool ShouldReject;
        public int PurchaseCount;
        public IEnumerator Fetch(Action<ShopState> onSuccess, Action<ShopError> onFailure) { onSuccess(State()); yield break; }
        public IEnumerator Purchase(string offerId, Action<ShopState> onSuccess, Action<ShopError> onFailure)
        {
            PurchaseCount++;
            while (ShouldWait) yield return null;
            if (ShouldLoseResponse) onFailure(new ShopError(ShopErrorCode.UnknownOutcome, "Unknown"));
            else if (ShouldReject) onFailure(new ShopError(ShopErrorCode.InsufficientFunds, "Insufficient"));
            else onSuccess(State(true));
        }
    }
}
