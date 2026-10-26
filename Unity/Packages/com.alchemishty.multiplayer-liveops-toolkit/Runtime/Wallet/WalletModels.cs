using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace MultiplayerLiveOpsToolkit.Wallet
{
    public sealed class WalletBalance
    {
        public const long MAX_AMOUNT = int.MaxValue;
        public string CurrencyId { get; }
        public long Amount { get; }
        public WalletBalance(string currencyId, long amount)
        {
            WalletBalance.ValidateId(currencyId, nameof(currencyId));
            if (amount < 0 || amount > MAX_AMOUNT) throw new ArgumentOutOfRangeException(nameof(amount));
            CurrencyId = currencyId; Amount = amount;
        }
        internal static void ValidateId(string value, string argument)
        {
            if (value == null || !Regex.IsMatch(value, @"\A[A-Za-z0-9_.:-]{1,128}\z")) throw new ArgumentException("Wallet requires a valid identifier.", argument);
        }
    }
    public sealed class WalletConversionOffer
    {
        public string OfferId { get; }
        public string SourceCurrencyId { get; }
        public string TargetCurrencyId { get; }
        public long SourceUnits { get; }
        public long TargetUnits { get; }
        public WalletConversionOffer(string offerId, string sourceCurrencyId, string targetCurrencyId, long sourceUnits, long targetUnits)
        {
            WalletBalance.ValidateId(offerId, nameof(offerId));
            WalletBalance.ValidateId(sourceCurrencyId, nameof(sourceCurrencyId));
            WalletBalance.ValidateId(targetCurrencyId, nameof(targetCurrencyId));
            if (sourceCurrencyId == targetCurrencyId || sourceUnits < 1 || targetUnits < 1 || sourceUnits > WalletBalance.MAX_AMOUNT || targetUnits > WalletBalance.MAX_AMOUNT)
                throw new ArgumentException("Wallet conversion requires distinct currencies and positive bounded integer units.");
            OfferId = offerId; SourceCurrencyId = sourceCurrencyId; TargetCurrencyId = targetCurrencyId; SourceUnits = sourceUnits; TargetUnits = targetUnits;
        }
    }
    public sealed class WalletSnapshot
    {
        public IReadOnlyDictionary<string, WalletBalance> Balances { get; }
        public IReadOnlyDictionary<string, WalletConversionOffer> Offers { get; }
        public WalletSnapshot(IEnumerable<WalletBalance> balances, IEnumerable<WalletConversionOffer> offers)
        {
            if (balances == null) throw new ArgumentNullException(nameof(balances));
            if (offers == null) throw new ArgumentNullException(nameof(offers));
            var balancesById = new Dictionary<string, WalletBalance>(StringComparer.Ordinal);
            foreach (var balance in balances)
            {
                if (balance == null) throw new ArgumentException("Wallet snapshot contains a null balance.", nameof(balances));
                balancesById.Add(balance.CurrencyId, balance);
            }
            var offersById = new Dictionary<string, WalletConversionOffer>(StringComparer.Ordinal);
            foreach (var offer in offers)
            {
                if (offer == null || !balancesById.ContainsKey(offer.SourceCurrencyId) || !balancesById.ContainsKey(offer.TargetCurrencyId))
                    throw new ArgumentException("Wallet offers require existing source and target currencies.", nameof(offers));
                offersById.Add(offer.OfferId, offer);
            }
            Balances = new ReadOnlyDictionary<string, WalletBalance>(balancesById);
            Offers = new ReadOnlyDictionary<string, WalletConversionOffer>(offersById);
        }
    }
    public enum WalletErrorCode { Unavailable, Rejected, InsufficientFunds, InvalidUnits, BalanceLimit, Misconfigured, InvalidResponse, UnknownOutcome, RefreshRequired, Busy, Cancelled }
    public sealed class WalletError
    {
        public WalletErrorCode Code { get; }
        public string Message { get; }
        public WalletError(WalletErrorCode code, string message) { Code = code; Message = message ?? throw new ArgumentNullException(nameof(message)); }
    }
}
