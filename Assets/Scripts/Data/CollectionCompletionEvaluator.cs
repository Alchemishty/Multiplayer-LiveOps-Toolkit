using System;

public class CollectionCompletionEvaluator : IAchievementConditionEvaluator
{
    public AchievementConditionType ConditionType => AchievementConditionType.CollectionCompletion;

    private readonly Func<AccessoryType, bool> collectionCompleteChecker;

    public CollectionCompletionEvaluator(Func<AccessoryType, bool> collectionCompleteChecker)
    {
        this.collectionCompleteChecker = collectionCompleteChecker
            ?? throw new ArgumentNullException(nameof(collectionCompleteChecker));
    }

    public bool Evaluate(AchievementDefinition definition, AchievementProgress progress, GameEventContext context)
    {
        return collectionCompleteChecker(definition.targetSlot);
    }
}
