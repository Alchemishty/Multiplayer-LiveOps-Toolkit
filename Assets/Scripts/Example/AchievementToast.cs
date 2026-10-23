using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class AchievementToast : MonoBehaviour
{
    public static AchievementToast Instance { get; private set; }

    private const float DISPLAY_DURATION = 3f;

    private readonly Queue<AchievementDefinition> pendingToasts = new();
    private bool isShowing;
    private AchievementServiceSubscriber serviceSubscriber;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        serviceSubscriber = new AchievementServiceSubscriber(this, OnAchievementUnlocked);
    }

    private void OnEnable()
    {
        serviceSubscriber.Subscribe();
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        serviceSubscriber.Unsubscribe();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnAchievementUnlocked(string achievementId)
    {
        if (AchievementService.Instance == null) return;

        var definitions = AchievementService.Instance.GetAllDefinitions();
        AchievementDefinition unlockedDefinition = null;
        foreach (var definition in definitions)
        {
            if (definition.achievementId == achievementId)
            {
                unlockedDefinition = definition;
                break;
            }
        }

        if (unlockedDefinition == null) return;

        pendingToasts.Enqueue(unlockedDefinition);
        if (!isShowing)
            StartCoroutine(ShowNextToast());
    }

    private IEnumerator ShowNextToast()
    {
        isShowing = true;
        try
        {
            while (pendingToasts.Count > 0)
            {
                var definition = pendingToasts.Dequeue();
                var canvas = GetComponentInParent<Canvas>();
                if (canvas == null) continue;

                var toastObject = AchievementToastBuilder.Build(definition, canvas.transform);

                yield return new WaitForSeconds(DISPLAY_DURATION);

                if (toastObject != null)
                    Destroy(toastObject);
            }
        }
        finally
        {
            isShowing = false;
        }
    }
}
