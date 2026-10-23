using UnityEngine;
using System;
using System.Collections;

public class AchievementServiceSubscriber
{
    private readonly MonoBehaviour owner;
    private readonly Action<string> onAchievementUnlocked;
    private bool isSubscribed;
    private Coroutine waitCoroutine;

    public AchievementServiceSubscriber(MonoBehaviour owner, Action<string> onAchievementUnlocked)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.onAchievementUnlocked = onAchievementUnlocked
            ?? throw new ArgumentNullException(nameof(onAchievementUnlocked));
    }

    public void Subscribe()
    {
        if (TrySubscribeNow())
            return;
        waitCoroutine = owner.StartCoroutine(WaitAndSubscribe());
    }

    public void Unsubscribe()
    {
        if (waitCoroutine != null)
        {
            owner.StopCoroutine(waitCoroutine);
            waitCoroutine = null;
        }

        if (!isSubscribed) return;
        if (AchievementService.Instance != null)
            AchievementService.Instance.AchievementUnlocked -= onAchievementUnlocked;
        isSubscribed = false;
    }

    private bool TrySubscribeNow()
    {
        if (isSubscribed || AchievementService.Instance == null)
            return false;
        AchievementService.Instance.AchievementUnlocked += onAchievementUnlocked;
        isSubscribed = true;
        return true;
    }

    private IEnumerator WaitAndSubscribe()
    {
        while (AchievementService.Instance == null)
            yield return null;
        TrySubscribeNow();
        waitCoroutine = null;
    }
}
