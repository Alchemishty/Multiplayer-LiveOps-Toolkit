using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Core;

namespace MultiplayerLiveOpsToolkit.Inventory
{
    /// <summary>Account-scoped cosmetic ownership; grants and equipment belong to other services.</summary>
    public sealed class InventoryModule : ILiveOpsModule, IInventoryState
    {
        private static readonly string[] ModuleDependencies = { "identity" };
        private readonly IInventoryBackend backend;
        private readonly Action<Exception> reportSubscriberException;
        private InventorySnapshot snapshot;
        private bool hasStarted;
        private bool isStopped;
        private bool hasExternalUpdate;
        private Stack<IEnumerator> activeOperations;
        private Action<InventoryError> pendingFailure;
        public string Id => "inventory";
        public IReadOnlyCollection<string> Dependencies => Array.AsReadOnly(ModuleDependencies);
        public bool HasSnapshot => snapshot != null && !isStopped;
        public bool IsBusy { get; private set; }
        public InventorySnapshot Snapshot => HasSnapshot ? snapshot : throw new InvalidOperationException("Inventory has no confirmed snapshot. Initialize or refresh it first.");
        public event Action SnapshotChanged;

        public InventoryModule(IInventoryBackend backend, Action<Exception> reportSubscriberException)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.reportSubscriberException = reportSubscriberException ?? throw new ArgumentNullException(nameof(reportSubscriberException));
        }
        public IEnumerator Initialize()
        {
            if (hasStarted || isStopped) throw new InvalidOperationException("InventoryModule cannot be reused after initialization/shutdown.");
            hasStarted = true;
            Action<InventoryError> failInitialization = error => throw new InvalidOperationException("Inventory initialization failed: " + error.Message);
            return Reserve(FetchState(() => { }), failInitialization);
        }
        public IEnumerator Refresh(Action onSuccess, Action<InventoryError> onFailure)
        {
            ValidateCallbacks(onSuccess, onFailure);
            EnsureStarted();
            return Reserve(FetchState(onSuccess), onFailure);
        }
        public void BeginUpdate()
        {
            EnsureStarted();
            if (IsBusy) throw new InvalidOperationException("Account state already has an active operation.");
            hasExternalUpdate = true;
            IsBusy = true;
        }
        public void StageSnapshot(InventorySnapshot confirmedSnapshot)
        {
            EnsureStarted();
            if (!hasExternalUpdate) throw new InvalidOperationException("Reserve account state before staging a confirmed snapshot.");
            snapshot = confirmedSnapshot; // Null explicitly invalidates an uncertain mutation outcome.
        }
        public void EndUpdate()
        {
            if (isStopped) return; // Context teardown may have already cleared this reservation.
            if (!hasExternalUpdate) throw new InvalidOperationException("Account state has no external update to complete.");
            hasExternalUpdate = false;
            IsBusy = false;
            NotifySnapshotChanged();
        }
        public void Shutdown()
        {
            if (isStopped) return;
            isStopped = true;
            hasExternalUpdate = false;
            snapshot = null;
            IsBusy = false;
            var operations = activeOperations;
            activeOperations = null;
            var cancelled = pendingFailure;
            pendingFailure = null;
            try { DisposeOperations(operations); }
            finally
            {
                try { cancelled?.Invoke(new InventoryError(InventoryErrorCode.Cancelled, "Inventory operation cancelled by shutdown.")); }
                finally { NotifySnapshotChanged(); }
            }
        }
        private IEnumerator Reserve(IEnumerator operation, Action<InventoryError> onFailure)
        {
            if (IsBusy) return FailWithoutDispatch(onFailure, InventoryErrorCode.Busy, "An inventory operation is already in progress.");
            IsBusy = true;
            pendingFailure = onFailure;
            activeOperations = new Stack<IEnumerator>();
            activeOperations.Push(operation);
            return new OwnedOperation(this, activeOperations, Run(activeOperations));
        }
        private IEnumerator FetchState(Action onSuccess)
        {
            InventorySnapshot result = null;
            InventoryError failure = null;
            int completions = 0;
            bool isAccepting = true;
            try
            {
                yield return backend.Fetch(value => { if (isAccepting && !isStopped) { completions++; result = value; } },
                    error => { if (isAccepting && !isStopped) { completions++; failure = error; } })
                    ?? throw new InvalidOperationException("Inventory backend returned no fetch operation.");
            }
            finally { isAccepting = false; }
            if (isStopped) yield break;
            if (completions != 1 || (result == null && failure == null)) failure = new InventoryError(InventoryErrorCode.InvalidResponse, "Inventory backend must complete once with a snapshot or failure.");
            if (failure != null) { CompleteFailure(failure); yield break; }
            snapshot = result;
            NotifySnapshotChanged();
            if (!isStopped) CompleteSuccess(onSuccess);
        }
        private IEnumerator Run(Stack<IEnumerator> operations)
        {
            try
            {
                while (operations.Count > 0 && activeOperations == operations && !isStopped)
                {
                    var operation = operations.Peek();
                    bool hasNext = false;
                    object instruction = null;
                    Exception failure = null;
                    try { hasNext = operation.MoveNext(); if (hasNext) instruction = operation.Current; }
                    catch (Exception exception) { failure = exception; }
                    if (activeOperations != operations) yield break;
                    if (failure != null)
                    {
                        if (pendingFailure == null) throw failure; // Consumer callbacks are not backend failures.
                        CompleteFailure(new InventoryError(InventoryErrorCode.Unavailable, "Inventory backend operation failed."));
                        yield break;
                    }
                    if (!hasNext) { operations.Pop(); (operation as IDisposable)?.Dispose(); }
                    else if (instruction is IEnumerator nested) operations.Push(nested);
                    else yield return instruction;
                }
            }
            finally { Cancel(operations); }
        }
        private void Cancel(Stack<IEnumerator> operations)
        {
            var cancelled = activeOperations == operations ? pendingFailure : null;
            if (activeOperations == operations) { activeOperations = null; pendingFailure = null; IsBusy = false; }
            try { DisposeOperations(operations); }
            finally
            {
                cancelled?.Invoke(new InventoryError(InventoryErrorCode.Cancelled, "Inventory operation was disposed."));
            }
        }
        private void CompleteSuccess(Action onSuccess) { pendingFailure = null; IsBusy = false; onSuccess(); }
        private void CompleteFailure(InventoryError error) { var callback = pendingFailure; pendingFailure = null; IsBusy = false; callback(error); }
        private void NotifySnapshotChanged()
        {
            var subscribers = SnapshotChanged;
            if (subscribers == null) return;
            foreach (Action subscriber in subscribers.GetInvocationList())
            {
                try { subscriber(); }
                catch (Exception exception) { reportSubscriberException(exception); }
            }
        }
        private static void DisposeOperations(Stack<IEnumerator> operations)
        {
            if (operations == null) return;
            var failures = new List<Exception>();
            while (operations.Count > 0)
            {
                try { (operations.Pop() as IDisposable)?.Dispose(); }
                catch (Exception exception) { failures.Add(exception); }
            }
            if (failures.Count > 0) throw new AggregateException("Inventory operation cleanup failed.", failures);
        }
        private sealed class OwnedOperation : IEnumerator, IDisposable
        {
            private readonly InventoryModule owner;
            private readonly Stack<IEnumerator> operations;
            private readonly IEnumerator routine;
            private bool hasStarted;
            private bool isDisposed;
            public OwnedOperation(InventoryModule owner, Stack<IEnumerator> operations, IEnumerator routine)
            { this.owner = owner; this.operations = operations; this.routine = routine; }
            public object Current => routine.Current;
            public bool MoveNext() { if (isDisposed) return false; hasStarted = true; return routine.MoveNext(); }
            public void Reset() { throw new NotSupportedException(); }
            public void Dispose()
            {
                if (isDisposed) return;
                isDisposed = true;
                if (hasStarted) ((IDisposable)routine).Dispose();
                else owner.Cancel(operations);
            }
        }
        private static IEnumerator FailWithoutDispatch(Action<InventoryError> onFailure, InventoryErrorCode code, string message)
        { onFailure(new InventoryError(code, message)); yield break; }
        private void EnsureStarted() { if (!hasStarted || isStopped) throw new InvalidOperationException("InventoryModule requires an active initialized lifetime."); }
        private static void ValidateCallbacks(Action onSuccess, Action<InventoryError> onFailure)
        { if (onSuccess == null) throw new ArgumentNullException(nameof(onSuccess)); if (onFailure == null) throw new ArgumentNullException(nameof(onFailure)); }
    }
}
