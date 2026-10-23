using NUnit.Framework;
using System.Collections.Generic;

[TestFixture]
public class AchievementProgressTrackerTests
{
    private AchievementProgressTracker tracker;

    [SetUp]
    public void SetUp()
    {
        tracker = new AchievementProgressTracker();
        tracker.RegisterEvaluator(new ThresholdCounterEvaluator());
        tracker.RegisterEvaluator(new OneTimeTriggerEvaluator());
    }

    private AchievementDefinition MakeThreshold(string id, GameEventType trigger, int target,
        string targetCurrency = null)
    {
        return new AchievementDefinition
        {
            achievementId = id,
            conditionType = AchievementConditionType.ThresholdCounter,
            triggerEvent = trigger,
            targetCount = target,
            targetCurrency = targetCurrency,
        };
    }

    private AchievementDefinition MakeOneTime(string id, GameEventType trigger)
    {
        return new AchievementDefinition
        {
            achievementId = id,
            conditionType = AchievementConditionType.OneTimeTrigger,
            triggerEvent = trigger,
        };
    }

    private AchievementDefinition MakeCollection(string id, GameEventType trigger, AccessoryType slot)
    {
        return new AchievementDefinition
        {
            achievementId = id,
            conditionType = AchievementConditionType.CollectionCompletion,
            triggerEvent = trigger,
            targetSlot = slot,
        };
    }

    [Test]
    public void ThresholdCounter_IncrementsByOne_ForPurchaseEvent()
    {
        tracker.SetDefinitions(new List<AchievementDefinition> { MakeThreshold("buy-3", GameEventType.Purchase, 3) });
        tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        Assert.AreEqual(1, tracker.GetProgress("buy-3").currentCount);
    }

    [Test]
    public void ThresholdCounter_UnlocksAtTarget()
    {
        tracker.SetDefinitions(new List<AchievementDefinition> { MakeThreshold("buy-2", GameEventType.Purchase, 2) });
        tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        var unlocked = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 2 });
        Assert.Contains("buy-2", unlocked);
        Assert.IsTrue(tracker.GetProgress("buy-2").isUnlocked);
    }

    [Test]
    public void ThresholdCounter_DoesNotDoubleUnlock()
    {
        tracker.SetDefinitions(new List<AchievementDefinition> { MakeThreshold("buy-1", GameEventType.Purchase, 1) });
        var first = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        var second = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 2 });
        Assert.AreEqual(1, first.Count);
        Assert.AreEqual(0, second.Count);
    }

    [Test]
    public void ThresholdCounter_CurrencySpent_IncrementsByAmount()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeThreshold("spend-1000", GameEventType.CurrencySpent, 1000, "gold") });
        tracker.ProcessEvent(GameEventType.CurrencySpent,
            new GameEventContext { currencyName = "gold", amount = 400 });
        Assert.AreEqual(400, tracker.GetProgress("spend-1000").currentCount);
    }

    [Test]
    public void ThresholdCounter_CurrencySpent_FiltersByCurrency()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeThreshold("spend-gold", GameEventType.CurrencySpent, 100, "gold") });
        tracker.ProcessEvent(GameEventType.CurrencySpent,
            new GameEventContext { currencyName = "gems", amount = 500 });
        Assert.AreEqual(0, tracker.GetProgress("spend-gold").currentCount);
    }

    [Test]
    public void ThresholdCounter_CurrencySpent_NullTargetCountsAllCurrencies()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeThreshold("spend-any", GameEventType.CurrencySpent, 100, null) });
        tracker.ProcessEvent(GameEventType.CurrencySpent,
            new GameEventContext { currencyName = "gold", amount = 60 });
        tracker.ProcessEvent(GameEventType.CurrencySpent,
            new GameEventContext { currencyName = "gems", amount = 50 });
        Assert.AreEqual(110, tracker.GetProgress("spend-any").currentCount);
        Assert.IsTrue(tracker.GetProgress("spend-any").isUnlocked);
    }

    [Test]
    public void ThresholdCounter_CurrencySpent_MultipleCurrenciesIndependent()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
        {
            MakeThreshold("spend-gold", GameEventType.CurrencySpent, 100, "gold"),
            MakeThreshold("spend-gems", GameEventType.CurrencySpent, 100, "gems"),
        });
        tracker.ProcessEvent(GameEventType.CurrencySpent,
            new GameEventContext { currencyName = "gold", amount = 100 });
        Assert.IsTrue(tracker.GetProgress("spend-gold").isUnlocked);
        Assert.IsFalse(tracker.GetProgress("spend-gems").isUnlocked);
    }

    [Test]
    public void OneTimeTrigger_UnlocksOnFirstEvent()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeOneTime("first-buy", GameEventType.Purchase) });
        var unlocked = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        Assert.Contains("first-buy", unlocked);
        Assert.IsTrue(tracker.GetProgress("first-buy").isUnlocked);
    }

    [Test]
    public void OneTimeTrigger_AlreadyUnlocked_NoRepeat()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeOneTime("first-buy", GameEventType.Purchase) });
        tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        var second = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 2 });
        Assert.AreEqual(0, second.Count);
    }

    [Test]
    public void CollectionCompletion_CheckerReturnsFalse_NoUnlock()
    {
        tracker.RegisterEvaluator(new CollectionCompletionEvaluator(slot => false));
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeCollection("all-headwear", GameEventType.Purchase, AccessoryType.Headwear) });
        var unlocked = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        Assert.AreEqual(0, unlocked.Count);
        Assert.IsFalse(tracker.GetProgress("all-headwear").isUnlocked);
    }

    [Test]
    public void CollectionCompletion_CheckerReturnsTrue_Unlocks()
    {
        tracker.RegisterEvaluator(new CollectionCompletionEvaluator(slot => slot == AccessoryType.Headwear));
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeCollection("all-headwear", GameEventType.Purchase, AccessoryType.Headwear) });
        var unlocked = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        Assert.Contains("all-headwear", unlocked);
        Assert.IsTrue(tracker.GetProgress("all-headwear").isUnlocked);
    }

    [Test]
    public void CollectionCompletion_AlreadyUnlocked_NoRepeat()
    {
        tracker.RegisterEvaluator(new CollectionCompletionEvaluator(slot => true));
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeCollection("all-headwear", GameEventType.Purchase, AccessoryType.Headwear) });
        tracker.ProcessEvent(GameEventType.Purchase, default);
        var second = tracker.ProcessEvent(GameEventType.Purchase, default);
        Assert.AreEqual(0, second.Count);
    }

    [Test]
    public void OneTimeTrigger_LoginEvent_UnlocksFirstLoginAchievement()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeOneTime("first-login", GameEventType.Login) });
        var unlocked = tracker.ProcessEvent(GameEventType.Login, default);
        Assert.Contains("first-login", unlocked);
        Assert.IsTrue(tracker.GetProgress("first-login").isUnlocked);
    }

    [Test]
    public void ProcessEvent_NonMatchingEventType_ReturnsEmpty()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeOneTime("first-buy", GameEventType.Purchase) });
        var unlocked = tracker.ProcessEvent(GameEventType.Login, default);
        Assert.AreEqual(0, unlocked.Count);
    }

    [Test]
    public void ProcessEvent_MultipleUnlocksFromOneEvent()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
        {
            MakeOneTime("first-buy", GameEventType.Purchase),
            MakeThreshold("buy-1", GameEventType.Purchase, 1),
        });
        var unlocked = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        Assert.AreEqual(2, unlocked.Count);
    }

    [Test]
    public void LoadProgress_RestoresState()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeThreshold("buy-5", GameEventType.Purchase, 5) });
        tracker.LoadProgress(new List<AchievementProgress>
        {
            new AchievementProgress { achievementId = "buy-5", currentCount = 3, isUnlocked = false }
        });
        Assert.AreEqual(3, tracker.GetProgress("buy-5").currentCount);
    }

    [Test]
    public void LoadProgress_AlreadyUnlocked_DoesNotReUnlock()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeThreshold("buy-1", GameEventType.Purchase, 1) });
        tracker.LoadProgress(new List<AchievementProgress>
        {
            new AchievementProgress { achievementId = "buy-1", currentCount = 1, isUnlocked = true, unlockedAtTimestamp = 1000 }
        });
        var unlocked = tracker.ProcessEvent(GameEventType.Purchase, new GameEventContext { itemId = 1 });
        Assert.AreEqual(0, unlocked.Count);
    }

    [Test]
    public void LoadProgress_UnknownAchievementId_Ignored()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>
            { MakeOneTime("real", GameEventType.Purchase) });
        Assert.DoesNotThrow(() => tracker.LoadProgress(new List<AchievementProgress>
        {
            new AchievementProgress { achievementId = "nonexistent", currentCount = 5 }
        }));
    }

    [Test]
    public void GetProgress_UnknownId_ReturnsNull()
    {
        tracker.SetDefinitions(new List<AchievementDefinition>());
        Assert.IsNull(tracker.GetProgress("nope"));
    }

    [Test]
    public void ProcessEvent_BeforeSetDefinitions_ReturnsEmpty()
    {
        var unlocked = tracker.ProcessEvent(GameEventType.Purchase, default);
        Assert.AreEqual(0, unlocked.Count);
    }

    [Test]
    public void SetDefinitions_NullEntryInList_ThrowsArgumentException()
    {
        Assert.Throws<System.ArgumentException>(() =>
            tracker.SetDefinitions(new List<AchievementDefinition> { null }));
    }
}
