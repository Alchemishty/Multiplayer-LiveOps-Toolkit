public interface IAchievementConditionEvaluator
{
    AchievementConditionType ConditionType { get; }
    bool Evaluate(AchievementDefinition definition, AchievementProgress progress, GameEventContext context);
}
