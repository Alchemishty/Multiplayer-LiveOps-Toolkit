using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Core;
using MultiplayerLiveOpsToolkit.Wallet;
using MultiplayerLiveOpsToolkit.Inventory;

namespace MultiplayerLiveOpsToolkit.Shop
{
    /// <summary>Backend-authorized purchases and coordinated confirmed account snapshots.</summary>
    public sealed class ShopModule : ILiveOpsModule, IShopService
    {
        private static readonly string[] ModuleDependencies = { "identity", "wallet", "inventory" };
        private readonly IShopBackend backend;
        private readonly IWalletState wallet;
        private readonly IInventoryState inventory;
        private readonly Action<Exception> reportSubscriberException;
        private bool hasStarted;
        private bool isStopped;
        private bool ownsAccountStates;
        private bool isMutation;
        private bool hasDispatched;
        private Stack<IEnumerator> activeOperations;
        private Action<ShopError> pendingFailure;
        private ShopSnapshot snapshot;
        public string Id => "shop";
        public IReadOnlyCollection<string> Dependencies => Array.AsReadOnly(ModuleDependencies);
        public bool HasSnapshot => snapshot != null && !isStopped;
        public bool IsBusy { get; private set; }
        public bool RequiresRefresh { get; private set; }
        public ShopSnapshot Snapshot => HasSnapshot ? snapshot : throw new InvalidOperationException("Shop requires a confirmed catalog.");
        public ShopError LastError { get; private set; }
        public event Action StateChanged;

        public ShopModule(IShopBackend backend, IWalletState wallet, IInventoryState inventory, Action<Exception> reportSubscriberException)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            this.reportSubscriberException = reportSubscriberException ?? throw new ArgumentNullException(nameof(reportSubscriberException));
        }
        public IEnumerator Initialize()
        {
            if (hasStarted || isStopped) throw new InvalidOperationException("ShopModule cannot be initialized twice or reused after shutdown.");
            hasStarted = true;
            return Reserve(null, state => { }, error => throw new InvalidOperationException("Shop initialization failed: " + error.Message));
        }
        public IEnumerator Refresh(Action onSuccess, Action<ShopError> onFailure)
        {
            if (onSuccess == null) throw new ArgumentNullException(nameof(onSuccess));
            if (onFailure == null) throw new ArgumentNullException(nameof(onFailure));
            EnsureStarted();
            return Reserve(null, state => onSuccess(), onFailure);
        }
        public IEnumerator Purchase(string offerId, Action<ShopState> onSuccess, Action<ShopError> onFailure)
        {
            if (onSuccess == null) throw new ArgumentNullException(nameof(onSuccess));
            if (onFailure == null) throw new ArgumentNullException(nameof(onFailure));
            EnsureStarted();
            ShopOffer.ValidateId(offerId);
            if (RequiresRefresh || !wallet.HasSnapshot || !inventory.HasSnapshot) return Fail(onFailure, ShopErrorCode.RefreshRequired, "Refresh Shop before another purchase.");
            if (!HasSnapshot || !snapshot.Offers.TryGetValue(offerId, out var offer)) return Fail(onFailure, ShopErrorCode.Rejected, "Offer is unavailable.");
            bool isOwned = true;
            foreach (var itemId in offer.ItemIds) isOwned &= inventory.Snapshot.Items.ContainsKey(itemId);
            if (isOwned) return Fail(onFailure, ShopErrorCode.AlreadyOwned, "Offer is already owned.");
            return Reserve(offerId, onSuccess, onFailure);
        }
        private IEnumerator Reserve(string offerId, Action<ShopState> onSuccess, Action<ShopError> onFailure)
        {
            if (IsBusy || wallet.IsBusy || inventory.IsBusy) return Fail(onFailure, ShopErrorCode.Busy, "An account operation is already in progress.");
            wallet.BeginUpdate();
            try { inventory.BeginUpdate(); }
            catch { wallet.EndUpdate(); throw; }
            ownsAccountStates = true;
            IsBusy = true;
            isMutation = offerId != null;
            hasDispatched = false;
            pendingFailure = onFailure;
            LastError = null;
            var operations = new Stack<IEnumerator>();
            operations.Push(FetchState(offerId, onSuccess));
            activeOperations = operations;
            NotifyChanged();
            return new OwnedOperation(this, operations, Run(operations));
        }
        private IEnumerator FetchState(string offerId, Action<ShopState> onSuccess)
        {
            ShopState result = null;
            ShopError failure = null;
            int completions = 0;
            bool isAccepting = true;
            hasDispatched = true;
            Action<ShopState> accept = value => { if (isAccepting && !isStopped) { completions++; result = value; } };
            Action<ShopError> reject = error => { if (isAccepting && !isStopped) { completions++; failure = error; } };
            try
            {
                yield return (offerId == null ? backend.Fetch(accept, reject) : backend.Purchase(offerId, accept, reject))
                    ?? throw new InvalidOperationException("Shop backend returned no operation.");
            }
            finally { isAccepting = false; }
            if (isStopped) yield break;
            if (completions != 1 || (result == null && failure == null)) failure = new ShopError(isMutation ? ShopErrorCode.UnknownOutcome : ShopErrorCode.InvalidResponse, "Shop response could not be confirmed.");
            if (failure == null && isMutation)
            {
                if (result.PurchasedOfferId != offerId) failure = new ShopError(ShopErrorCode.UnknownOutcome, "Purchase response did not match the requested offer.");
                else foreach (var itemId in snapshot.Offers[offerId].ItemIds)
                    if (!result.Inventory.Items.ContainsKey(itemId)) failure = new ShopError(ShopErrorCode.UnknownOutcome, "Purchase ownership could not be confirmed.");
            }
            if (failure != null)
            {
                if (isMutation && (failure.Code == ShopErrorCode.Unavailable || failure.Code == ShopErrorCode.InvalidResponse || failure.Code == ShopErrorCode.UnknownOutcome || failure.Code == ShopErrorCode.Cancelled))
                { InvalidateAccount(); failure = new ShopError(ShopErrorCode.UnknownOutcome, "Purchase outcome is unknown. Refresh Shop before trying again."); }
                CompleteFailure(failure);
                yield break;
            }
            wallet.StageSnapshot(result.Wallet);
            inventory.StageSnapshot(result.Inventory);
            snapshot = result.Catalog;
            RequiresRefresh = false;
            ReleaseAccountStates();
            if (isStopped) yield break;
            NotifyChanged();
            if (isStopped) yield break;
            pendingFailure = null;
            IsBusy = false;
            NotifyChanged();
            onSuccess(result); // Completion was committed before the final idle notification.
        }
        private IEnumerator Run(Stack<IEnumerator> operations)
        {
            try
            {
                while (activeOperations == operations && operations.Count > 0 && !isStopped)
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
                        if (pendingFailure == null) throw failure;
                        if (isMutation) InvalidateAccount();
                        CompleteFailure(new ShopError(isMutation ? ShopErrorCode.UnknownOutcome : ShopErrorCode.Unavailable, "Shop operation could not be confirmed."));
                        yield break;
                    }
                    if (!hasNext) { operations.Pop(); (operation as IDisposable)?.Dispose(); }
                    else if (instruction is IEnumerator nested) operations.Push(nested);
                    else yield return instruction;
                }
            }
            finally { Cancel(operations); }
        }
        private void CompleteFailure(ShopError error)
        {
            LastError = error;
            ReleaseAccountStates();
            if (isStopped) return;
            var callback = pendingFailure;
            pendingFailure = null;
            IsBusy = false;
            NotifyChanged();
            callback(error);
        }
        private void InvalidateAccount()
        {
            RequiresRefresh = true;
            if (ownsAccountStates) { wallet.StageSnapshot(null); inventory.StageSnapshot(null); }
        }
        private void ReleaseAccountStates()
        {
            if (!ownsAccountStates) return;
            ownsAccountStates = false;
            try { wallet.EndUpdate(); }
            finally { inventory.EndUpdate(); }
        }
        private void Cancel(Stack<IEnumerator> operations)
        {
            if (activeOperations != operations) return;
            activeOperations = null;
            if (pendingFailure != null && isMutation && hasDispatched) InvalidateAccount();
            var cancelled = pendingFailure;
            pendingFailure = null;
            IsBusy = false;
            try { DisposeOperations(operations); }
            finally
            {
                try { ReleaseAccountStates(); }
                finally { try { cancelled?.Invoke(new ShopError(ShopErrorCode.Cancelled, "Shop operation was cancelled.")); } finally { NotifyChanged(); } }
            }
        }
        public void Shutdown()
        {
            if (isStopped) return;
            isStopped = true;
            snapshot = null;
            LastError = null;
            if (activeOperations != null) Cancel(activeOperations);
            else { IsBusy = false; NotifyChanged(); }
        }
        private void NotifyChanged()
        {
            var subscribers = StateChanged;
            if (subscribers == null) return;
            foreach (Action subscriber in subscribers.GetInvocationList())
            { try { subscriber(); } catch (Exception exception) { reportSubscriberException(exception); } }
        }
        private static void DisposeOperations(Stack<IEnumerator> operations)
        {
            var failures = new List<Exception>();
            while (operations.Count > 0) { try { (operations.Pop() as IDisposable)?.Dispose(); } catch (Exception exception) { failures.Add(exception); } }
            if (failures.Count > 0) throw new AggregateException("Shop cleanup failed.", failures);
        }
        private static IEnumerator Fail(Action<ShopError> onFailure, ShopErrorCode code, string message) { onFailure(new ShopError(code, message)); yield break; }
        private void EnsureStarted() { if (!hasStarted || isStopped) throw new InvalidOperationException("ShopModule requires an active initialized lifetime."); }
        private sealed class OwnedOperation : IEnumerator, IDisposable
        {
            private readonly ShopModule owner;
            private readonly Stack<IEnumerator> operations;
            private readonly IEnumerator routine;
            private bool hasStarted;
            private bool isDisposed;
            public OwnedOperation(ShopModule owner, Stack<IEnumerator> operations, IEnumerator routine) { this.owner = owner; this.operations = operations; this.routine = routine; }
            public object Current => routine.Current;
            public bool MoveNext() { if (isDisposed) return false; hasStarted = true; return routine.MoveNext(); }
            public void Reset() { throw new NotSupportedException(); }
            public void Dispose() { if (isDisposed) return; isDisposed = true; if (hasStarted) (routine as IDisposable)?.Dispose(); else owner.Cancel(operations); }
        }
    }
}
