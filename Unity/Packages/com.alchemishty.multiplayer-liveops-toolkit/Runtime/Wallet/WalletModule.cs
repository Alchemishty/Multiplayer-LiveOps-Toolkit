using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Core;

namespace MultiplayerLiveOpsToolkit.Wallet
{
    /// <summary>Account-scoped wallet state; currency authority belongs to the supplied backend.</summary>
    public sealed class WalletModule : ILiveOpsModule, IWalletState
    {
        private static readonly string[] ModuleDependencies = { "identity" };
        private readonly IWalletBackend backend;
        private readonly Action<Exception> reportSubscriberException;
        private WalletSnapshot snapshot;
        private bool hasStarted;
        private bool isStopped;
        private bool hasExternalUpdate;
        private Stack<IEnumerator> activeOperations;
        private Action<WalletError> pendingFailure;
        public string Id => "wallet";
        public IReadOnlyCollection<string> Dependencies => Array.AsReadOnly(ModuleDependencies);
        public bool HasSnapshot => snapshot != null && !isStopped;
        public bool IsBusy { get; private set; }
        public WalletSnapshot Snapshot => HasSnapshot ? snapshot : throw new InvalidOperationException("Wallet has no confirmed snapshot. Initialize or refresh it first.");
        public event Action SnapshotChanged;

        public WalletModule(IWalletBackend backend, Action<Exception> reportSubscriberException)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.reportSubscriberException = reportSubscriberException ?? throw new ArgumentNullException(nameof(reportSubscriberException));
        }
        public IEnumerator Initialize()
        {
            if (hasStarted || isStopped) throw new InvalidOperationException("WalletModule cannot be reused after initialization/shutdown.");
            hasStarted = true;
            Action<WalletError> failInitialization = error => throw new InvalidOperationException("Wallet initialization failed: " + error.Message);
            return Reserve(FetchState(() => { }), failInitialization, false);
        }
        public IEnumerator Refresh(Action onSuccess, Action<WalletError> onFailure)
        {
            ValidateCallbacks(onSuccess, onFailure);
            EnsureStarted();
            return Reserve(FetchState(onSuccess), onFailure, false);
        }
        public IEnumerator Convert(string offerId, long amount, Action onSuccess, Action<WalletError> onFailure)
        {
            ValidateCallbacks(onSuccess, onFailure);
            EnsureStarted();
            WalletBalance.ValidateId(offerId, nameof(offerId));
            if (amount < 1 || amount > WalletBalance.MAX_AMOUNT) throw new ArgumentOutOfRangeException(nameof(amount));
            if (!HasSnapshot) return FailWithoutDispatch(onFailure, WalletErrorCode.RefreshRequired, "Refresh wallet balances before converting.");
            if (!Snapshot.Offers.TryGetValue(offerId, out var offer)) return FailWithoutDispatch(onFailure, WalletErrorCode.Rejected, "Conversion offer is unavailable.");
            if (amount % offer.SourceUnits != 0) return FailWithoutDispatch(onFailure, WalletErrorCode.InvalidUnits, "Amount must use complete conversion units.");
            return Reserve(ConvertState(offerId, amount, onSuccess), onFailure, true);
        }
        public void BeginUpdate()
        {
            EnsureStarted();
            if (IsBusy) throw new InvalidOperationException("Account state already has an active operation.");
            hasExternalUpdate = true;
            IsBusy = true;
        }
        public void StageSnapshot(WalletSnapshot confirmedSnapshot)
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
                try { cancelled?.Invoke(new WalletError(WalletErrorCode.Cancelled, "Wallet operation cancelled by shutdown.")); }
                finally { NotifySnapshotChanged(); }
            }
        }
        private IEnumerator Reserve(IEnumerator operation, Action<WalletError> onFailure, bool isMutation)
        {
            if (IsBusy) return FailWithoutDispatch(onFailure, WalletErrorCode.Busy, "A wallet operation is already in progress.");
            IsBusy = true;
            pendingFailure = onFailure;
            activeOperations = new Stack<IEnumerator>();
            activeOperations.Push(operation);
            return new OwnedOperation(this, activeOperations, Run(activeOperations, isMutation));
        }
        private IEnumerator FetchState(Action onSuccess)
        {
            WalletSnapshot result = null;
            WalletError failure = null;
            int completions = 0;
            bool isAccepting = true;
            try
            {
                yield return backend.Fetch(value => { if (isAccepting && !isStopped) { completions++; result = value; } },
                    error => { if (isAccepting && !isStopped) { completions++; failure = error; } })
                    ?? throw new InvalidOperationException("Wallet backend returned no fetch operation.");
            }
            finally { isAccepting = false; }
            if (isStopped) yield break;
            if (completions != 1 || (result == null && failure == null)) failure = new WalletError(WalletErrorCode.InvalidResponse, "Wallet backend must complete once with a snapshot or failure.");
            if (failure != null) { CompleteFailure(failure); yield break; }
            snapshot = result;
            NotifySnapshotChanged();
            if (!isStopped) CompleteSuccess(onSuccess);
        }
        private IEnumerator ConvertState(string offerId, long amount, Action onSuccess)
        {
            WalletSnapshot result = null;
            WalletError failure = null;
            int completions = 0;
            bool isAccepting = true;
            try
            {
                yield return backend.Convert(offerId, amount,
                    value => { if (isAccepting && !isStopped) { completions++; result = value; } },
                    error => { if (isAccepting && !isStopped) { completions++; failure = error; } })
                    ?? throw new InvalidOperationException("Wallet backend returned no conversion operation.");
            }
            finally { isAccepting = false; }
            if (isStopped) yield break;
            if (completions != 1 || (result == null && failure == null)) failure = new WalletError(WalletErrorCode.UnknownOutcome, "Conversion could not be confirmed. Refresh balances.");
            if (failure != null)
            {
                if (failure.Code == WalletErrorCode.UnknownOutcome || failure.Code == WalletErrorCode.Unavailable || failure.Code == WalletErrorCode.InvalidResponse)
                {
                    snapshot = null;
                    NotifySnapshotChanged();
                    failure = new WalletError(WalletErrorCode.UnknownOutcome, "Conversion could not be confirmed. Refresh before trying again.");
                }
                CompleteFailure(failure);
                yield break;
            }
            snapshot = result;
            NotifySnapshotChanged();
            if (!isStopped) CompleteSuccess(onSuccess);
        }
        private IEnumerator Run(Stack<IEnumerator> operations, bool isMutation)
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
                        if (isMutation) { snapshot = null; NotifySnapshotChanged(); }
                        CompleteFailure(new WalletError(isMutation ? WalletErrorCode.UnknownOutcome : WalletErrorCode.Unavailable,
                            isMutation ? "Conversion could not be confirmed. Refresh balances." : "Wallet backend operation failed."));
                        yield break;
                    }
                    if (!hasNext) { operations.Pop(); (operation as IDisposable)?.Dispose(); }
                    else if (instruction is IEnumerator nested) operations.Push(nested);
                    else yield return instruction;
                }
            }
            finally { Cancel(operations, isMutation); }
        }
        private void Cancel(Stack<IEnumerator> operations, bool isMutation)
        {
            var cancelled = activeOperations == operations ? pendingFailure : null;
            if (activeOperations == operations) { activeOperations = null; pendingFailure = null; IsBusy = false; }
            try { DisposeOperations(operations); }
            finally
            {
                if (cancelled != null && isMutation) { snapshot = null; NotifySnapshotChanged(); }
                cancelled?.Invoke(new WalletError(WalletErrorCode.Cancelled, "Wallet operation was disposed."));
            }
        }
        private void CompleteSuccess(Action onSuccess) { pendingFailure = null; IsBusy = false; onSuccess(); }
        private void CompleteFailure(WalletError error) { var callback = pendingFailure; pendingFailure = null; IsBusy = false; callback(error); }
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
            if (failures.Count > 0) throw new AggregateException("Wallet operation cleanup failed.", failures);
        }
        private sealed class OwnedOperation : IEnumerator, IDisposable
        {
            private readonly WalletModule owner;
            private readonly Stack<IEnumerator> operations;
            private readonly IEnumerator routine;
            private bool hasStarted;
            private bool isDisposed;
            public OwnedOperation(WalletModule owner, Stack<IEnumerator> operations, IEnumerator routine)
            { this.owner = owner; this.operations = operations; this.routine = routine; }
            public object Current => routine.Current;
            public bool MoveNext() { if (isDisposed) return false; hasStarted = true; return routine.MoveNext(); }
            public void Reset() { throw new NotSupportedException(); }
            public void Dispose()
            {
                if (isDisposed) return;
                isDisposed = true;
                if (hasStarted) ((IDisposable)routine).Dispose();
                else owner.Cancel(operations, false);
            }
        }
        private static IEnumerator FailWithoutDispatch(Action<WalletError> onFailure, WalletErrorCode code, string message)
        { onFailure(new WalletError(code, message)); yield break; }
        private void EnsureStarted() { if (!hasStarted || isStopped) throw new InvalidOperationException("WalletModule requires an active initialized lifetime."); }
        private static void ValidateCallbacks(Action onSuccess, Action<WalletError> onFailure)
        { if (onSuccess == null) throw new ArgumentNullException(nameof(onSuccess)); if (onFailure == null) throw new ArgumentNullException(nameof(onFailure)); }
    }
}
