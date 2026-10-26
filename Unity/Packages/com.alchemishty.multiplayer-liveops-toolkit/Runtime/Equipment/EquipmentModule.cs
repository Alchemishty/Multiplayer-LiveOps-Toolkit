using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Core;
using MultiplayerLiveOpsToolkit.Inventory;

namespace MultiplayerLiveOpsToolkit.Equipment
{
    /// <summary>Canonical account equipment and local-only preview; no avatar or networking dependency.</summary>
    public sealed class EquipmentModule : ILiveOpsModule, IEquipmentService
    {
        private static readonly string[] ModuleDependencies = { "identity", "inventory" };
        private readonly IEquipmentBackend backend;
        private readonly IInventoryService inventory;
        private readonly Action<Exception> reportSubscriberException;
        private EquipmentState snapshot;
        private EquipmentLoadout preview;
        private bool hasStarted;
        private bool isStopped;
        private bool isMutation;
        private bool hasDispatched;
        private Stack<IEnumerator> activeOperations;
        private Action<EquipmentError> pendingFailure;
        public string Id => "equipment";
        public IReadOnlyCollection<string> Dependencies => Array.AsReadOnly(ModuleDependencies);
        public bool HasSnapshot => snapshot != null && !isStopped;
        public bool IsBusy { get; private set; }
        public bool RequiresRefresh { get; private set; }
        public EquipmentState Snapshot => HasSnapshot ? snapshot : throw new InvalidOperationException("Equipment requires a confirmed snapshot.");
        public EquipmentLoadout Presentation => preview ?? Snapshot.Loadout;
        public bool IsPreviewActive => preview != null;
        public event Action StateChanged;
        public event Action PreviewChanged;

        public EquipmentModule(IEquipmentBackend backend, IInventoryService inventory, Action<Exception> reportSubscriberException)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            this.reportSubscriberException = reportSubscriberException ?? throw new ArgumentNullException(nameof(reportSubscriberException));
        }
        public IEnumerator Initialize()
        {
            if (hasStarted || isStopped) throw new InvalidOperationException("EquipmentModule cannot be reused after initialization/shutdown.");
            hasStarted = true;
            return Reserve(null, () => { }, error => throw new InvalidOperationException("Equipment initialization failed: " + error.Message));
        }
        public IEnumerator Refresh(Action onSuccess, Action<EquipmentError> onFailure)
        {
            ValidateCallbacks(onSuccess, onFailure); EnsureStarted();
            return Reserve(null, onSuccess, onFailure);
        }
        public IEnumerator ApplyItems(IEnumerable<string> itemIds, Action onSuccess, Action<EquipmentError> onFailure)
        {
            ValidateCallbacks(onSuccess, onFailure); EnsureStarted();
            var ids = CopyIds(itemIds);
            if (!CanMutate(onFailure, out var rejection)) return rejection;
            foreach (string id in ids)
                if (!inventory.Snapshot.Items.ContainsKey(id) || !Snapshot.SlotsByItemId.ContainsKey(id)) return Fail(onFailure, EquipmentErrorCode.Rejected, "Cannot equip an unowned or undefined cosmetic: " + id);
            EquipmentLoadout candidate;
            try { candidate = Project(ids); }
            catch (ArgumentException) { return Fail(onFailure, EquipmentErrorCode.Rejected, "Requested set contains conflicting slots."); }
            return Reserve(new List<string>(candidate.ItemsBySlot.Values).AsReadOnly(), onSuccess, onFailure);
        }
        public IEnumerator Unequip(IEnumerable<string> slotIds, Action onSuccess, Action<EquipmentError> onFailure)
        {
            ValidateCallbacks(onSuccess, onFailure); EnsureStarted();
            var slots = CopyIds(slotIds);
            if (!CanMutate(onFailure, out var rejection)) return rejection;
            var remaining = new Dictionary<string, string>(Snapshot.Loadout.ItemsBySlot);
            foreach (string slot in slots)
            {
                if (!Snapshot.Slots.ContainsKey(slot)) throw new ArgumentException("Undefined equipment slot: " + slot, nameof(slotIds));
                remaining.Remove(slot);
            }
            return Reserve(new List<string>(remaining.Values).AsReadOnly(), onSuccess, onFailure);
        }
        public IEnumerator Clear(Action onSuccess, Action<EquipmentError> onFailure)
        {
            ValidateCallbacks(onSuccess, onFailure); EnsureStarted();
            if (!CanMutate(onFailure, out var rejection)) return rejection;
            return Reserve(Array.Empty<string>(), onSuccess, onFailure);
        }
        private bool CanMutate(Action<EquipmentError> onFailure, out IEnumerator rejection)
        {
            rejection = null;
            if (IsBusy || inventory.IsBusy) rejection = Fail(onFailure, EquipmentErrorCode.Busy, "An equipment or inventory operation is in progress.");
            else if (RequiresRefresh || !HasSnapshot || !inventory.HasSnapshot) rejection = Fail(onFailure, EquipmentErrorCode.RefreshRequired, "Refresh authoritative equipment and ownership before changing equipment.");
            return rejection == null;
        }
        public void Preview(IEnumerable<string> itemIds)
        {
            EnsureStarted();
            var candidate = Project(CopyIds(itemIds));
            preview = candidate;
            Notify(PreviewChanged);
        }
        public void ClearPreview()
        {
            if (preview == null) return; // No local presentation override is active.
            preview = null;
            Notify(PreviewChanged);
        }
        private EquipmentLoadout Project(IReadOnlyList<string> ids)
        {
            var state = Snapshot;
            var additions = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string id in ids)
            {
                if (!state.SlotsByItemId.TryGetValue(id, out string slot)) throw new ArgumentException("Undefined equipment cosmetic: " + id);
                additions.Add(slot, id);
            }
            var requested = new EquipmentLoadout(state.Loadout.Revision, additions);
            state.ValidateLoadout(requested); // A set cannot silently drop one of its own members.
            var result = new Dictionary<string, string>(state.Loadout.ItemsBySlot);
            foreach (var item in additions)
            {
                foreach (string conflict in state.Slots[item.Key].Conflicts) result.Remove(conflict);
                result[item.Key] = item.Value;
            }
            return new EquipmentLoadout(state.Loadout.Revision, result);
        }
        private IEnumerator Reserve(IReadOnlyList<string> itemIds, Action onSuccess, Action<EquipmentError> onFailure)
        {
            if (IsBusy) return Fail(onFailure, EquipmentErrorCode.Busy, "An equipment operation is in progress.");
            IsBusy = true; isMutation = itemIds != null; hasDispatched = false; pendingFailure = onFailure;
            var operations = new Stack<IEnumerator>();
            operations.Push(FetchState(itemIds, onSuccess));
            activeOperations = operations;
            Notify(StateChanged);
            return new OwnedOperation(this, operations, Run(operations));
        }
        private IEnumerator FetchState(IReadOnlyList<string> itemIds, Action onSuccess)
        {
            EquipmentState result = null; EquipmentError failure = null; int completions = 0; bool isAccepting = true;
            long previousRevision = snapshot == null ? -1 : snapshot.Loadout.Revision;
            hasDispatched = true;
            Action<EquipmentState> accept = value => { if (isAccepting && !isStopped) { completions++; result = value; } };
            Action<EquipmentError> reject = error => { if (isAccepting && !isStopped) { completions++; failure = error; } };
            try { yield return (itemIds == null ? backend.Fetch(accept, reject) : backend.Replace(itemIds, previousRevision, accept, reject)) ?? throw new InvalidOperationException("Equipment backend returned no operation."); }
            finally { isAccepting = false; }
            if (isStopped) yield break;
            if (completions != 1 || (result == null && failure == null)) failure = new EquipmentError(isMutation ? EquipmentErrorCode.UnknownOutcome : EquipmentErrorCode.InvalidResponse, "Equipment backend must complete exactly once.");
            if (failure == null)
            {
                bool invalid = result.Loadout.Revision < previousRevision || (isMutation && result.Loadout.Revision <= previousRevision);
                if (snapshot != null && result.Loadout.Revision == previousRevision)
                {
                    invalid |= result.Loadout.ItemsBySlot.Count != snapshot.Loadout.ItemsBySlot.Count;
                    foreach (var item in result.Loadout.ItemsBySlot)
                        invalid |= !snapshot.Loadout.ItemsBySlot.TryGetValue(item.Key, out string previousId) || previousId != item.Value;
                }
                if (isMutation)
                {
                    var expected = new HashSet<string>(itemIds, StringComparer.Ordinal);
                    invalid |= !expected.SetEquals(result.Loadout.ItemsBySlot.Values);
                }
                if (invalid) failure = new EquipmentError(isMutation ? EquipmentErrorCode.UnknownOutcome : EquipmentErrorCode.InvalidResponse, "Equipment response has stale revision or mismatched loadout.");
            }
            if (failure != null)
            {
                if (isMutation && (failure.Code == EquipmentErrorCode.UnknownOutcome || failure.Code == EquipmentErrorCode.InvalidResponse || failure.Code == EquipmentErrorCode.Cancelled || failure.Code == EquipmentErrorCode.StaleRevision)) RequiresRefresh = true;
                CompleteFailure(failure); yield break;
            }
            snapshot = result; RequiresRefresh = false; preview = null;
            // Commit completion before notifications: observers may synchronously stop the context.
            pendingFailure = null; IsBusy = false;
            Notify(PreviewChanged); Notify(StateChanged); onSuccess();
        }
        private IEnumerator Run(Stack<IEnumerator> operations)
        {
            try
            {
                while (activeOperations == operations && operations.Count > 0 && !isStopped)
                {
                    var operation = operations.Peek(); bool hasNext = false; object instruction = null; Exception failure = null;
                    try { hasNext = operation.MoveNext(); if (hasNext) instruction = operation.Current; } catch (Exception exception) { failure = exception; }
                    if (activeOperations != operations) yield break;
                    if (failure != null)
                    {
                        if (pendingFailure == null) throw failure;
                        if (isMutation) RequiresRefresh = true;
                        CompleteFailure(new EquipmentError(isMutation ? EquipmentErrorCode.UnknownOutcome : EquipmentErrorCode.Unavailable, "Equipment operation could not be confirmed.")); yield break;
                    }
                    if (!hasNext) { operations.Pop(); (operation as IDisposable)?.Dispose(); }
                    else if (instruction is IEnumerator nested) operations.Push(nested);
                    else yield return instruction;
                }
            }
            finally { Cancel(operations); }
        }
        private void CompleteFailure(EquipmentError error)
        {
            var callback = pendingFailure; pendingFailure = null; IsBusy = false; Notify(StateChanged); callback(error);
        }
        private void Cancel(Stack<IEnumerator> operations)
        {
            if (activeOperations != operations) return;
            activeOperations = null;
            var callback = pendingFailure; pendingFailure = null;
            if (callback != null && isMutation && hasDispatched) RequiresRefresh = true;
            IsBusy = false;
            try { DisposeOperations(operations); }
            finally { try { callback?.Invoke(new EquipmentError(EquipmentErrorCode.Cancelled, "Equipment operation was cancelled.")); } finally { Notify(StateChanged); } }
        }
        public void Shutdown()
        {
            if (isStopped) return;
            isStopped = true; snapshot = null; preview = null;
            try { if (activeOperations != null) Cancel(activeOperations); }
            finally { IsBusy = false; Notify(PreviewChanged); Notify(StateChanged); }
        }
        private void Notify(Action subscribers)
        {
            if (subscribers == null) return;
            foreach (Action subscriber in subscribers.GetInvocationList()) { try { subscriber(); } catch (Exception exception) { reportSubscriberException(exception); } }
        }
        private static IReadOnlyList<string> CopyIds(IEnumerable<string> ids)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            var result = new List<string>(); var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids) { EquipmentLoadout.ValidateId(id); if (!unique.Add(id)) throw new ArgumentException("Equipment IDs must be unique.", nameof(ids)); result.Add(id); }
            if (result.Count > 32) throw new ArgumentException("Equipment accepts at most 32 IDs.", nameof(ids));
            return result.AsReadOnly();
        }
        private static void DisposeOperations(Stack<IEnumerator> operations)
        {
            var failures = new List<Exception>();
            while (operations.Count > 0) { try { (operations.Pop() as IDisposable)?.Dispose(); } catch (Exception exception) { failures.Add(exception); } }
            if (failures.Count > 0) throw new AggregateException("Equipment cleanup failed.", failures);
        }
        private static IEnumerator Fail(Action<EquipmentError> failure, EquipmentErrorCode code, string message) { failure(new EquipmentError(code, message)); yield break; }
        private static void ValidateCallbacks(Action success, Action<EquipmentError> failure) { if (success == null) throw new ArgumentNullException(nameof(success)); if (failure == null) throw new ArgumentNullException(nameof(failure)); }
        private void EnsureStarted() { if (!hasStarted || isStopped) throw new InvalidOperationException("EquipmentModule requires an initialized active lifetime."); }
        private sealed class OwnedOperation : IEnumerator, IDisposable
        {
            private readonly EquipmentModule owner; private readonly Stack<IEnumerator> operations; private readonly IEnumerator routine; private bool hasStarted; private bool isDisposed;
            public OwnedOperation(EquipmentModule owner, Stack<IEnumerator> operations, IEnumerator routine) { this.owner = owner; this.operations = operations; this.routine = routine; }
            public object Current => routine.Current;
            public bool MoveNext() { if (isDisposed) return false; hasStarted = true; return routine.MoveNext(); }
            public void Reset() { throw new NotSupportedException(); }
            public void Dispose() { if (isDisposed) return; isDisposed = true; if (hasStarted) (routine as IDisposable)?.Dispose(); else owner.Cancel(operations); }
        }
    }
}
