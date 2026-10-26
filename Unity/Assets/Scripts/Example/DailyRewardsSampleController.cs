using MultiplayerLiveOpsToolkit.UnityIntegration;
using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.DailyRewards;
using UnityEngine;

/// <summary>Durable sample attendance owner independent of calendar visibility.</summary>
public sealed class DailyRewardsSampleController : MonoBehaviour
{
    [SerializeField] private DailyLoginPanel panel;
    private IDailyRewardService service;
    private IAuthenticatedSession session;
    private readonly HashSet<IEnumerator> operations = new HashSet<IEnumerator>();
    private int lifetimeVersion;
    private bool isRefreshRunning;
    private bool isRefreshQueued;
    public void Bind(IDailyRewardService dailyRewardService, IAuthenticatedSession authenticatedSession)
    {
        if (service != null && service.IsBusy) throw new InvalidOperationException("Cannot rebind daily rewards during account work.");
        service = dailyRewardService ?? throw new ArgumentNullException(nameof(dailyRewardService));
        session = authenticatedSession ?? throw new ArgumentNullException(nameof(authenticatedSession));
        if (panel != null) panel.Bind(service);
    }
    private void OnEnable()
    {
        if (panel != null) { panel.ClaimRequested += ClaimToday; panel.RefreshRequested += RefreshRewards; }
    }
    public event Action AttendanceConfirmed;
    public void ClaimToday()
    {
        if (service == null) throw new InvalidOperationException("Bind Daily Rewards before requesting a claim.");
        if (!isActiveAndEnabled || !session.IsActive) return; // Stale UI cannot start work after owner teardown.
        int requestedLifetime = lifetimeVersion;
        var operation = service.ClaimToday(NotifyAttendance, HandleFailure);
        if (TryOwn(operation, requestedLifetime)) StartCoroutine(RunOwned(operation));
    }
    private bool TryOwn(IEnumerator operation, int requestedLifetime)
    {
        if (operation == null) throw new InvalidOperationException("Daily Rewards service returned no operation.");
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
    public void RefreshRewards()
    {
        if (service == null) throw new InvalidOperationException("Bind Daily Rewards before requesting a refresh.");
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
    private void NotifyAttendance()
    {
        if (!isActiveAndEnabled || !session.IsActive) return; // A committed result still completes during context teardown.
        var subscribers = AttendanceConfirmed;
        if (subscribers != null) foreach (Action subscriber in subscribers.GetInvocationList()) { try { subscriber(); } catch (Exception exception) { Debug.LogException(exception); } }
    }
    private void HandleFailure(DailyRewardError error)
    {
        if (error.Code == DailyRewardErrorCode.Misconfigured || error.Code == DailyRewardErrorCode.Unavailable || error.Code == DailyRewardErrorCode.InvalidResponse)
            session.RejectSession(session.SessionVersion, error.Message);
        else if (error.Code != DailyRewardErrorCode.Cancelled && panel != null) panel.ShowError(error.Message);
    }
    private void OnDisable()
    {
        lifetimeVersion++;
        if (panel != null) { panel.ClaimRequested -= ClaimToday; panel.RefreshRequested -= RefreshRewards; }
        foreach (var operation in new List<IEnumerator>(operations)) { try { (operation as IDisposable)?.Dispose(); } catch (Exception exception) { Debug.LogException(exception); } }
        operations.Clear(); StopAllCoroutines(); isRefreshRunning = false; isRefreshQueued = false;
    }
}
