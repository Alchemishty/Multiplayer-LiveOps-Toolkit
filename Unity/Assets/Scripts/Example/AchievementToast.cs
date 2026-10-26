using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Achievements;
using UnityEngine;

public class AchievementToast : MonoBehaviour
{
    private readonly Queue<AchievementEntry> pending = new Queue<AchievementEntry>();
    private IAchievementService service;
    private GameObject activePopup;
    private bool isShowing;
    public void Bind(IAchievementService achievementService)
    {
        if (achievementService == null) throw new ArgumentNullException(nameof(achievementService));
        Unsubscribe(); Clear(); service = achievementService;
        if (isActiveAndEnabled) Subscribe();
    }
    private void OnEnable() { if (service != null) Subscribe(); }
    private void Subscribe() { service.AchievementUnlocked += OnUnlocked; service.StateChanged += OnStateChanged; }
    private void Unsubscribe() { if (service != null) { service.AchievementUnlocked -= OnUnlocked; service.StateChanged -= OnStateChanged; } }
    private void OnStateChanged() { if (!service.HasSnapshot) Clear(); }
    private void OnUnlocked(string id)
    {
        if (!service.HasSnapshot) return; // Terminal teardown can precede another subscriber's notification.
        if (!service.Snapshot.Entries.TryGetValue(id, out var entry)) throw new InvalidOperationException("Confirmed achievement notification has no definition: " + id);
        pending.Enqueue(entry);
        if (!isShowing) StartCoroutine(ShowPending());
    }
    private IEnumerator ShowPending()
    {
        isShowing = true;
        try
        {
            while (pending.Count > 0)
            {
                var canvas = GetComponentInParent<Canvas>() ?? throw new InvalidOperationException("AchievementToast requires a Canvas.");
                activePopup = AchievementToastBuilder.Build(pending.Dequeue(), canvas.transform);
                yield return new WaitForSecondsRealtime(3);
                if (activePopup != null) Destroy(activePopup);
                activePopup = null;
            }
        }
        finally { isShowing = false; }
    }
    private void Clear()
    {
        StopAllCoroutines(); pending.Clear(); isShowing = false;
        if (activePopup != null) { activePopup.transform.SetParent(null, false); Destroy(activePopup); activePopup = null; }
    }
    private void OnDisable() { Unsubscribe(); Clear(); }
}
