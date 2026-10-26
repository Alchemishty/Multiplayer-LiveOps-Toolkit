using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Wallet;
using NUnit.Framework;

public class WalletModuleTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void Operation_DisposedBeforeStarting_ReleasesReservationWithoutDispatch(bool isConversion)
    {
        var backend = new BackendStub();
        var module = Ready(backend);
        var confirmedSnapshot = module.Snapshot;
        int failures = 0;
        Action<WalletError> onFailure = error => { failures++; Assert.That(error.Code, Is.EqualTo(WalletErrorCode.Cancelled)); };
        try
        {
            var operation = isConversion ? module.Convert("gears-to-gold-v1", 1, () => Assert.Fail("Cancelled conversion succeeded"), onFailure) : module.Refresh(() => Assert.Fail("Cancelled refresh succeeded"), onFailure);
            ((IDisposable)operation).Dispose();
            ((IDisposable)operation).Dispose();
            Assert.That(module.IsBusy, Is.False);
            Assert.That(failures, Is.EqualTo(1));
            Assert.That(module.Snapshot, Is.SameAs(confirmedSnapshot));
            Assert.That(operation.MoveNext(), Is.False);
            Assert.That(backend.FetchCount, Is.EqualTo(1));
            Assert.That(backend.ConvertCount, Is.Zero);
            bool hasRefreshed = false;
            Drain(module.Refresh(() => hasRefreshed = true, error => Assert.Fail(error.Message)));
            Assert.That(hasRefreshed, Is.True);
            Assert.That(backend.FetchCount, Is.EqualTo(2));
            Assert.That(failures, Is.EqualTo(1));
        }
        finally { module.Shutdown(); }
    }
    private static WalletSnapshot State(long gold = 100, long gears = 5) => new WalletSnapshot(
        new[] { new WalletBalance("gold", gold), new WalletBalance("gears", gears) },
        new[] { new WalletConversionOffer("gears-to-gold-v1", "gears", "gold", 1, 25) });
    private static WalletModule Ready(BackendStub backend)
    {
        var module = new WalletModule(backend, exception => Assert.Fail(exception.Message));
        Drain(module.Initialize());
        Assert.That(module.HasSnapshot, Is.True);
        return module;
    }
    [Test]
    public void Initialize_IndependentWallets_DoNotShareBalances()
    {
        var first = Ready(new BackendStub { State = State(10) });
        var second = Ready(new BackendStub { State = State(20) });
        first.Shutdown();
        Assert.That(first.HasSnapshot, Is.False);
        Assert.That(second.Snapshot.Balances["gold"].Amount, Is.EqualTo(20));
        second.Shutdown();
    }
    [Test]
    public void Refresh_PublishesWholeImmutableSnapshotAfterBackendConfirmation()
    {
        var backend = new BackendStub();
        var module = Ready(backend);
        var original = module.Snapshot;
        int notifications = 0;
        Action changed = () => { notifications++; Assert.That(module.Snapshot.Balances["gold"].Amount, Is.EqualTo(150)); };
        module.SnapshotChanged += changed;
        try
        {
            backend.State = State(150, 3);
            Drain(module.Refresh(() => { }, error => Assert.Fail(error.Message)));
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(original.Balances["gold"].Amount, Is.EqualTo(100));
            Assert.That(module.IsBusy, Is.False);
        }
        finally { module.SnapshotChanged -= changed; module.Shutdown(); }
    }
    [Test]
    public void Convert_ConfirmedResponse_AppliesAuthoritativeSnapshot()
    {
        var backend = new BackendStub();
        var module = Ready(backend);
        bool hasCompleted = false;
        Drain(module.Convert("gears-to-gold-v1", 2, () => hasCompleted = true, error => Assert.Fail(error.Message)));
        Assert.That(hasCompleted, Is.True);
        Assert.That(module.Snapshot.Balances["gold"].Amount, Is.EqualTo(150));
        module.Shutdown();
    }
    [Test]
    public void Convert_UnknownOutcome_RequiresRefreshInsteadOfAutomaticRetry()
    {
        var backend = new BackendStub { ShouldLoseResponse = true };
        var module = Ready(backend);
        WalletError failure = null;
        Drain(module.Convert("gears-to-gold-v1", 2, () => Assert.Fail("Unconfirmed conversion became successful"), error => failure = error));
        Assert.That(failure.Code, Is.EqualTo(WalletErrorCode.UnknownOutcome));
        Assert.That(module.HasSnapshot, Is.False);
        Assert.That(backend.ConvertCount, Is.EqualTo(1));
        Drain(module.Convert("gears-to-gold-v1", 2, () => Assert.Fail("Another conversion dispatched"), error => failure = error));
        Assert.That(failure.Code, Is.EqualTo(WalletErrorCode.RefreshRequired));
        Assert.That(backend.ConvertCount, Is.EqualTo(1));
        backend.ShouldLoseResponse = false;
        backend.State = State(150, 3);
        Drain(module.Refresh(() => { }, error => Assert.Fail(error.Message)));
        Assert.That(module.Snapshot.Balances["gold"].Amount, Is.EqualTo(150));
        module.Shutdown();
    }
    [Test]
    public void Convert_BusinessRejection_DoesNotPublishAnOptimisticDebit()
    {
        var backend = new BackendStub { ShouldReject = true };
        var module = Ready(backend);
        WalletError failure = null;
        Drain(module.Convert("gears-to-gold-v1", 2, () => Assert.Fail("Rejected operation applied"), error => failure = error));
        Assert.That(failure.Code, Is.EqualTo(WalletErrorCode.InsufficientFunds));
        Assert.That(module.Snapshot.Balances["gold"].Amount, Is.EqualTo(100));
        module.Shutdown();
    }
    [Test]
    public void Shutdown_PendingRefresh_CompletesCancellationOnceAndClearsPrivateState()
    {
        var backend = new BackendStub();
        var module = Ready(backend);
        backend.ShouldWait = true;
        int failures = 0;
        var operation = module.Refresh(() => Assert.Fail("Stopped module published"), error => { failures++; Assert.That(error.Code, Is.EqualTo(WalletErrorCode.Cancelled)); });
        Assert.That(operation.MoveNext(), Is.True);
        module.Shutdown();
        module.Shutdown();
        Assert.That(failures, Is.EqualTo(1));
        Assert.That(module.HasSnapshot, Is.False);
        Assert.That(module.IsBusy, Is.False);
        Assert.That(backend.HasDisposed, Is.True);
        (operation as IDisposable)?.Dispose();
    }
    [Test]
    public void Refresh_ReservedOperation_BlocksConcurrentRefresh()
    {
        var backend = new BackendStub();
        var module = Ready(backend);
        var pending = module.Refresh(() => { }, error => { });
        WalletError failure = null;
        Drain(module.Refresh(() => Assert.Fail("Overlapping fetch"), error => failure = error));
        Assert.That(failure.Code, Is.EqualTo(WalletErrorCode.Busy));
        Drain(pending);
        module.Shutdown();
    }
    [Test]
    public void SnapshotSubscriberThrows_OtherSubscribersAndCompletionStillRun()
    {
        var backend = new BackendStub();
        int diagnostics = 0;
        var module = new WalletModule(backend, exception => diagnostics++);
        Drain(module.Initialize());
        bool hasNotified = false;
        bool hasCompleted = false;
        Action throwing = () => throw new InvalidOperationException("Subscriber failure");
        Action later = () => hasNotified = true;
        module.SnapshotChanged += throwing;
        module.SnapshotChanged += later;
        try
        {
            backend.State = State(200);
            Drain(module.Refresh(() => hasCompleted = true, error => Assert.Fail(error.Message)));
            Assert.That(diagnostics, Is.EqualTo(1));
            Assert.That(hasNotified && hasCompleted, Is.True);
        }
        finally { module.SnapshotChanged -= throwing; module.SnapshotChanged -= later; module.Shutdown(); }
    }
    [Test]
    public void ModelGuards_RejectInvalidUnitsDuplicateCurrenciesAndInvalidAmounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WalletBalance("gold", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WalletBalance("gold", (long)int.MaxValue + 1));
        Assert.Throws<ArgumentException>(() => new WalletSnapshot(new[] { new WalletBalance("gold", 1), new WalletBalance("gold", 2) }, Array.Empty<WalletConversionOffer>()));
        Assert.Throws<ArgumentException>(() => new WalletConversionOffer("offer", "gold", "gold", 1, 1));
    }
    [Test]
    public void Refresh_ShutdownFromNotification_CancelsWithoutSuccess()
    {
        var module = Ready(new BackendStub());
        int successes = 0;
        int failures = 0;
        Action stop = () => module.Shutdown();
        module.SnapshotChanged += stop;
        try
        {
            Drain(module.Refresh(() => successes++, error => failures++));
            Assert.That(successes, Is.Zero);
            Assert.That(failures, Is.EqualTo(1));
            Assert.That(module.HasSnapshot, Is.False);
        }
        finally { module.SnapshotChanged -= stop; module.Shutdown(); }
    }
    [Test]
    public void Convert_DisposedInFlight_RequiresRefreshBeforeAnotherConversion()
    {
        var backend = new BackendStub();
        var module = Ready(backend);
        backend.ShouldWait = true;
        WalletError failure = null;
        var operation = module.Convert("gears-to-gold-v1", 1, () => Assert.Fail("Pending conversion completed"), error => failure = error);
        Assert.That(operation.MoveNext(), Is.True);
        ((IDisposable)operation).Dispose();
        Assert.That(failure.Code, Is.EqualTo(WalletErrorCode.Cancelled));
        Assert.That(module.HasSnapshot, Is.False);
        Assert.That(module.IsBusy, Is.False);
        module.Shutdown();
    }
    public static void Drain(IEnumerator operation)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(operation);
        while (stack.Count > 0)
        {
            var current = stack.Peek();
            if (!current.MoveNext()) { stack.Pop(); (current as IDisposable)?.Dispose(); }
            else if (current.Current is IEnumerator nested) stack.Push(nested);
        }
    }
    private sealed class BackendStub : IWalletBackend
    {
        public WalletSnapshot State = WalletModuleTests.State();
        public int ConvertCount;
        public bool ShouldLoseResponse;
        public bool ShouldReject;
        public int FetchCount;
        public bool ShouldWait;
        public bool HasDisposed;
        public IEnumerator Fetch(Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure)
        { FetchCount++; try { while (ShouldWait) yield return null; onSuccess(State); } finally { HasDisposed = true; } }
        public IEnumerator Convert(string offerId, long amount, Action<WalletSnapshot> onSuccess, Action<WalletError> onFailure)
        {
            ConvertCount++;
            while (ShouldWait) yield return null;
            if (ShouldLoseResponse) onFailure(new WalletError(WalletErrorCode.UnknownOutcome, "Response lost."));
            else if (ShouldReject) onFailure(new WalletError(WalletErrorCode.InsufficientFunds, "Not enough currency."));
            else onSuccess(WalletModuleTests.State(150, 3));
            yield break;
        }
    }
}
