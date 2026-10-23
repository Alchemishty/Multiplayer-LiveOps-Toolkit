using System.Collections.Generic;
using Newtonsoft.Json;

[System.Serializable]
public class DailyLoginCalendarResponse
{
    [JsonProperty("year")] public int year;
    [JsonProperty("month")] public int month;
    [JsonProperty("today")] public int today;
    [JsonProperty("days")] public List<DailyLoginDayEntry> days;
}

[System.Serializable]
public class DailyLoginDayEntry
{
    [JsonProperty("day")] public int day;
    [JsonProperty("reward")] public DailyLoginRewardInfo reward;
    [JsonProperty("is_claimed")] public bool isClaimed;
}

[System.Serializable]
public class DailyLoginRewardInfo
{
    [JsonProperty("currency")] public string currency;
    [JsonProperty("amount")] public int amount;
}

[System.Serializable]
public class DailyLoginClaimResponse
{
    [JsonProperty("day")] public int day;
    [JsonProperty("reward")] public DailyLoginRewardInfo reward;
    [JsonProperty("message")] public string message;
}
