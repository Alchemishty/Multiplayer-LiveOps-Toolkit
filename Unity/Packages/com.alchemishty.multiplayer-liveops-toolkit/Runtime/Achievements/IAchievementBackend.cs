using System;
using System.Collections;
namespace MultiplayerLiveOpsToolkit.Achievements
{
    public interface IAchievementBackend
    {
        IEnumerator Fetch(Action<AchievementState> onSuccess, Action<AchievementError> onFailure);
        IEnumerator Claim(string achievementId, Action<AchievementState> onSuccess, Action<AchievementError> onFailure);
    }
}
