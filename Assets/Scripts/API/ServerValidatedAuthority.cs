using UnityEngine;
using System;

public class ServerValidatedAuthority : MonoBehaviour, IAchievementAuthority
{
    public void RequestUnlock(string achievementId, int currentCount,
        Action onConfirmed, Action<string> onRejected)
    {
        ApiWriteQueue.Instance.Enqueue(
            (onSuccess, onError) => AchievementAPI.Instance.ReportUnlock(
                achievementId, currentCount, onSuccess, onError),
            $"AchievementUnlock:{achievementId}",
            onSuccess: () => onConfirmed?.Invoke(),
            onFinalFailure: (string error) => onRejected?.Invoke(error)
        );
    }

    public void ReportProgress(string achievementId, int currentCount)
    {
        ApiWriteQueue.Instance.Enqueue(
            (onSuccess, onError) => AchievementAPI.Instance.ReportProgressUpdate(
                achievementId, currentCount, onSuccess, onError),
            $"AchievementProgress:{achievementId}"
        );
    }
}
