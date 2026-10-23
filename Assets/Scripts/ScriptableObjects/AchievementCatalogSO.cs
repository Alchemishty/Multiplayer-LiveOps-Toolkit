using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "AchievementCatalog", menuName = "LiveOps/Achievement Catalog")]
public class AchievementCatalogSO : ScriptableObject
{
    [SerializeField] private AchievementDefinition[] achievements;

    private Dictionary<string, AchievementDefinition> definitionsById;
    private bool isCacheInitialized;

    private void InitializeCache()
    {
        if (isCacheInitialized && definitionsById != null) return;

        definitionsById = new Dictionary<string, AchievementDefinition>();
        if (achievements != null)
        {
            foreach (var definition in achievements)
            {
                if (definition != null && !string.IsNullOrEmpty(definition.achievementId))
                    definitionsById[definition.achievementId] = definition;
            }
        }
        isCacheInitialized = true;
    }

    public AchievementDefinition GetDefinitionById(string achievementId)
    {
        if (string.IsNullOrEmpty(achievementId))
            return null;
        InitializeCache();
        definitionsById.TryGetValue(achievementId, out var definition);
        return definition;
    }

    public AchievementDefinition[] GetAllDefinitions()
    {
        return achievements ?? System.Array.Empty<AchievementDefinition>();
    }
}
