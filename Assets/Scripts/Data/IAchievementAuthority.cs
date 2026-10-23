public interface IAchievementAuthority
{
    void RequestUnlock(string achievementId, int currentCount,
        System.Action onConfirmed, System.Action<string> onRejected);
    void ReportProgress(string achievementId, int currentCount);
}
