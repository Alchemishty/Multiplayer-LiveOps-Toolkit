using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using MultiplayerLiveOpsToolkit.Wallet;
using MultiplayerLiveOpsToolkit.Inventory;
namespace MultiplayerLiveOpsToolkit.Achievements
{
    public enum AchievementRewardKind { None, Currency, Cosmetic }
    public sealed class AchievementReward
    {
        public AchievementRewardKind Kind { get; }
        public string CurrencyId { get; }
        public string ItemId { get; }
        public long Amount { get; }
        public AchievementReward(AchievementRewardKind kind, string currencyId = null, string itemId = null, long amount = 0)
        {
            if (kind == AchievementRewardKind.None)
            { if (currencyId != null || itemId != null || amount != 0) throw new ArgumentException("No-reward achievements cannot contain a grant."); }
            else if (kind == AchievementRewardKind.Currency)
            { AchievementEntry.ValidateId(currencyId); if (itemId != null || amount < 1 || amount > int.MaxValue) throw new ArgumentException("Currency rewards require a bounded positive amount and currency only."); }
            else if (kind == AchievementRewardKind.Cosmetic)
            { AchievementEntry.ValidateId(itemId); if (currencyId != null || amount != 1) throw new ArgumentException("Cosmetic rewards grant one stable cosmetic ID."); }
            else throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind; CurrencyId = currencyId; ItemId = itemId; Amount = amount;
        }
    }
    public sealed class AchievementEntry
    {
        public string AchievementId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public long CurrentCount { get; }
        public long TargetCount { get; }
        public bool IsUnlocked { get; }
        public bool IsClaimed { get; }
        public long UnlockedAt { get; }
        public AchievementReward Reward { get; }
        public bool CanClaim => IsUnlocked && !IsClaimed && Reward.Kind != AchievementRewardKind.None;
        public AchievementEntry(string achievementId, string displayName, string description, long currentCount, long targetCount, bool isUnlocked, bool isClaimed, long unlockedAt, AchievementReward reward)
        {
            ValidateId(achievementId);
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 256) throw new ArgumentException("Achievement display name is missing or oversized.", nameof(displayName));
            if (description == null || description.Length > 4096) throw new ArgumentException("Achievement description must be present and bounded.", nameof(description));
            if (currentCount < 0 || currentCount > int.MaxValue || targetCount < 1 || targetCount > int.MaxValue) throw new ArgumentException("Achievement counts must be bounded integral units.");
            if (unlockedAt < 0 || unlockedAt > 253402300799000 || (isUnlocked != (unlockedAt > 0))) throw new ArgumentException("Achievement completion requires a server timestamp.");
            Reward = reward ?? throw new ArgumentNullException(nameof(reward));
            if (isClaimed && (!isUnlocked || reward.Kind == AchievementRewardKind.None)) throw new ArgumentException("Claimed achievements require completed reward eligibility.");
            AchievementId = achievementId; DisplayName = displayName; Description = description; CurrentCount = currentCount; TargetCount = targetCount; IsUnlocked = isUnlocked; IsClaimed = isClaimed; UnlockedAt = unlockedAt;
        }
        internal static void ValidateId(string value)
        { if (value == null || !Regex.IsMatch(value, @"\A[A-Za-z0-9_.:-]{1,128}\z", RegexOptions.CultureInvariant)) throw new ArgumentException("Achievement content requires bounded stable IDs.", nameof(value)); }
    }
    public sealed class AchievementSnapshot
    {
        public IReadOnlyDictionary<string, AchievementEntry> Entries { get; }
        public bool RequiresWallet { get; }
        public bool RequiresInventory { get; }
        public AchievementSnapshot(IEnumerable<AchievementEntry> entries, bool requiresInventory = false)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var values = new Dictionary<string, AchievementEntry>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry == null) throw new ArgumentException("Achievement snapshot contains a null entry.", nameof(entries));
                values.Add(entry.AchievementId, entry);
                RequiresWallet |= entry.Reward.Kind == AchievementRewardKind.Currency;
                requiresInventory |= entry.Reward.Kind == AchievementRewardKind.Cosmetic;
            }
            if (values.Count > 1024) throw new ArgumentException("Achievement content exceeds 1024 entries.", nameof(entries));
            Entries = new ReadOnlyDictionary<string, AchievementEntry>(values); RequiresInventory = requiresInventory;
        }
    }
    public sealed class AchievementState
    {
        public AchievementSnapshot Achievements { get; }
        public WalletSnapshot Wallet { get; }
        public InventorySnapshot Inventory { get; }
        public string ClaimedAchievementId { get; }
        public AchievementState(AchievementSnapshot achievements, WalletSnapshot wallet = null, InventorySnapshot inventory = null, string claimedAchievementId = null)
        {
            Achievements = achievements ?? throw new ArgumentNullException(nameof(achievements));
            if (achievements.RequiresWallet && wallet == null) throw new ArgumentException("Currency achievement rewards require a confirmed wallet snapshot.", nameof(wallet));
            if (achievements.RequiresInventory && inventory == null) throw new ArgumentException("Collection/cosmetic achievements require a confirmed Inventory snapshot.", nameof(inventory));
            if (claimedAchievementId != null)
            {
                AchievementEntry.ValidateId(claimedAchievementId);
                if (!achievements.Entries.TryGetValue(claimedAchievementId, out var claimed) || !claimed.IsClaimed) throw new ArgumentException("Claim result must confirm the requested achievement.", nameof(claimedAchievementId));
            }
            Wallet = wallet; Inventory = inventory; ClaimedAchievementId = claimedAchievementId;
        }
    }
    public enum AchievementErrorCode { Unavailable, Rejected, AlreadyClaimed, Misconfigured, InvalidResponse, UnknownOutcome, RefreshRequired, Busy, Cancelled }
    public sealed class AchievementError
    {
        public AchievementErrorCode Code { get; }
        public string Message { get; }
        public AchievementError(AchievementErrorCode code, string message) { if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Achievement diagnostic required.", nameof(message)); Code = code; Message = message; }
    }
}
