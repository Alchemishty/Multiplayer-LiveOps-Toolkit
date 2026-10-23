[System.Serializable]
public class AchievementDefinition
{
    public string achievementId;
    public string displayName;
    public string description;
    public AchievementConditionType conditionType;
    public GameEventType triggerEvent;
    public int targetCount;
    public AccessoryType targetSlot;
    public string targetCurrency;
    public string iconName;

    public void Validate()
    {
        if (string.IsNullOrEmpty(achievementId))
            throw new System.ArgumentException(
                "AchievementDefinition: achievementId must not be null or empty");

        if (conditionType == AchievementConditionType.ThresholdCounter && targetCount <= 0)
            throw new System.ArgumentException(
                $"AchievementDefinition '{achievementId}': targetCount must be positive for ThresholdCounter");

        if (conditionType == AchievementConditionType.CollectionCompletion && targetSlot == AccessoryType.None)
            throw new System.ArgumentException(
                $"AchievementDefinition '{achievementId}': targetSlot must not be None for CollectionCompletion");
    }
}
