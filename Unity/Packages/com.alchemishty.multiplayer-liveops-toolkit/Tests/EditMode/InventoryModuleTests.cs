using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Inventory;
using NUnit.Framework;

public class InventoryModuleTests
{
    [Test]
    public void Operation_DisposedBeforeStarting_ReleasesReservationWithoutDispatch()
    {
        var backend = new BackendStub();
        var module = Ready(backend);
        var confirmedSnapshot = module.Snapshot;
        int failures = 0;
        Action<InventoryError> onFailure = error => { failures++; Assert.That(error.Code, Is.EqualTo(InventoryErrorCode.Cancelled)); };
        try
        {
            var operation = module.Refresh(() => Assert.Fail("Cancelled refresh succeeded"), onFailure);
            ((IDisposable)operation).Dispose();
            ((IDisposable)operation).Dispose();
            Assert.That(module.IsBusy, Is.False);
            Assert.That(failures, Is.EqualTo(1));
            Assert.That(module.Snapshot, Is.SameAs(confirmedSnapshot));
            Assert.That(operation.MoveNext(), Is.False);
            Assert.That(backend.FetchCount, Is.EqualTo(1));
            bool hasRefreshed = false;
            Drain(module.Refresh(() => hasRefreshed = true, error => Assert.Fail(error.Message)));
            Assert.That(hasRefreshed, Is.True);
            Assert.That(backend.FetchCount, Is.EqualTo(2));
            Assert.That(failures, Is.EqualTo(1));
        }
        finally { module.Shutdown(); }
    }
    private static InventorySnapshot State(string itemId = "hat") => new InventorySnapshot(new[] { new CosmeticItem(itemId, "Hat", "headwear") }, new[] { new CosmeticSet("outfit", "Outfit", new[] { itemId }) });
    private static InventoryModule Ready(BackendStub backend)
    {
        var module = new InventoryModule(backend, exception => Assert.Fail(exception.Message));
        Drain(module.Initialize());
        Assert.That(module.HasSnapshot, Is.True);
        return module;
    }
    [Test]
    public void Initialize_IndependentInventories_DoNotShareOwnership()
    {
        var first = Ready(new BackendStub());
        var second = Ready(new BackendStub { State = State("sword") });
        first.Shutdown();
        Assert.That(first.HasSnapshot, Is.False);
        Assert.That(second.Snapshot.Items.ContainsKey("sword"), Is.True);
        second.Shutdown();
    }
    [Test]
    public void Refresh_ReplacesWholeSnapshotAndPreservesPreviouslyReturnedState()
    {
        var backend = new BackendStub();
        var module = Ready(backend);
        var previous = module.Snapshot;
        backend.State = State("sword");
        int notifications = 0;
        Action changed = () => { notifications++; Assert.That(module.Snapshot.Sets["outfit"].ItemIds[0], Is.EqualTo("sword")); };
        module.SnapshotChanged += changed;
        try
        {
            Drain(module.Refresh(() => { }, error => Assert.Fail(error.Message)));
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(previous.Items.ContainsKey("hat"), Is.True);
            Assert.That(module.Snapshot.Items.ContainsKey("hat"), Is.False);
        }
        finally { module.SnapshotChanged -= changed; module.Shutdown(); }
    }
    [Test]
    public void Refresh_RejectedRead_RetainsConfirmedOwnershipAndReportsFailure()
    {
        var backend = new BackendStub();
        var module = Ready(backend);
        backend.ShouldFail = true;
        InventoryError failure = null;
        Drain(module.Refresh(() => Assert.Fail("Failed read completed"), error => failure = error));
        Assert.That(failure.Code, Is.EqualTo(InventoryErrorCode.Unavailable));
        Assert.That(module.Snapshot.Items.ContainsKey("hat"), Is.True);
        module.Shutdown();
    }
    [Test]
    public void Shutdown_PendingRead_CancelsOnceDisposesBackendAndIgnoresLateCallback()
    {
        var backend = new BackendStub();
        var module = Ready(backend);
        backend.ShouldWait = true;
        int failures = 0;
        var operation = module.Refresh(() => Assert.Fail("Stopped inventory published"), error => failures++);
        Assert.That(operation.MoveNext(), Is.True);
        module.Shutdown();
        module.Shutdown();
        backend.LateSuccess(State("late"));
        Assert.That(failures, Is.EqualTo(1));
        Assert.That(backend.HasDisposed, Is.True);
        Assert.That(module.HasSnapshot, Is.False);
        Assert.That(module.IsBusy, Is.False);
        ((IDisposable)operation).Dispose();
    }
    [Test]
    public void Refresh_ReservedRead_RejectsConcurrentRead()
    {
        var module = Ready(new BackendStub());
        var pending = module.Refresh(() => { }, error => { });
        InventoryError failure = null;
        Drain(module.Refresh(() => Assert.Fail("Overlapping read"), error => failure = error));
        Assert.That(failure.Code, Is.EqualTo(InventoryErrorCode.Busy));
        Drain(pending);
        module.Shutdown();
    }
    [Test]
    public void Refresh_ShutdownFromNotification_DoesNotAlsoCompleteSuccess()
    {
        var module = Ready(new BackendStub());
        int successes = 0;
        int failures = 0;
        Action stop = module.Shutdown;
        module.SnapshotChanged += stop;
        try
        {
            Drain(module.Refresh(() => successes++, error => failures++));
            Assert.That(successes, Is.Zero);
            Assert.That(failures, Is.EqualTo(1));
        }
        finally { module.SnapshotChanged -= stop; module.Shutdown(); }
    }
    [Test]
    public void SnapshotSubscriberThrows_LaterSubscriberAndCompletionStillRun()
    {
        int diagnostics = 0;
        var module = new InventoryModule(new BackendStub(), exception => diagnostics++);
        Drain(module.Initialize());
        bool hasNotified = false;
        bool hasCompleted = false;
        Action throwing = () => throw new InvalidOperationException("View failed");
        Action later = () => hasNotified = true;
        module.SnapshotChanged += throwing;
        module.SnapshotChanged += later;
        try
        {
            Drain(module.Refresh(() => hasCompleted = true, error => Assert.Fail(error.Message)));
            Assert.That(diagnostics, Is.EqualTo(1));
            Assert.That(hasNotified && hasCompleted, Is.True);
        }
        finally { module.SnapshotChanged -= throwing; module.SnapshotChanged -= later; module.Shutdown(); }
    }
    [Test]
    public void Models_CopyCollectionsAndRejectDuplicateOrUnownedSetMembers()
    {
        var members = new List<string> { "hat" };
        var outfit = new CosmeticSet("outfit", "Outfit", members);
        members.Clear();
        Assert.That(outfit.ItemIds, Has.Count.EqualTo(1));
        Assert.Throws<ArgumentException>(() => new CosmeticSet("empty", "Empty", Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => new InventorySnapshot(Array.Empty<CosmeticItem>(), new[] { outfit }));
        Assert.Throws<ArgumentException>(() => new InventorySnapshot(new[] { new CosmeticItem("hat", "Hat", "headwear"), new CosmeticItem("hat", "Hat", "headwear") }, Array.Empty<CosmeticSet>()));
    }
    private static void Drain(IEnumerator operation)
    {
        var pending = new Stack<IEnumerator>(); pending.Push(operation);
        while (pending.Count > 0)
        {
            var current = pending.Peek();
            if (!current.MoveNext()) { pending.Pop(); (current as IDisposable)?.Dispose(); }
            else if (current.Current is IEnumerator nested) pending.Push(nested);
        }
    }
    private sealed class BackendStub : IInventoryBackend
    {
        public InventorySnapshot State = InventoryModuleTests.State();
        public int FetchCount;
        public bool ShouldWait;
        public bool ShouldFail;
        public bool HasDisposed;
        public Action<InventorySnapshot> LateSuccess;
        public IEnumerator Fetch(Action<InventorySnapshot> onSuccess, Action<InventoryError> onFailure)
        {
            FetchCount++;
            LateSuccess = onSuccess;
            try { while (ShouldWait) yield return null; if (ShouldFail) onFailure(new InventoryError(InventoryErrorCode.Unavailable, "Unavailable")); else onSuccess(State); }
            finally { HasDisposed = true; }
        }
    }
}
