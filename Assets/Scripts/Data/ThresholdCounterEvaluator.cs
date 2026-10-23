public class ThresholdCounterEvaluator : IAchievementConditionEvaluator
{
    public AchievementConditionType ConditionType => AchievementConditionType.ThresholdCounter;

    public bool Evaluate(AchievementDefinition definition, AchievementProgress progress, GameEventContext context)
    {
        if (definition.triggerEvent == GameEventType.CurrencySpent)
        {
            if (definition.targetCurrency != null && definition.targetCurrency != context.currencyName)
                return false;
            progress.currentCount += context.amount;
        }
        else
        {
            progress.currentCount += 1;
        }

        return progress.currentCount >= definition.targetCount;
    }
}
