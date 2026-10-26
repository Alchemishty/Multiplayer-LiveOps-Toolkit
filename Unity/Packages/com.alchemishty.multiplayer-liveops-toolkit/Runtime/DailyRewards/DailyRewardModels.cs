using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MultiplayerLiveOpsToolkit.Wallet;

namespace MultiplayerLiveOpsToolkit.DailyRewards
{
    public sealed class DailyCurrencyReward
    {
        public string CurrencyId { get; }
        public long Amount { get; }
        public DailyCurrencyReward(string currencyId, long amount)
        {
            if (amount < 1 || amount > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(amount));
            var balance = new WalletBalance(currencyId, amount);
            CurrencyId = balance.CurrencyId; Amount = amount;
        }
    }
    public sealed class DailyRewardDay
    {
        public int Day { get; }
        public DailyCurrencyReward Reward { get; }
        public bool IsClaimed { get; }
        public DailyRewardDay(int day, DailyCurrencyReward reward, bool isClaimed)
        {
            if (day < 1 || day > 31) throw new ArgumentOutOfRangeException(nameof(day));
            Day = day; Reward = reward; IsClaimed = isClaimed;
        }
    }
    public sealed class DailyRewardSnapshot
    {
        public DateTime Today { get; }
        public IReadOnlyList<DailyRewardDay> Days { get; }
        public bool RequiresWallet { get; }
        public bool CanClaimToday => !Days[Today.Day - 1].IsClaimed;
        public DailyRewardSnapshot(DateTime today, IEnumerable<DailyRewardDay> days)
        {
            if (today != today.Date) throw new ArgumentException("Daily Rewards requires a backend calendar date without time.", nameof(today));
            if (days == null) throw new ArgumentNullException(nameof(days));
            var calendarDays = new List<DailyRewardDay>();
            foreach (var day in days)
            {
                if (day == null || day.Day != calendarDays.Count + 1) throw new ArgumentException("Daily Rewards requires every month day once in order.", nameof(days));
                calendarDays.Add(day); RequiresWallet |= day.Reward != null;
            }
            if (calendarDays.Count != DateTime.DaysInMonth(today.Year, today.Month)) throw new ArgumentException("Daily Rewards calendar must contain the complete backend month.", nameof(days));
            Today = today; Days = new ReadOnlyCollection<DailyRewardDay>(calendarDays);
        }
    }
    public sealed class DailyRewardState
    {
        public DailyRewardSnapshot Calendar { get; }
        public WalletSnapshot Wallet { get; }
        public DateTime? ClaimedDate { get; }
        public DailyRewardState(DailyRewardSnapshot calendar, WalletSnapshot wallet = null, DateTime? claimedDate = null)
        {
            Calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
            if (calendar.RequiresWallet && wallet == null) throw new ArgumentException("Currency daily rewards require a confirmed Wallet snapshot.", nameof(wallet));
            if (claimedDate.HasValue && (claimedDate.Value != calendar.Today || !calendar.Days[calendar.Today.Day - 1].IsClaimed)) throw new ArgumentException("Daily claim result must confirm attendance on its backend date.", nameof(claimedDate));
            Wallet = wallet; ClaimedDate = claimedDate;
        }
    }
    public enum DailyRewardErrorCode { Unavailable, Rejected, AlreadyClaimed, DayChanged, Misconfigured, InvalidResponse, UnknownOutcome, RefreshRequired, Busy, Cancelled }
    public sealed class DailyRewardError
    {
        public DailyRewardErrorCode Code { get; }
        public string Message { get; }
        public DailyRewardError(DailyRewardErrorCode code, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Daily Rewards diagnostic required.", nameof(message));
            Code = code; Message = message;
        }
    }
}
