using System;
using System.Collections.Generic;

public class AchievementProgressTracker
{
    private Dictionary<string, AchievementDefinition> definitionsById = new();
    private Dictionary<string, AchievementProgress> progressById = new();
    private readonly Dictionary<AchievementConditionType, IAchievementConditionEvaluator> evaluatorsByType = new();

    public void RegisterEvaluator(IAchievementConditionEvaluator evaluator)
    {
        if (evaluator == null)
            throw new ArgumentNullException(nameof(evaluator));
        evaluatorsByType[evaluator.ConditionType] = evaluator;
    }

    public void SetDefinitions(List<AchievementDefinition> definitions)
    {
        if (definitions == null)
            throw new ArgumentNullException(nameof(definitions));

        definitionsById.Clear();
        foreach (var definition in definitions)
        {
            if (definition == null)
                throw new ArgumentException(
                    "AchievementProgressTracker.SetDefinitions: list contains a null entry");
            definition.Validate();
            definitionsById[definition.achievementId] = definition;
        }

        foreach (var definition in definitions)
        {
            if (!progressById.ContainsKey(definition.achievementId))
            {
                progressById[definition.achievementId] = new AchievementProgress
                {
                    achievementId = definition.achievementId,
                    currentCount = 0,
                    isUnlocked = false,
                    unlockedAtTimestamp = 0,
                };
            }
        }
    }

    public void LoadProgress(List<AchievementProgress> progressList)
    {
        if (progressList == null)
            throw new ArgumentNullException(nameof(progressList));

        foreach (var progress in progressList)
        {
            if (definitionsById.ContainsKey(progress.achievementId))
            {
                progressById[progress.achievementId] = progress;
            }
        }
    }

    public List<string> ProcessEvent(GameEventType eventType, GameEventContext context)
    {
        var newlyUnlocked = new List<string>();

        foreach (var definition in definitionsById.Values)
        {
            if (definition.triggerEvent != eventType)
                continue;

            var progress = progressById[definition.achievementId];
            if (progress.isUnlocked)
                continue;

            if (!evaluatorsByType.TryGetValue(definition.conditionType, out var evaluator))
                continue;

            if (evaluator.Evaluate(definition, progress, context))
            {
                progress.isUnlocked = true;
                progress.unlockedAtTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                newlyUnlocked.Add(definition.achievementId);
            }
        }

        return newlyUnlocked;
    }

    public AchievementProgress GetProgress(string achievementId)
    {
        progressById.TryGetValue(achievementId, out var progress);
        return progress;
    }

    public List<AchievementProgress> GetAllProgress()
    {
        return new List<AchievementProgress>(progressById.Values);
    }

    public List<AchievementDefinition> GetAllDefinitions()
    {
        return new List<AchievementDefinition>(definitionsById.Values);
    }
}
