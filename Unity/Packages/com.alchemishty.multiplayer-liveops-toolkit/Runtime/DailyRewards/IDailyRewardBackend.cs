using System;
using System.Collections;
namespace MultiplayerLiveOpsToolkit.DailyRewards
{
    public interface IDailyRewardBackend
    {
        IEnumerator Fetch(Action<DailyRewardState> onSuccess, Action<DailyRewardError> onFailure);
        IEnumerator Claim(DateTime expectedDate, Action<DailyRewardState> onSuccess, Action<DailyRewardError> onFailure);
    }
}
