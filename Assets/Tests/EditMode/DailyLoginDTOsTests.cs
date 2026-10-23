using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json;

[TestFixture]
public class DailyLoginDTOsTests
{
    [Test]
    public void CalendarResponse_Deserialize_ParsesAllFields()
    {
        string json = @"{
            ""year"": 2026,
            ""month"": 10,
            ""today"": 17,
            ""days"": [
                {""day"": 1, ""reward"": {""currency"": ""gold"", ""amount"": 50}, ""is_claimed"": true},
                {""day"": 2, ""reward"": {""currency"": ""gold"", ""amount"": 50}, ""is_claimed"": false}
            ]
        }";

        var response = JsonConvert.DeserializeObject<DailyLoginCalendarResponse>(json);

        Assert.AreEqual(2026, response.year);
        Assert.AreEqual(10, response.month);
        Assert.AreEqual(17, response.today);
        Assert.AreEqual(2, response.days.Count);
    }

    [Test]
    public void DayEntry_Deserialize_ParsesClaimedDay()
    {
        string json = @"{""day"": 5, ""reward"": {""currency"": ""snowflakes"", ""amount"": 15}, ""is_claimed"": true}";

        var dayEntry = JsonConvert.DeserializeObject<DailyLoginDayEntry>(json);

        Assert.AreEqual(5, dayEntry.day);
        Assert.IsTrue(dayEntry.isClaimed);
        Assert.AreEqual("snowflakes", dayEntry.reward.currency);
        Assert.AreEqual(15, dayEntry.reward.amount);
    }

    [Test]
    public void DayEntry_Deserialize_ParsesUnclaimedDay()
    {
        string json = @"{""day"": 12, ""reward"": {""currency"": ""gold"", ""amount"": 100}, ""is_claimed"": false}";

        var dayEntry = JsonConvert.DeserializeObject<DailyLoginDayEntry>(json);

        Assert.AreEqual(12, dayEntry.day);
        Assert.IsFalse(dayEntry.isClaimed);
    }

    [Test]
    public void DayEntry_Deserialize_NullReward_SetsRewardToNull()
    {
        string json = @"{""day"": 3, ""reward"": null, ""is_claimed"": false}";

        var dayEntry = JsonConvert.DeserializeObject<DailyLoginDayEntry>(json);

        Assert.AreEqual(3, dayEntry.day);
        Assert.IsNull(dayEntry.reward);
    }

    [Test]
    public void ClaimResponse_Deserialize_ParsesAllFields()
    {
        string json = @"{""day"": 17, ""reward"": {""currency"": ""gold"", ""amount"": 50}, ""message"": ""Claimed 50 gold!""}";

        var response = JsonConvert.DeserializeObject<DailyLoginClaimResponse>(json);

        Assert.AreEqual(17, response.day);
        Assert.AreEqual("gold", response.reward.currency);
        Assert.AreEqual(50, response.reward.amount);
        Assert.AreEqual("Claimed 50 gold!", response.message);
    }

    [Test]
    public void RewardInfo_Deserialize_ParsesCurrencyAndAmount()
    {
        string json = @"{""currency"": ""gears"", ""amount"": 3}";

        var rewardInfo = JsonConvert.DeserializeObject<DailyLoginRewardInfo>(json);

        Assert.AreEqual("gears", rewardInfo.currency);
        Assert.AreEqual(3, rewardInfo.amount);
    }

    [Test]
    public void CalendarResponse_Deserialize_FullMonth_ParsesAllDays()
    {
        var days = new List<object>();
        for (int dayIndex = 1; dayIndex <= 31; dayIndex++)
        {
            days.Add(new { day = dayIndex, reward = new { currency = "gold", amount = 50 }, is_claimed = dayIndex <= 16 });
        }
        string json = JsonConvert.SerializeObject(new { year = 2026, month = 10, today = 17, days });

        var response = JsonConvert.DeserializeObject<DailyLoginCalendarResponse>(json);

        Assert.AreEqual(31, response.days.Count);
        Assert.IsTrue(response.days[0].isClaimed);
        Assert.IsTrue(response.days[15].isClaimed);
        Assert.IsFalse(response.days[16].isClaimed);
        Assert.IsFalse(response.days[30].isClaimed);
    }

    [Test]
    public void CalendarResponse_Deserialize_TodayZero_WhenViewingPastMonth()
    {
        string json = @"{""year"": 2025, ""month"": 1, ""today"": 0, ""days"": []}";

        var response = JsonConvert.DeserializeObject<DailyLoginCalendarResponse>(json);

        Assert.AreEqual(0, response.today);
        Assert.AreEqual(0, response.days.Count);
    }
}
