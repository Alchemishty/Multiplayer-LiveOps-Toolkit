using System.Collections.Generic;
using Newtonsoft.Json;

[System.Serializable]
public class AchievementDefinitionDTO
{
    public string id;
    public string name;
    public string description;
    [JsonProperty("condition_type")]
    public string conditionType;
    [JsonProperty("trigger_event")]
    public string triggerEvent;
    [JsonProperty("target_count")]
    public int targetCount;
    [JsonProperty("target_slot")]
    public string targetSlot;
    [JsonProperty("target_currency")]
    public string targetCurrency;
}

[System.Serializable]
public class AchievementDefinitionResponse
{
    public List<AchievementDefinitionDTO> achievements;
}

[System.Serializable]
public class AchievementProgressDTO
{
    [JsonProperty("achievement_id")]
    public string achievementId;
    [JsonProperty("current_count")]
    public int currentCount;
    [JsonProperty("is_unlocked")]
    public bool isUnlocked;
    [JsonProperty("unlocked_at")]
    public long unlockedAt;
}

[System.Serializable]
public class AchievementProgressResponse
{
    public List<AchievementProgressDTO> progress;
}

[System.Serializable]
public class AchievementUnlockRequest
{
    [JsonProperty("achievement_id")]
    public string achievementId;
    [JsonProperty("current_count")]
    public int currentCount;
}

[System.Serializable]
public class AchievementProgressUpdateRequest
{
    [JsonProperty("achievement_id")]
    public string achievementId;
    [JsonProperty("current_count")]
    public int currentCount;
}
