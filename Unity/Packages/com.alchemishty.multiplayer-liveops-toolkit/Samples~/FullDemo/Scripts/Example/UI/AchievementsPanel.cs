using System;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Achievements;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Service-bound achievement view with no local definition, account or avatar discovery.</summary>
public class AchievementsPanel : MonoBehaviour
{
    [SerializeField] private Transform contentContainer;
    [SerializeField] private TMP_Text statusText;
    private readonly List<Row> rows = new List<Row>();
    private IAchievementService service;
    public event Action<string> ClaimRequested;
    public event Action RefreshRequested;
    public void Bind(IAchievementService achievementService)
    {
        if (achievementService == null) throw new ArgumentNullException(nameof(achievementService));
        if (isActiveAndEnabled && service != null) service.StateChanged -= Render;
        service = achievementService;
        if (isActiveAndEnabled) { service.StateChanged += Render; Render(); }
    }
    private void OnEnable()
    {
        if (contentContainer == null || statusText == null) throw new InvalidOperationException("AchievementsPanel requires contentContainer and statusText.");
        statusText.richText = false;
        if (service != null) { service.StateChanged += Render; Render(); }
    }
    private void Render()
    {
        ClearRows(); statusText.text = "";
        if (service == null || !service.HasSnapshot) return; // Initial or terminal account state.
        foreach (var entry in service.Snapshot.Entries.Values)
        {
            var elements = ShopUIFactory.CreateInventoryItemRow(entry.DisplayName + "\n" + entry.Description + RewardText(entry.Reward), contentContainer);
            elements.rootObject.name = "Achievement_" + entry.AchievementId;
            var layout = elements.rootObject.GetComponent<LayoutElement>(); layout.minHeight = 64; layout.preferredHeight = 64;
            foreach (var label in elements.rootObject.GetComponentsInChildren<TMP_Text>()) { label.richText = false; label.enableWordWrapping = true; }
            elements.buttonText.text = entry.IsClaimed ? "Claimed" : entry.CanClaim ? "Claim" : entry.IsUnlocked ? "Completed" : entry.CurrentCount + "/" + entry.TargetCount;
            elements.buttonImage.color = entry.IsClaimed || (entry.IsUnlocked && !entry.CanClaim)
                ? new Color(0.16f, 0.45f, 0.27f)
                : entry.CanClaim ? new Color(0.5f, 0.35f, 0.08f) : new Color(0.22f, 0.25f, 0.32f);
            elements.actionButton.interactable = entry.CanClaim && !service.IsBusy && !service.RequiresRefresh;
            string id = entry.AchievementId;
            UnityAction listener = () => OnClaimClicked(id);
            elements.actionButton.onClick.AddListener(listener); rows.Add(new Row { Button = elements.actionButton, Listener = listener });
        }
        if (service.Snapshot.Entries.Count == 0) ShopUIFactory.CreateEmptyStateMessage("No achievements available.", contentContainer);
        var refresh = ShopUIFactory.CreateInventoryItemRow(service.RequiresRefresh ? "Confirm account state before another claim" : "Update achievement progress", contentContainer);
        refresh.rootObject.name = "AchievementsRefresh"; refresh.buttonText.text = "Refresh"; refresh.actionButton.interactable = !service.IsBusy;
        refresh.actionButton.onClick.AddListener(OnRefreshClicked); rows.Add(new Row { Button = refresh.actionButton, Listener = OnRefreshClicked });
    }
    private static string RewardText(AchievementReward reward)
    {
        if (reward.Kind == AchievementRewardKind.None) return "";
        return reward.Kind == AchievementRewardKind.Currency ? "\nReward: " + reward.Amount + " " + reward.CurrencyId : "\nReward: cosmetic " + reward.ItemId;
    }
    public void ShowError(string message) { if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Achievement error message required.", nameof(message)); if (isActiveAndEnabled) statusText.text = message; }
    private void OnClaimClicked(string id)
    {
        var subscribers = ClaimRequested ?? throw new InvalidOperationException("AchievementsPanel requires an owner for claim requests.");
        foreach (Action<string> subscriber in subscribers.GetInvocationList()) { try { subscriber(id); } catch (Exception exception) { Debug.LogException(exception); } }
    }
    private void OnRefreshClicked()
    {
        var subscribers = RefreshRequested ?? throw new InvalidOperationException("AchievementsPanel requires an owner for refresh requests.");
        foreach (Action subscriber in subscribers.GetInvocationList()) { try { subscriber(); } catch (Exception exception) { Debug.LogException(exception); } }
    }
    private void ClearRows()
    {
        foreach (var row in rows) row.Button.onClick.RemoveListener(row.Listener);
        rows.Clear();
        for (int index = contentContainer.childCount - 1; index >= 0; index--) { var child = contentContainer.GetChild(index); child.SetParent(null, false); Destroy(child.gameObject); }
    }
    private void OnDisable()
    {
        if (service != null) service.StateChanged -= Render;
        if (contentContainer != null) ClearRows(); // Hierarchy references may already be destroyed during unload.
        if (statusText != null) statusText.text = "";
    }
    private sealed class Row { public Button Button; public UnityAction Listener; }
}
