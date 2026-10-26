using System;
using System.Collections;
namespace MultiplayerLiveOpsToolkit.DailyRewards
{
    public interface IDailyRewardService
    {
        DailyRewardSnapshot Snapshot { get; }
        bool HasSnapshot { get; }
        bool IsBusy { get; }
        bool RequiresRefresh { get; }
        event Action StateChanged;
        IEnumerator Refresh(Action onSuccess, Action<DailyRewardError> onFailure);
        IEnumerator ClaimToday(Action onSuccess, Action<DailyRewardError> onFailure);
    }
}
