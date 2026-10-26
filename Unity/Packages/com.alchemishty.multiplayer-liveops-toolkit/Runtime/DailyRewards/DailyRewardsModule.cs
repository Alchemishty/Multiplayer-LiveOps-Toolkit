using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Core;
using MultiplayerLiveOpsToolkit.Wallet;

namespace MultiplayerLiveOpsToolkit.DailyRewards
{
    /// <summary>Backend-confirmed monthly attendance and atomic currency claims.</summary>
    public sealed class DailyRewardsModule : ILiveOpsModule, IDailyRewardService
    {
        private readonly IReadOnlyCollection<string> moduleDependencies;
        private bool needsWalletState;
        private readonly IDailyRewardBackend backend;
        private readonly IWalletState wallet;
        private readonly Action<Exception> reportSubscriberException;
        private bool hasStarted;
        private bool isStopped;
        private bool ownsAccountStates;
        private bool isMutation;
        private bool hasDispatched;
        private Stack<IEnumerator> activeOperations;
        private Action<DailyRewardError> pendingFailure;
        private DailyRewardSnapshot snapshot;
        public string Id => "daily-rewards";
        public IReadOnlyCollection<string> Dependencies => moduleDependencies;
        public bool HasSnapshot => snapshot != null && !isStopped;
        private bool hasActiveOperation;
        public bool IsBusy => !isStopped && (hasActiveOperation || (wallet != null && wallet.IsBusy));
        public bool RequiresRefresh { get; private set; }
        public DailyRewardSnapshot Snapshot => HasSnapshot ? snapshot : throw new InvalidOperationException("Daily rewards requires a confirmed snapshot.");
        public event Action StateChanged;

        public DailyRewardsModule(IDailyRewardBackend backend, Action<Exception> reportSubscriberException, IWalletState wallet = null)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.wallet = wallet; // Explicit optional capability: configured currency schedules require it.
            var dependencies = new List<string> { "identity" };
            if (wallet != null) dependencies.Add("wallet");
            moduleDependencies = dependencies.AsReadOnly();
            this.reportSubscriberException = reportSubscriberException ?? throw new ArgumentNullException(nameof(reportSubscriberException));
        }
        public IEnumerator Initialize()
        {
            if (hasStarted || isStopped) throw new InvalidOperationException("DailyRewardsModule cannot be initialized twice or reused after shutdown.");
            hasStarted = true;
            return Reserve(null, state => { }, error => throw new InvalidOperationException("Daily reward initialization failed: " + error.Message));
        }
        public IEnumerator Refresh(Action onSuccess, Action<DailyRewardError> onFailure)
        {
            if (onSuccess == null) throw new ArgumentNullException(nameof(onSuccess));
            if (onFailure == null) throw new ArgumentNullException(nameof(onFailure));
            EnsureStarted();
            return Reserve(null, state => onSuccess(), onFailure);
        }
        public IEnumerator ClaimToday(Action onSuccess, Action<DailyRewardError> onFailure)
        {
            if (onSuccess == null) throw new ArgumentNullException(nameof(onSuccess));
            if (onFailure == null) throw new ArgumentNullException(nameof(onFailure));
            EnsureStarted();
            if (RequiresRefresh || (needsWalletState && !wallet.HasSnapshot)) return Fail(onFailure, DailyRewardErrorCode.RefreshRequired, "Refresh Daily Rewards before another claim.");
            if (!HasSnapshot) return Fail(onFailure, DailyRewardErrorCode.RefreshRequired, "Daily Rewards requires a confirmed calendar.");
            if (!snapshot.CanClaimToday) return Fail(onFailure, DailyRewardErrorCode.AlreadyClaimed, "Today's attendance is already claimed. Refresh for the backend's current day.");
            return Reserve(snapshot.Today, state => onSuccess(), onFailure);
        }
        private IEnumerator Reserve(DateTime? expectedDate, Action<DailyRewardState> onSuccess, Action<DailyRewardError> onFailure)
        {
            if (IsBusy || (wallet != null && wallet.IsBusy)) return Fail(onFailure, DailyRewardErrorCode.Busy, "An account operation is already in progress.");
            if (wallet != null) wallet.BeginUpdate();
            ownsAccountStates = true;
            hasActiveOperation = true;
            isMutation = expectedDate.HasValue;
            hasDispatched = false;
            pendingFailure = onFailure;
            var operations = new Stack<IEnumerator>();
            operations.Push(FetchState(expectedDate, onSuccess));
            activeOperations = operations;
            NotifyChanged();
            return new OwnedOperation(this, operations, Run(operations));
        }
        private IEnumerator FetchState(DateTime? expectedDate, Action<DailyRewardState> onSuccess)
        {
            DailyRewardState result = null;
            DailyRewardError failure = null;
            int completions = 0;
            bool isAccepting = true;
            hasDispatched = true;
            Action<DailyRewardState> accept = value => { if (isAccepting && !isStopped) { completions++; result = value; } };
            Action<DailyRewardError> reject = error => { if (isAccepting && !isStopped) { completions++; failure = error; } };
            try
            {
                yield return (expectedDate == null ? backend.Fetch(accept, reject) : backend.Claim(expectedDate.Value, accept, reject))
                    ?? throw new InvalidOperationException("Daily reward backend returned no operation.");
            }
            finally { isAccepting = false; }
            if (isStopped) yield break;
            if (completions != 1 || (result == null && failure == null)) failure = new DailyRewardError(isMutation ? DailyRewardErrorCode.UnknownOutcome : DailyRewardErrorCode.InvalidResponse, "Daily reward response could not be confirmed.");
            if (failure == null)
            {
                if (result.Calendar.RequiresWallet && wallet == null)
                    failure = new DailyRewardError(DailyRewardErrorCode.Misconfigured, "Configured daily currency rewards require explicitly selected Wallet.");
                else if (needsWalletState && wallet != null && !wallet.HasSnapshot && result.Wallet == null)
                    failure = new DailyRewardError(DailyRewardErrorCode.Misconfigured, "Daily reward configuration changed before uncertain Wallet state was refreshed.");
                else if (result.ClaimedDate != expectedDate)
                    failure = new DailyRewardError(isMutation ? DailyRewardErrorCode.UnknownOutcome : DailyRewardErrorCode.InvalidResponse, "Daily reward result did not match the requested operation.");
                else if (isMutation)
                {
                    var reward = snapshot.Days[snapshot.Today.Day - 1].Reward;
                    if (reward != null && (result.Wallet == null || !result.Wallet.Balances.ContainsKey(reward.CurrencyId)))
                        failure = new DailyRewardError(DailyRewardErrorCode.UnknownOutcome, "Claim did not confirm the reward currency state.");
                }
            }
            if (failure != null)
            {
                if (isMutation && (failure.Code == DailyRewardErrorCode.Unavailable || failure.Code == DailyRewardErrorCode.InvalidResponse || failure.Code == DailyRewardErrorCode.UnknownOutcome || failure.Code == DailyRewardErrorCode.Cancelled))
                { InvalidateAccount(); failure = new DailyRewardError(DailyRewardErrorCode.UnknownOutcome, "Claim outcome is unknown. Refresh Daily rewards before trying again."); }
                if (failure.Code == DailyRewardErrorCode.DayChanged || failure.Code == DailyRewardErrorCode.AlreadyClaimed) RequiresRefresh = true;
                CompleteFailure(failure);
                yield break;
            }
            if (wallet != null && result.Wallet != null) wallet.StageSnapshot(result.Wallet);
            snapshot = result.Calendar;
            needsWalletState = snapshot.RequiresWallet;
            RequiresRefresh = false;
            pendingFailure = null; hasActiveOperation = false; // Completion is committed before observers can stop/rebind the owner.
            ReleaseAccountStates();
            NotifyChanged();
            onSuccess(result);
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
                        CompleteFailure(new DailyRewardError(isMutation ? DailyRewardErrorCode.UnknownOutcome : DailyRewardErrorCode.Unavailable, "Daily reward operation could not be confirmed."));
                        yield break;
                    }
                    if (!hasNext) { operations.Pop(); (operation as IDisposable)?.Dispose(); }
                    else if (instruction is IEnumerator nested) operations.Push(nested);
                    else yield return instruction;
                }
            }
            finally { Cancel(operations); }
        }
        private void CompleteFailure(DailyRewardError error)
        {
            ReleaseAccountStates();
            if (isStopped) return;
            var callback = pendingFailure;
            pendingFailure = null;
            hasActiveOperation = false;
            NotifyChanged();
            callback(error);
        }
        private void InvalidateAccount()
        {
            RequiresRefresh = true;
            if (ownsAccountStates)
            {
                if (needsWalletState && wallet != null) wallet.StageSnapshot(null);
            }
        }
        private void ReleaseAccountStates()
        {
            if (!ownsAccountStates) return;
            ownsAccountStates = false;
            if (wallet != null) wallet.EndUpdate();
        }
        private void Cancel(Stack<IEnumerator> operations)
        {
            if (activeOperations != operations) { DisposeOperations(operations); return; }
            activeOperations = null;
            if (pendingFailure != null && isMutation && hasDispatched) InvalidateAccount();
            var cancelled = pendingFailure;
            pendingFailure = null;
            hasActiveOperation = false;
            try { DisposeOperations(operations); }
            finally
            {
                try { ReleaseAccountStates(); }
                finally { try { cancelled?.Invoke(new DailyRewardError(DailyRewardErrorCode.Cancelled, "Daily reward operation was cancelled.")); } finally { NotifyChanged(); } }
            }
        }
        public void Shutdown()
        {
            if (isStopped) return;
            isStopped = true;
            snapshot = null;
            if (activeOperations != null) Cancel(activeOperations);
            else { hasActiveOperation = false; NotifyChanged(); }
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
            if (failures.Count > 0) throw new AggregateException("Daily reward cleanup failed.", failures);
        }
        private static IEnumerator Fail(Action<DailyRewardError> onFailure, DailyRewardErrorCode code, string message) { onFailure(new DailyRewardError(code, message)); yield break; }
        private void EnsureStarted() { if (!hasStarted || isStopped) throw new InvalidOperationException("DailyRewardsModule requires an active initialized lifetime."); }
        private sealed class OwnedOperation : IEnumerator, IDisposable
        {
            private readonly DailyRewardsModule owner;
            private readonly Stack<IEnumerator> operations;
            private readonly IEnumerator routine;
            private bool hasStarted;
            private bool isDisposed;
            public OwnedOperation(DailyRewardsModule owner, Stack<IEnumerator> operations, IEnumerator routine) { this.owner = owner; this.operations = operations; this.routine = routine; }
            public object Current => routine.Current;
            public bool MoveNext() { if (isDisposed) return false; hasStarted = true; return routine.MoveNext(); }
            public void Reset() { throw new NotSupportedException(); }
            public void Dispose() { if (isDisposed) return; isDisposed = true; if (hasStarted) (routine as IDisposable)?.Dispose(); else owner.Cancel(operations); }
        }
    }
}
