using NUnit.Framework;

[TestFixture]
public class AchievementDefinitionTests
{
    [Test]
    public void Validate_NullId_ThrowsArgumentException()
    {
        var definition = new AchievementDefinition
        {
            achievementId = null,
            conditionType = AchievementConditionType.OneTimeTrigger,
            triggerEvent = GameEventType.Purchase,
        };
        Assert.Throws<System.ArgumentException>(() => definition.Validate());
    }

    [Test]
    public void Validate_EmptyId_ThrowsArgumentException()
    {
        var definition = new AchievementDefinition
        {
            achievementId = "",
            conditionType = AchievementConditionType.OneTimeTrigger,
            triggerEvent = GameEventType.Purchase,
        };
        Assert.Throws<System.ArgumentException>(() => definition.Validate());
    }

    [Test]
    public void Validate_ThresholdCounter_ZeroTarget_ThrowsArgumentException()
    {
        var definition = new AchievementDefinition
        {
            achievementId = "buy-5",
            conditionType = AchievementConditionType.ThresholdCounter,
            triggerEvent = GameEventType.Purchase,
            targetCount = 0,
        };
        Assert.Throws<System.ArgumentException>(() => definition.Validate());
    }

    [Test]
    public void Validate_ThresholdCounter_NegativeTarget_ThrowsArgumentException()
    {
        var definition = new AchievementDefinition
        {
            achievementId = "buy-neg",
            conditionType = AchievementConditionType.ThresholdCounter,
            triggerEvent = GameEventType.Purchase,
            targetCount = -1,
        };
        Assert.Throws<System.ArgumentException>(() => definition.Validate());
    }

    [Test]
    public void Validate_CollectionCompletion_NoneSlot_ThrowsArgumentException()
    {
        var definition = new AchievementDefinition
        {
            achievementId = "collect-none",
            conditionType = AchievementConditionType.CollectionCompletion,
            triggerEvent = GameEventType.Purchase,
            targetSlot = AccessoryType.None,
        };
        Assert.Throws<System.ArgumentException>(() => definition.Validate());
    }

    [Test]
    public void Validate_ValidOneTimeTrigger_DoesNotThrow()
    {
        var definition = new AchievementDefinition
        {
            achievementId = "first-purchase",
            conditionType = AchievementConditionType.OneTimeTrigger,
            triggerEvent = GameEventType.Purchase,
        };
        Assert.DoesNotThrow(() => definition.Validate());
    }

    [Test]
    public void Validate_ValidThresholdCounter_DoesNotThrow()
    {
        var definition = new AchievementDefinition
        {
            achievementId = "buy-5",
            conditionType = AchievementConditionType.ThresholdCounter,
            triggerEvent = GameEventType.Purchase,
            targetCount = 5,
        };
        Assert.DoesNotThrow(() => definition.Validate());
    }

    [Test]
    public void Validate_ValidCollectionCompletion_DoesNotThrow()
    {
        var definition = new AchievementDefinition
        {
            achievementId = "all-headwear",
            conditionType = AchievementConditionType.CollectionCompletion,
            triggerEvent = GameEventType.Purchase,
            targetSlot = AccessoryType.Headwear,
        };
        Assert.DoesNotThrow(() => definition.Validate());
    }
}
