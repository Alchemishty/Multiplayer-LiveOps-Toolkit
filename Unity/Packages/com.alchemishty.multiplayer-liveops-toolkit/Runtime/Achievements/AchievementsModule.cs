using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Core;
using MultiplayerLiveOpsToolkit.Wallet;
using MultiplayerLiveOpsToolkit.Inventory;

namespace MultiplayerLiveOpsToolkit.Achievements
{
    /// <summary>Backend-confirmed achievements and atomic reward claims with explicit optional economy capabilities.</summary>
    public sealed class AchievementsModule : ILiveOpsModule, IAchievementService
    {
        private readonly IReadOnlyCollection<string> moduleDependencies;
        private bool needsWalletState;
        private bool needsInventoryState;
        private readonly IAchievementBackend backend;
        private readonly IWalletState wallet;
        private readonly IInventoryState inventory;
        private readonly Action<Exception> reportSubscriberException;
        private bool hasStarted;
        private bool isStopped;
        private bool ownsAccountStates;
        private bool isMutation;
        private bool hasDispatched;
        private Stack<IEnumerator> activeOperations;
        private Action<AchievementError> pendingFailure;
        private AchievementSnapshot snapshot;
        public string Id => "achievements";
        public IReadOnlyCollection<string> Dependencies => moduleDependencies;
        public bool HasSnapshot => snapshot != null && !isStopped;
        private bool hasActiveOperation;
        public bool IsBusy => !isStopped && (hasActiveOperation || (wallet != null && wallet.IsBusy) || (inventory != null && inventory.IsBusy));
        public bool RequiresRefresh { get; private set; }
        public AchievementSnapshot Snapshot => HasSnapshot ? snapshot : throw new InvalidOperationException("Achievements requires a confirmed snapshot.");
        public event Action StateChanged;
        public event Action<string> AchievementUnlocked;

        public AchievementsModule(IAchievementBackend backend, Action<Exception> reportSubscriberException, IWalletState wallet = null, IInventoryState inventory = null)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.wallet = wallet; // Explicit optional capability: only configured rewards/collections require it.
            this.inventory = inventory;
            var dependencies = new List<string> { "identity" };
            if (wallet != null) dependencies.Add("wallet");
            if (inventory != null) dependencies.Add("inventory");
            moduleDependencies = dependencies.AsReadOnly();
            this.reportSubscriberException = reportSubscriberException ?? throw new ArgumentNullException(nameof(reportSubscriberException));
        }
        public IEnumerator Initialize()
        {
            if (hasStarted || isStopped) throw new InvalidOperationException("AchievementsModule cannot be initialized twice or reused after shutdown.");
            hasStarted = true;
            return Reserve(null, state => { }, error => throw new InvalidOperationException("Achievement initialization failed: " + error.Message));
        }
        public IEnumerator Refresh(Action onSuccess, Action<AchievementError> onFailure)
        {
            if (onSuccess == null) throw new ArgumentNullException(nameof(onSuccess));
            if (onFailure == null) throw new ArgumentNullException(nameof(onFailure));
            EnsureStarted();
            return Reserve(null, state => onSuccess(), onFailure);
        }
        public IEnumerator Claim(string achievementId, Action onSuccess, Action<AchievementError> onFailure)
        {
            if (onSuccess == null) throw new ArgumentNullException(nameof(onSuccess));
            if (onFailure == null) throw new ArgumentNullException(nameof(onFailure));
            EnsureStarted(); AchievementEntry.ValidateId(achievementId);
            if (RequiresRefresh || (needsWalletState && !wallet.HasSnapshot) || (needsInventoryState && !inventory.HasSnapshot)) return Fail(onFailure, AchievementErrorCode.RefreshRequired, "Refresh authoritative achievement/account state before another claim.");
            if (!HasSnapshot || !snapshot.Entries.TryGetValue(achievementId, out var entry)) return Fail(onFailure, AchievementErrorCode.Rejected, "Achievement is unavailable.");
            if (entry.IsClaimed) return Fail(onFailure, AchievementErrorCode.AlreadyClaimed, "Achievement reward is already claimed.");
            if (!entry.CanClaim) return Fail(onFailure, AchievementErrorCode.Rejected, "Achievement has no eligible reward to claim.");
            return Reserve(achievementId, state => onSuccess(), onFailure);
        }
        private IEnumerator Reserve(string achievementId, Action<AchievementState> onSuccess, Action<AchievementError> onFailure)
        {
            if (IsBusy || (wallet != null && wallet.IsBusy) || (inventory != null && inventory.IsBusy)) return Fail(onFailure, AchievementErrorCode.Busy, "An account operation is already in progress.");
            if (wallet != null) wallet.BeginUpdate();
            try { if (inventory != null) inventory.BeginUpdate(); }
            catch { if (wallet != null) wallet.EndUpdate(); throw; }
            ownsAccountStates = true;
            hasActiveOperation = true;
            isMutation = achievementId != null;
            hasDispatched = false;
            pendingFailure = onFailure;
            var operations = new Stack<IEnumerator>();
            operations.Push(FetchState(achievementId, onSuccess));
            activeOperations = operations;
            NotifyChanged();
            return new OwnedOperation(this, operations, Run(operations));
        }
        private IEnumerator FetchState(string achievementId, Action<AchievementState> onSuccess)
        {
            AchievementState result = null;
            AchievementError failure = null;
            int completions = 0;
            bool isAccepting = true;
            hasDispatched = true;
            Action<AchievementState> accept = value => { if (isAccepting && !isStopped) { completions++; result = value; } };
            Action<AchievementError> reject = error => { if (isAccepting && !isStopped) { completions++; failure = error; } };
            try
            {
                yield return (achievementId == null ? backend.Fetch(accept, reject) : backend.Claim(achievementId, accept, reject))
                    ?? throw new InvalidOperationException("Achievement backend returned no operation.");
            }
            finally { isAccepting = false; }
            if (isStopped) yield break;
            if (completions != 1 || (result == null && failure == null)) failure = new AchievementError(isMutation ? AchievementErrorCode.UnknownOutcome : AchievementErrorCode.InvalidResponse, "Achievement response could not be confirmed.");
            if (failure == null)
            {
                if ((result.Achievements.RequiresWallet && wallet == null) || (result.Achievements.RequiresInventory && inventory == null))
                    failure = new AchievementError(AchievementErrorCode.Misconfigured, "Configured achievement rewards/collections require explicitly selected Wallet/Inventory capabilities.");
                else if ((needsWalletState && wallet != null && !wallet.HasSnapshot && result.Wallet == null) || (needsInventoryState && inventory != null && !inventory.HasSnapshot && result.Inventory == null))
                    failure = new AchievementError(AchievementErrorCode.Misconfigured, "Achievement configuration changed before uncertain account state was refreshed.");
                else if (isMutation)
                {
                    if (result.ClaimedAchievementId != achievementId) failure = new AchievementError(AchievementErrorCode.UnknownOutcome, "Claim response did not confirm the requested achievement.");
                    else
                    {
                        var reward = snapshot.Entries[achievementId].Reward;
                        if ((reward.Kind == AchievementRewardKind.Currency && (result.Wallet == null || !result.Wallet.Balances.ContainsKey(reward.CurrencyId)))
                            || (reward.Kind == AchievementRewardKind.Cosmetic && (result.Inventory == null || !result.Inventory.Items.ContainsKey(reward.ItemId))))
                            failure = new AchievementError(AchievementErrorCode.UnknownOutcome, "Claim response did not contain the confirmed reward state.");
                    }
                }
                else if (result.ClaimedAchievementId != null) failure = new AchievementError(AchievementErrorCode.InvalidResponse, "Achievement refresh returned a mutation result.");
            }
            if (failure != null)
            {
                if (isMutation && (failure.Code == AchievementErrorCode.Unavailable || failure.Code == AchievementErrorCode.InvalidResponse || failure.Code == AchievementErrorCode.UnknownOutcome || failure.Code == AchievementErrorCode.Cancelled))
                { InvalidateAccount(); failure = new AchievementError(AchievementErrorCode.UnknownOutcome, "Claim outcome is unknown. Refresh Achievements before trying again."); }
                CompleteFailure(failure);
                yield break;
            }
            var newlyUnlocked = new List<string>();
            if (snapshot != null)
                foreach (var entry in result.Achievements.Entries.Values)
                    if (entry.IsUnlocked && snapshot.Entries.TryGetValue(entry.AchievementId, out var previous) && !previous.IsUnlocked) newlyUnlocked.Add(entry.AchievementId);
            if (wallet != null && result.Wallet != null) wallet.StageSnapshot(result.Wallet);
            if (inventory != null && result.Inventory != null) inventory.StageSnapshot(result.Inventory);
            snapshot = result.Achievements;
            needsWalletState = snapshot.RequiresWallet; needsInventoryState = snapshot.RequiresInventory;
            RequiresRefresh = false;
            pendingFailure = null; hasActiveOperation = false; // Completion is committed before observers can stop/rebind the owner.
            ReleaseAccountStates();
            NotifyChanged();
            foreach (string id in newlyUnlocked) { if (!isStopped) NotifyUnlocked(id); }
            onSuccess(result);
        }
        private void NotifyUnlocked(string achievementId)
        {
            var subscribers = AchievementUnlocked;
            if (subscribers == null) return;
            foreach (Action<string> subscriber in subscribers.GetInvocationList())
            { try { subscriber(achievementId); } catch (Exception exception) { reportSubscriberException(exception); } }
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
                        CompleteFailure(new AchievementError(isMutation ? AchievementErrorCode.UnknownOutcome : AchievementErrorCode.Unavailable, "Achievement operation could not be confirmed."));
                        yield break;
                    }
                    if (!hasNext) { operations.Pop(); (operation as IDisposable)?.Dispose(); }
                    else if (instruction is IEnumerator nested) operations.Push(nested);
                    else yield return instruction;
                }
            }
            finally { Cancel(operations); }
        }
        private void CompleteFailure(AchievementError error)
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
                if (needsInventoryState && inventory != null) inventory.StageSnapshot(null);
            }
        }
        private void ReleaseAccountStates()
        {
            if (!ownsAccountStates) return;
            ownsAccountStates = false;
            try { if (wallet != null) wallet.EndUpdate(); }
            finally { if (inventory != null) inventory.EndUpdate(); }
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
                finally { try { cancelled?.Invoke(new AchievementError(AchievementErrorCode.Cancelled, "Achievement operation was cancelled.")); } finally { NotifyChanged(); } }
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
            if (failures.Count > 0) throw new AggregateException("Achievement cleanup failed.", failures);
        }
        private static IEnumerator Fail(Action<AchievementError> onFailure, AchievementErrorCode code, string message) { onFailure(new AchievementError(code, message)); yield break; }
        private void EnsureStarted() { if (!hasStarted || isStopped) throw new InvalidOperationException("AchievementsModule requires an active initialized lifetime."); }
        private sealed class OwnedOperation : IEnumerator, IDisposable
        {
            private readonly AchievementsModule owner;
            private readonly Stack<IEnumerator> operations;
            private readonly IEnumerator routine;
            private bool hasStarted;
            private bool isDisposed;
            public OwnedOperation(AchievementsModule owner, Stack<IEnumerator> operations, IEnumerator routine) { this.owner = owner; this.operations = operations; this.routine = routine; }
            public object Current => routine.Current;
            public bool MoveNext() { if (isDisposed) return false; hasStarted = true; return routine.MoveNext(); }
            public void Reset() { throw new NotSupportedException(); }
            public void Dispose() { if (isDisposed) return; isDisposed = true; if (hasStarted) (routine as IDisposable)?.Dispose(); else owner.Cancel(operations); }
        }
    }
}
