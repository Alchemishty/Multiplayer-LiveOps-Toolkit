using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace MultiplayerLiveOpsToolkit.Inventory
{
    public sealed class CosmeticItem
    {
        public string ItemId { get; }
        public string DisplayName { get; }
        public string CategoryId { get; }
        public CosmeticItem(string itemId, string displayName, string categoryId)
        {
            ValidateId(itemId, nameof(itemId));
            ValidateId(categoryId, nameof(categoryId));
            ValidateName(displayName);
            ItemId = itemId; DisplayName = displayName; CategoryId = categoryId;
        }
        internal static void ValidateId(string value, string argument)
        {
            if (value == null || !Regex.IsMatch(value, @"\A[A-Za-z0-9_.:-]{1,128}\z")) throw new ArgumentException("Cosmetic inventory requires a valid identifier.", argument);
        }
        internal static void ValidateName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256) throw new ArgumentException("Cosmetic inventory requires a display name of at most 256 characters.", nameof(value));
        }
    }
    public sealed class CosmeticSet
    {
        public string SetId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<string> ItemIds { get; }
        public CosmeticSet(string setId, string displayName, IEnumerable<string> itemIds)
        {
            CosmeticItem.ValidateId(setId, nameof(setId));
            CosmeticItem.ValidateName(displayName);
            if (itemIds == null) throw new ArgumentNullException(nameof(itemIds));
            var members = new List<string>();
            var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var itemId in itemIds)
            {
                CosmeticItem.ValidateId(itemId, nameof(itemIds));
                if (!uniqueIds.Add(itemId)) throw new ArgumentException("Cosmetic set contains duplicate item IDs.", nameof(itemIds));
                members.Add(itemId);
            }
            if (members.Count == 0) throw new ArgumentException("Cosmetic sets require at least one item.", nameof(itemIds));
            SetId = setId; DisplayName = displayName; ItemIds = members.AsReadOnly();
        }
    }
    public sealed class InventorySnapshot
    {
        public IReadOnlyDictionary<string, CosmeticItem> Items { get; }
        public IReadOnlyDictionary<string, CosmeticSet> Sets { get; }
        public InventorySnapshot(IEnumerable<CosmeticItem> items, IEnumerable<CosmeticSet> sets)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (sets == null) throw new ArgumentNullException(nameof(sets));
            var itemsById = new Dictionary<string, CosmeticItem>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item == null) throw new ArgumentException("Inventory contains a null item.", nameof(items));
                itemsById.Add(item.ItemId, item);
            }
            var setsById = new Dictionary<string, CosmeticSet>(StringComparer.Ordinal);
            foreach (var cosmeticSet in sets)
            {
                if (cosmeticSet == null) throw new ArgumentException("Inventory contains a null set.", nameof(sets));
                foreach (var itemId in cosmeticSet.ItemIds)
                    if (!itemsById.ContainsKey(itemId)) throw new ArgumentException("Owned sets require ownership of every member.", nameof(sets));
                setsById.Add(cosmeticSet.SetId, cosmeticSet);
            }
            Items = new ReadOnlyDictionary<string, CosmeticItem>(itemsById);
            Sets = new ReadOnlyDictionary<string, CosmeticSet>(setsById);
        }
    }
    public enum InventoryErrorCode { Unavailable, InvalidResponse, Misconfigured, Busy, Cancelled }
    public sealed class InventoryError
    {
        public InventoryErrorCode Code { get; }
        public string Message { get; }
        public InventoryError(InventoryErrorCode code, string message) { Code = code; Message = message ?? throw new ArgumentNullException(nameof(message)); }
    }
}
