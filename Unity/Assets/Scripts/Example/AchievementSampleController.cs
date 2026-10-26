using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Achievements;
using UnityEngine;

/// <summary>Durable sample claim/read owner. Confirmed gameplay actions request reads, never submit counts.</summary>
public sealed class AchievementSampleController : MonoBehaviour
{
    [SerializeField] private AchievementsPanel panel;
    [SerializeField] private AchievementToast toast;
    [SerializeField] private ShopSampleController shopSample;
    [UnityEngine.Serialization.FormerlySerializedAs("dailyPanel")]
    [SerializeField] private DailyRewardsSampleController dailyRewardsSample;
    private IAchievementService service;
    private IAuthenticatedSession session;
    private readonly HashSet<IEnumerator> operations = new HashSet<IEnumerator>();
    private int lifetimeVersion;
    private bool isRefreshRunning;
    private bool isRefreshQueued;
    public void Bind(IAchievementService achievementService, IAuthenticatedSession authenticatedSession)
    {
        if (service != null && service.IsBusy) throw new InvalidOperationException("Cannot rebind achievements during account work.");
        service = achievementService ?? throw new ArgumentNullException(nameof(achievementService));
        session = authenticatedSession ?? throw new ArgumentNullException(nameof(authenticatedSession));
        if (panel != null) panel.Bind(service);
        if (toast != null) toast.Bind(service);
    }
    private void OnEnable()
    {
        if (panel != null) { panel.ClaimRequested += ClaimAchievement; panel.RefreshRequested += RefreshAchievements; }
        if (shopSample != null) shopSample.PurchaseConfirmed += RefreshAchievements;
        if (dailyRewardsSample != null) dailyRewardsSample.AttendanceConfirmed += RefreshAchievements;
    }
    public void ClaimAchievement(string id)
    {
        if (service == null) throw new InvalidOperationException("Bind Achievements before requesting a claim.");
        if (!isActiveAndEnabled || !session.IsActive) return; // Stale UI cannot start work after owner teardown.
        int requestedLifetime = lifetimeVersion;
        var operation = service.Claim(id, () => { }, HandleFailure);
        if (TryOwn(operation, requestedLifetime)) StartCoroutine(RunOwned(operation));
    }
    private bool TryOwn(IEnumerator operation, int requestedLifetime)
    {
        if (operation == null) throw new InvalidOperationException("Achievement service returned no operation.");
        if (requestedLifetime != lifetimeVersion || !isActiveAndEnabled || !session.IsActive)
        {
            (operation as IDisposable)?.Dispose();
            return false;
        }
        operations.Add(operation);
        return true;
    }
    private IEnumerator RunOwned(IEnumerator operation)
    {
        try { yield return operation; }
        finally { operations.Remove(operation); (operation as IDisposable)?.Dispose(); }
    }
    public void RefreshAchievements()
    {
        if (service == null) throw new InvalidOperationException("Bind Achievements before requesting a refresh.");
        if (!isActiveAndEnabled || !session.IsActive) return;
        isRefreshQueued = true;
        if (!isRefreshRunning) StartCoroutine(RefreshWhenAvailable());
    }
    private IEnumerator RefreshWhenAvailable()
    {
        isRefreshRunning = true;
        try
        {
            while (isRefreshQueued && session.IsActive)
            {
                isRefreshQueued = false;
                while (service.IsBusy && session.IsActive) yield return null;
                if (!session.IsActive) yield break;
                int requestedLifetime = lifetimeVersion;
                var operation = service.Refresh(() => { }, HandleFailure);
                if (!TryOwn(operation, requestedLifetime)) yield break;
                yield return RunOwned(operation);
            }
        }
        finally { isRefreshRunning = false; }
    }
    private void HandleFailure(AchievementError error)
    {
        if (error.Code == AchievementErrorCode.Misconfigured || error.Code == AchievementErrorCode.Unavailable || error.Code == AchievementErrorCode.InvalidResponse)
            session.RejectSession(session.SessionVersion, error.Message);
        else if (error.Code != AchievementErrorCode.Cancelled && panel != null) panel.ShowError(error.Message);
    }
    private void OnDisable()
    {
        lifetimeVersion++;
        if (panel != null) { panel.ClaimRequested -= ClaimAchievement; panel.RefreshRequested -= RefreshAchievements; }
        if (shopSample != null) shopSample.PurchaseConfirmed -= RefreshAchievements;
        if (dailyRewardsSample != null) dailyRewardsSample.AttendanceConfirmed -= RefreshAchievements;
        foreach (var operation in new List<IEnumerator>(operations)) { try { (operation as IDisposable)?.Dispose(); } catch (Exception exception) { Debug.LogException(exception); } }
        operations.Clear(); StopAllCoroutines(); isRefreshRunning = false; isRefreshQueued = false;
    }
}
