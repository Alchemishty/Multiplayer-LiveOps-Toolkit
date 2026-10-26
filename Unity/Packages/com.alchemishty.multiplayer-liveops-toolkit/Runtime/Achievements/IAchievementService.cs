using System;
using System.Collections;
namespace MultiplayerLiveOpsToolkit.Achievements
{
    public interface IAchievementService
    {
        bool HasSnapshot { get; }
        bool IsBusy { get; }
        bool RequiresRefresh { get; }
        AchievementSnapshot Snapshot { get; }
        event Action StateChanged;
        event Action<string> AchievementUnlocked;
        IEnumerator Refresh(Action onSuccess, Action<AchievementError> onFailure);
        IEnumerator Claim(string achievementId, Action onSuccess, Action<AchievementError> onFailure);
    }
}
