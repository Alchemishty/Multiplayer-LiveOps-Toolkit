using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Core;
using MultiplayerLiveOpsToolkit.Identity;
using MultiplayerLiveOpsToolkit.Wallet;
using MultiplayerLiveOpsToolkit.Inventory;
using MultiplayerLiveOpsToolkit.Shop;
using NUnit.Framework;
using UnityEngine.Networking;

public class PlayerContextTests
{
    [Test]
    public void Initialize_TwoContexts_AuthenticateIndependentlyAndOwnTheirSessions()
    {
        var firstSession = new SessionStub();
        var secondSession = new SessionStub();
        var first = CreateContext(firstSession);
        var second = CreateContext(secondSession);
        Initialize(first, new BackendStub(firstSession, "first"));
        Initialize(second, new BackendStub(secondSession, "second"));
        Assert.That(first.CurrentPlayer.PlayerId, Is.EqualTo("first"));
        Assert.That(second.CurrentPlayer.PlayerId, Is.EqualTo("second"));
        first.Dispose();
        Assert.That(first.IsAuthenticated, Is.False);
        Assert.That(firstSession.DisposeCount, Is.EqualTo(1));
        Assert.That(second.IsAuthenticated, Is.True);
        Assert.That(secondSession.DisposeCount, Is.EqualTo(0));
        second.Dispose();
    }

    [Test]
    public void Initialize_RejectedLogin_CanRetryWithoutReplacingContext()
    {
        var session = new SessionStub();
        using (var context = CreateContext(session))
        {
            LiveOpsError failure = null;
            Drain(context.Initialize(new ProviderStub(), new BackendStub(session, "first") { ShouldReject = true },
                () => Assert.Fail("Rejected login became ready"), error => failure = error));
            Assert.That(failure, Is.Not.Null);
            Assert.That(context.IsAuthenticated, Is.False);
            Initialize(context, new BackendStub(session, "second"));
            Assert.That(context.CurrentPlayer.PlayerId, Is.EqualTo("second"));
            Assert.That(session.DisposeCount, Is.EqualTo(0));
        }
    }

    [Test]
    public void Dispose_RepeatedShutdown_ClearsIdentityAndDisposesSessionOnce()
    {
        var session = new SessionStub();
        var context = CreateContext(session);
        var backend = new BackendStub(session, "player");
        Initialize(context, backend);
        context.Dispose();
        context.Dispose();
        Assert.That(backend.ClearCount, Is.EqualTo(1));
        Assert.That(session.DisposeCount, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => { var player = context.CurrentPlayer; });
        Assert.Throws<InvalidOperationException>(() => context.Initialize(new ProviderStub(), backend, () => { }, error => { }));
    }

    [Test]
    public void Dispose_BackendCleanupThrows_StillDisposesSession()
    {
        var session = new SessionStub();
        var context = CreateContext(session);
        Initialize(context, new BackendStub(session, "player") { ShouldThrowOnClear = true });
        Assert.Throws<AggregateException>(() => context.Dispose());
        Assert.That(session.DisposeCount, Is.EqualTo(1));
        Assert.That(context.IsAuthenticated, Is.False);
        context.Dispose();
    }

    [Test]
    public void SessionRejected_CleansBeforeNotifyingAndDetachesSubscription()
    {
        var session = new SessionStub();
        var context = CreateContext(session);
        Initialize(context, new BackendStub(session, "player"));
        int notifications = 0;
        Action<string> onEnded = reason =>
        {
            notifications++;
            Assert.That(reason, Is.EqualTo("expired"));
            Assert.That(context.IsAuthenticated, Is.False);
            Assert.That(session.DisposeCount, Is.EqualTo(1));
        };
        context.SessionEnded += onEnded;
        try
        {
            session.RejectSession(0, "expired");
            session.RejectSession(0, "expired");
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(session.SubscriberCount, Is.EqualTo(0));
        }
        finally { context.SessionEnded -= onEnded; context.Dispose(); }
    }

    [Test]
    public void Initialize_UnstartedOperation_CannotBeReplacedBySecondInitialization()
    {
        var session = new SessionStub();
        using (var context = CreateContext(session))
        {
            context.Initialize(new ProviderStub(), new BackendStub(session, "first"), () => { }, error => { });
            Assert.Throws<InvalidOperationException>(() => context.Initialize(new ProviderStub(), new BackendStub(session, "second"), () => { }, error => { }));
        }
    }

    [Test]
    public void Constructor_MissingBindings_FailsImmediately()
    {
        Assert.Throws<ArgumentNullException>(() => new PlayerContext(null, new ProofStub()));
        Assert.Throws<ArgumentNullException>(() => new PlayerContext(new SessionStub(), null));
    }

    [Test]
    public void SessionRejected_ThrowingSubscriber_DoesNotSuppressLaterSubscriber()
    {
        var session = new SessionStub();
        var context = CreateContext(session);
        Initialize(context, new BackendStub(session, "player"));
        bool hasReceived = false;
        Action<string> throwing = reason => throw new InvalidOperationException("Subscriber failure");
        Action<string> later = reason => hasReceived = true;
        context.SessionEnded += throwing;
        context.SessionEnded += later;
        try
        {
            Assert.Throws<AggregateException>(() => session.RejectSession(0, "expired"));
            Assert.That(hasReceived, Is.True);
            Assert.That(session.DisposeCount, Is.EqualTo(1));
        }
        finally { context.SessionEnded -= throwing; context.SessionEnded -= later; context.Dispose(); }
    }

    [Test]
    public void Dispose_DuringAuthentication_CancelsRuntimeAndDisposesSession()
    {
        var session = new SessionStub();
        var context = CreateContext(session);
        int failures = 0;
        var routine = context.Initialize(new WaitingProvider(), new BackendStub(session, "player"),
            () => Assert.Fail("Cancelled context became ready"), error => failures++);
        Assert.That(routine.MoveNext(), Is.True);
        context.Dispose();
        Assert.That(failures, Is.EqualTo(1));
        Assert.That(session.DisposeCount, Is.EqualTo(1));
        Assert.That(context.IsAuthenticated, Is.False);
        Assert.That(routine.MoveNext(), Is.False);
        (routine as IDisposable)?.Dispose();
    }

    [Test]
    public void Initialize_SelectedWallet_LoadsAfterIdentityAndClearsWithContext()
    {
        var session = new SessionStub();
        var wallet = new WalletModule(new WalletBackendStub(session), exception => Assert.Fail(exception.Message));
        using (var context = CreateContext(session))
        {
            Drain(context.Initialize(new ProviderStub(), new BackendStub(session, "player"), () => { }, error => Assert.Fail(error.Message), new[] { wallet }));
            Assert.That(context.IsAuthenticated, Is.True);
            Assert.That(wallet.Snapshot.Balances["gold"].Amount, Is.EqualTo(10));
        }
        Assert.That(wallet.HasSnapshot, Is.False);
    }
    [Test]
    public void Initialize_InventoryWithoutWallet_LoadsAfterIdentityAndClearsOnTeardown()
    {
        var session = new SessionStub();
        var inventory = new InventoryModule(new InventoryBackendStub(session), exception => Assert.Fail(exception.Message));
        using (var context = CreateContext(session))
        {
            Drain(context.Initialize(new ProviderStub(), new BackendStub(session, "player"), () => { }, error => Assert.Fail(error.Message), new[] { inventory }));
            Assert.That(context.IsAuthenticated, Is.True);
            Assert.That(inventory.Snapshot.Items.ContainsKey("hat"), Is.True);
        }
        Assert.That(inventory.HasSnapshot, Is.False);
    }
    [Test]
    public void Initialize_ShopSelectedBeforeDependencies_ResolvesOrderAndOwnsAllState()
    {
        var session = new SessionStub();
        var wallet = new WalletModule(new WalletBackendStub(session), exception => Assert.Fail(exception.Message));
        var inventory = new InventoryModule(new InventoryBackendStub(session), exception => Assert.Fail(exception.Message));
        var shop = new ShopModule(new ShopBackendStub(), wallet, inventory, exception => Assert.Fail(exception.Message));
        using (var context = CreateContext(session))
        {
            Drain(context.Initialize(new ProviderStub(), new BackendStub(session, "player"), () => { }, error => Assert.Fail(error.Message), new ILiveOpsModule[] { shop, inventory, wallet }));
            Assert.That(context.IsAuthenticated && shop.HasSnapshot && wallet.HasSnapshot && inventory.HasSnapshot, Is.True);
        }
        Assert.That(shop.HasSnapshot || wallet.HasSnapshot || inventory.HasSnapshot, Is.False);
    }
    private sealed class ShopBackendStub : IShopBackend
    {
        public IEnumerator Fetch(Action<ShopState> onSuccess, Action<ShopError> onFailure)
        {
            onSuccess(new ShopState(new ShopSnapshot(Array.Empty<ShopOffer>()), new WalletSnapshot(new[] { new WalletBalance("gold", 10) }, Array.Empty<WalletConversionOffer>()), new InventorySnapshot(Array.Empty<CosmeticItem>(), Array.Empty<CosmeticSet>())));
            yield break;
        }
        public IEnumerator Purchase(string offerId, Action<ShopState> onSuccess, Action<ShopError> onFailure) { throw new NotSupportedException(); }
    }
    private sealed class InventoryBackendStub : IInventoryBackend
    {
        private readonly SessionStub session;
        public InventoryBackendStub(SessionStub session) { this.session = session; }
        public IEnumerator Fetch(Action<InventorySnapshot> onSuccess, Action<InventoryError> onFailure)
        {
            Assert.That(session.IsActive, Is.True);
            onSuccess(new InventorySnapshot(new[] { new CosmeticItem("hat", "Hat", "headwear") }, Array.Empty<CosmeticSet>()));
            yield break;
        }
    }

    private sealed class WalletBackendStub : IWalletBackend
    {
        private readonly SessionStub session;
        public WalletBackendStub(SessionStub session) { this.session = session; }
        public IEnumerator Fetch(Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure)
        {
            Assert.That(session.IsActive, Is.True, "Identity must initialize before Wallet");
            onSuccess(new WalletSnapshot(new[] { new WalletBalance("gold", 10) }, Array.Empty<WalletConversionOffer>()));
            yield break;
        }
        public IEnumerator Convert(string offerId, long amount, Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure) { throw new NotSupportedException(); }
    }

    private sealed class WaitingProvider : IIdentityProvider
    {
        public IEnumerator GetCredential(Action<IdentityCredential> onCredential) { while (true) yield return null; }
    }

    private static PlayerContext CreateContext(SessionStub session) => new PlayerContext(session, new ProofStub());
    private static void Initialize(PlayerContext context, BackendStub backend)
    {
        bool isReady = false;
        Drain(context.Initialize(new ProviderStub(), backend, () => isReady = true, error => Assert.Fail(error.Message)));
        Assert.That(isReady, Is.True);
        Assert.That(context.IsAuthenticated, Is.True);
    }
    private static void Drain(IEnumerator routine)
    {
        var pending = new Stack<IEnumerator>();
        pending.Push(routine);
        while (pending.Count > 0)
        {
            var current = pending.Peek();
            if (!current.MoveNext()) { pending.Pop(); (current as IDisposable)?.Dispose(); }
            else if (current.Current is IEnumerator nested) pending.Push(nested);
        }
    }
    private sealed class ProviderStub : IIdentityProvider
    {
        public IEnumerator GetCredential(Action<IdentityCredential> onCredential) { onCredential(new IdentityCredential("test", "credential")); yield break; }
    }
    private sealed class BackendStub : IIdentityBackend
    {
        private readonly SessionStub session;
        private readonly string playerId;
        public bool ShouldReject;
        public bool ShouldThrowOnClear;
        public int ClearCount;
        public BackendStub(SessionStub session, string playerId) { this.session = session; this.playerId = playerId; }
        public IEnumerator ExchangeCredential(IdentityCredential credential, Action<PlayerIdentity> onAuthenticated)
        {
            if (ShouldReject) throw new InvalidOperationException("Rejected credential");
            session.IsActive = true;
            onAuthenticated(new PlayerIdentity(playerId, "Display name"));
            yield break;
        }
        public void ClearSession() { ClearCount++; session.IsActive = false; if (ShouldThrowOnClear) throw new InvalidOperationException("Cleanup failure"); }
    }
    private sealed class ProofStub : IIdentityProofBackend
    {
        public IEnumerator RequestProof(string sessionId, string challenge, Action<string> onAssertion, Action<IdentityProofRequestFailure> onFailure) { yield break; }
    }
    private sealed class SessionStub : IAuthenticatedSession
    {
        public event Action<string> SessionEnded;
        public int SubscriberCount => SessionEnded == null ? 0 : SessionEnded.GetInvocationList().Length;
        public int SessionVersion => 0;
        public bool IsActive { get; set; }
        public int DisposeCount;
        public IEnumerator Send(UnityWebRequest request, ApiResponse response, Action<bool> onComplete = null) { yield break; }
        public void RejectSession(int expectedVersion, string reason = null) { IsActive = false; SessionEnded?.Invoke(reason); }
        public void Dispose() { DisposeCount++; IsActive = false; }
    }
}
