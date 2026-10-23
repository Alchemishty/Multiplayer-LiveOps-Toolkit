public class OneTimeTriggerEvaluator : IAchievementConditionEvaluator
{
    public AchievementConditionType ConditionType => AchievementConditionType.OneTimeTrigger;

    public bool Evaluate(AchievementDefinition definition, AchievementProgress progress, GameEventContext context)
    {
        return true;
    }
}
