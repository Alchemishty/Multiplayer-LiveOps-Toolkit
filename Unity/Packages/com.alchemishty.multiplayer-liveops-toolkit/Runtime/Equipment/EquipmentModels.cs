using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace MultiplayerLiveOpsToolkit.Equipment
{
    public sealed class EquipmentSlot
    {
        public string SlotId { get; }
        public IReadOnlyList<string> Conflicts { get; }
        public EquipmentSlot(string slotId, IEnumerable<string> conflicts)
        {
            EquipmentLoadout.ValidateId(slotId);
            if (conflicts == null) throw new ArgumentNullException(nameof(conflicts));
            var values = new List<string>();
            foreach (string conflict in conflicts)
            {
                EquipmentLoadout.ValidateId(conflict);
                if (conflict == slotId || values.Contains(conflict)) throw new ArgumentException("Slot conflicts must be unique other slots.", nameof(conflicts));
                values.Add(conflict);
            }
            SlotId = slotId; Conflicts = values.AsReadOnly();
        }
    }
    public sealed class EquipmentLoadout
    {
        public long Revision { get; }
        public IReadOnlyDictionary<string, string> ItemsBySlot { get; }
        public EquipmentLoadout(long revision, IEnumerable<KeyValuePair<string, string>> itemsBySlot)
        {
            if (revision < 0 || revision >= long.MaxValue) throw new ArgumentOutOfRangeException(nameof(revision));
            if (itemsBySlot == null) throw new ArgumentNullException(nameof(itemsBySlot));
            var items = new Dictionary<string, string>(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in itemsBySlot)
            {
                ValidateId(item.Key); ValidateId(item.Value);
                if (!ids.Add(item.Value)) throw new ArgumentException("A cosmetic cannot occupy multiple slots.", nameof(itemsBySlot));
                items.Add(item.Key, item.Value);
            }
            if (items.Count > 32) throw new ArgumentException("Equipment supports at most 32 slots.", nameof(itemsBySlot));
            Revision = revision; ItemsBySlot = new ReadOnlyDictionary<string, string>(items);
        }
        internal static void ValidateId(string value)
        {
            if (value == null || !Regex.IsMatch(value, @"\A[A-Za-z0-9_.:-]{1,128}\z", RegexOptions.CultureInvariant))
                throw new ArgumentException("Equipment requires bounded stable identifiers.", nameof(value));
        }
    }
    public sealed class EquipmentState
    {
        public IReadOnlyDictionary<string, EquipmentSlot> Slots { get; }
        public IReadOnlyDictionary<string, string> SlotsByItemId { get; }
        public EquipmentLoadout Loadout { get; }
        public EquipmentState(IEnumerable<EquipmentSlot> slots, IEnumerable<KeyValuePair<string, string>> slotsByItemId, EquipmentLoadout loadout)
        {
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            if (slotsByItemId == null) throw new ArgumentNullException(nameof(slotsByItemId));
            Loadout = loadout ?? throw new ArgumentNullException(nameof(loadout));
            var configured = new Dictionary<string, EquipmentSlot>(StringComparer.Ordinal);
            foreach (var slot in slots) { if (slot == null) throw new ArgumentException("Null equipment slot."); configured.Add(slot.SlotId, slot); }
            if (configured.Count < 1 || configured.Count > 32) throw new ArgumentException("Configure 1–32 equipment slots.");
            foreach (var slot in configured.Values)
                foreach (string conflict in slot.Conflicts)
                    if (!configured.TryGetValue(conflict, out var other) || !Contains(other.Conflicts, slot.SlotId)) throw new ArgumentException("Slot conflicts must be symmetric and configured.");
            var itemSlots = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in slotsByItemId) { EquipmentLoadout.ValidateId(item.Key); if (!configured.ContainsKey(item.Value)) throw new ArgumentException("Item requires a configured slot: " + item.Key); itemSlots.Add(item.Key, item.Value); }
            if (itemSlots.Count > 4096) throw new ArgumentException("Equipment content exceeds 4096 items.");
            Slots = new ReadOnlyDictionary<string, EquipmentSlot>(configured);
            SlotsByItemId = new ReadOnlyDictionary<string, string>(itemSlots);
            ValidateLoadout(loadout);
        }
        public void ValidateLoadout(EquipmentLoadout loadout)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            foreach (var item in loadout.ItemsBySlot)
            {
                if (!SlotsByItemId.TryGetValue(item.Value, out string slot) || slot != item.Key) throw new ArgumentException("Equipment item/slot mapping is invalid: " + item.Value);
                foreach (string conflict in Slots[slot].Conflicts) if (loadout.ItemsBySlot.ContainsKey(conflict)) throw new ArgumentException("Equipped slots conflict.");
            }
        }
        internal static bool Contains(IReadOnlyList<string> values, string target) { foreach (string value in values) if (value == target) return true; return false; }
    }
    public enum EquipmentErrorCode { Unavailable, Rejected, StaleRevision, Misconfigured, InvalidResponse, UnknownOutcome, RefreshRequired, Busy, Cancelled }
    public sealed class EquipmentError
    {
        public EquipmentErrorCode Code { get; }
        public string Message { get; }
        public EquipmentError(EquipmentErrorCode code, string message) { if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Equipment diagnostic required.", nameof(message)); Code = code; Message = message; }
    }
}
