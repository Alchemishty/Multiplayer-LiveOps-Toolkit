using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using MultiplayerLiveOpsToolkit.Wallet;
using MultiplayerLiveOpsToolkit.Inventory;
namespace MultiplayerLiveOpsToolkit.Shop
{
    public sealed class ShopPrice
    {
        public string CurrencyId { get; }
        public long Amount { get; }
        public ShopPrice(string currencyId, long amount)
        {
            ShopOffer.ValidateId(currencyId);
            if (amount < 0 || amount > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(amount));
            CurrencyId = currencyId; Amount = amount;
        }
    }
    public sealed class ShopOffer
    {
        public string OfferId { get; }
        public string DisplayName { get; }
        public string CategoryId { get; }
        public bool IsSet { get; }
        public IReadOnlyList<string> ItemIds { get; }
        public IReadOnlyDictionary<string, long> Costs { get; }
        public ShopOffer(string offerId, string displayName, string categoryId, bool isSet, IEnumerable<string> itemIds, IEnumerable<ShopPrice> costs)
        {
            ValidateId(offerId); ValidateId(categoryId);
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 256) throw new ArgumentException("Shop offer requires a display name of at most 256 characters.", nameof(displayName));
            if (itemIds == null) throw new ArgumentNullException(nameof(itemIds));
            if (costs == null) throw new ArgumentNullException(nameof(costs));
            var members = new List<string>();
            var memberIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var itemId in itemIds) { ValidateId(itemId); if (!memberIds.Add(itemId)) throw new ArgumentException("Offer contains duplicate items."); members.Add(itemId); }
            if (members.Count == 0 || (!isSet && members.Count != 1)) throw new ArgumentException("Individual offers require one item; sets require at least one item.");
            var costsByCurrency = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var cost in costs) { if (cost == null) throw new ArgumentException("Offer contains a null price."); costsByCurrency.Add(cost.CurrencyId, cost.Amount); }
            OfferId = offerId; DisplayName = displayName; CategoryId = categoryId; IsSet = isSet;
            ItemIds = members.AsReadOnly(); Costs = new ReadOnlyDictionary<string, long>(costsByCurrency);
        }
        internal static void ValidateId(string value)
        { if (value == null || !Regex.IsMatch(value, @"\A[A-Za-z0-9_.:-]{1,128}\z")) throw new ArgumentException("Shop requires a valid identifier.", nameof(value)); }
    }
    public sealed class ShopSnapshot
    {
        public IReadOnlyDictionary<string, ShopOffer> Offers { get; }
        public ShopSnapshot(IEnumerable<ShopOffer> offers)
        {
            if (offers == null) throw new ArgumentNullException(nameof(offers));
            var offersById = new Dictionary<string, ShopOffer>(StringComparer.Ordinal);
            foreach (var offer in offers) { if (offer == null) throw new ArgumentException("Shop contains a null offer."); offersById.Add(offer.OfferId, offer); }
            Offers = new ReadOnlyDictionary<string, ShopOffer>(offersById);
        }
    }
    public sealed class ShopState
    {
        public ShopSnapshot Catalog { get; }
        public WalletSnapshot Wallet { get; }
        public InventorySnapshot Inventory { get; }
        public string PurchasedOfferId { get; }
        public ShopState(ShopSnapshot catalog, WalletSnapshot wallet, InventorySnapshot inventory, string purchasedOfferId = null)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            if (purchasedOfferId != null) ShopOffer.ValidateId(purchasedOfferId);
            PurchasedOfferId = purchasedOfferId;
        }
    }
    public enum ShopErrorCode { Unavailable, Rejected, AlreadyOwned, InsufficientFunds, Misconfigured, InvalidResponse, UnknownOutcome, RefreshRequired, Busy, Cancelled }
    public sealed class ShopError
    {
        public ShopErrorCode Code { get; }
        public string Message { get; }
        public ShopError(ShopErrorCode code, string message) { Code = code; Message = message ?? throw new ArgumentNullException(nameof(message)); }
    }
}
