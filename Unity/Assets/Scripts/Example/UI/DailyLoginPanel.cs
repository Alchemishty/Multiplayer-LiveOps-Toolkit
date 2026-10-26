using System;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.DailyRewards;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Monthly TMP sample view; account operations belong to the durable sample owner.</summary>
public class DailyLoginPanel : MonoBehaviour
{
    [SerializeField] private Transform calendarGrid;
    [SerializeField] private TMP_Text monthHeaderText;
    [SerializeField] private TMP_Text statusText;
    private IDailyRewardService service;
    private readonly List<Button> dayButtons = new List<Button>();
    private GameObject refreshRow;
    private Button refreshButton;
    public event Action ClaimRequested;
    public event Action RefreshRequested;
    public void Bind(IDailyRewardService dailyRewardService)
    {
        if (dailyRewardService == null) throw new ArgumentNullException(nameof(dailyRewardService));
        if (isActiveAndEnabled && service != null) service.StateChanged -= Render;
        service = dailyRewardService;
        if (isActiveAndEnabled) { service.StateChanged += Render; Render(); }
    }
    private void OnEnable()
    {
        if (calendarGrid == null || monthHeaderText == null || statusText == null) throw new InvalidOperationException("DailyLoginPanel requires calendarGrid, monthHeaderText and statusText.");
        statusText.richText = false; monthHeaderText.richText = false;
        if (service != null) { service.StateChanged += Render; Render(); }
    }
    private void Render()
    {
        ClearCalendar(); statusText.text = ""; monthHeaderText.text = "";
        if (service == null || !service.HasSnapshot) return; // Initial/terminal context state.
        var calendar = service.Snapshot;
        monthHeaderText.text = calendar.Today.ToString("MMMM yyyy");
        int firstWeekday = (int)new DateTime(calendar.Today.Year, calendar.Today.Month, 1).DayOfWeek;
        for (int index = 0; index < firstWeekday; index++) ShopUIFactory.CreateUIObject("EmptyCell", calendarGrid);
        foreach (var day in calendar.Days)
        {
            var cell = ShopUIFactory.CreateUIObject("Day_" + day.Day, calendarGrid);
            var background = cell.AddComponent<Image>();
            bool isToday = day.Day == calendar.Today.Day;
            background.color = day.IsClaimed ? new Color(0.2f,0.6f,0.3f,0.9f) : isToday ? new Color(0.8f,0.55f,0.1f,0.9f) : new Color(0.15f,0.15f,0.2f,0.5f);
            var textObject = ShopUIFactory.CreateUIObject("DayLabel", cell.transform);
            var textRect = textObject.GetComponent<RectTransform>(); textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one; textRect.sizeDelta = Vector2.zero;
            var label = textObject.AddComponent<TextMeshProUGUI>(); label.richText = false; label.fontSize = 10; label.alignment = TextAlignmentOptions.Center;
            string reward = day.Reward == null ? "Attendance" : day.Reward.Amount + " " + day.Reward.CurrencyId;
            label.text = day.Day + "\n" + reward + "\n" + (day.IsClaimed ? "Done" : isToday ? "Claim" : day.Day < calendar.Today.Day ? "Missed" : "");
            var button = cell.AddComponent<Button>(); button.targetGraphic = background;
            button.interactable = isToday && calendar.CanClaimToday && !service.IsBusy && !service.RequiresRefresh;
            button.onClick.AddListener(OnClaimClicked); dayButtons.Add(button);
        }
        if (refreshRow == null)
        {
            var refresh = ShopUIFactory.CreateInventoryItemRow("Update daily reward calendar", transform);
            refreshRow = refresh.rootObject; refreshRow.name = "DailyRewardsRefresh"; refresh.buttonText.text = "Refresh";
            refreshRow.GetComponent<LayoutElement>().flexibleHeight = 0;
            refresh.buttonImage.color = new Color(0.2f, 0.4f, 0.7f);
            refreshButton = refresh.actionButton;
            foreach (var label in refreshRow.GetComponentsInChildren<TMP_Text>()) label.richText = false;
        }
        refreshRow.SetActive(true);
        refreshButton.interactable = !service.IsBusy;
        refreshButton.onClick.AddListener(OnRefreshClicked);
    }
    public void ShowError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Daily Rewards error required.", nameof(message));
        if (isActiveAndEnabled) statusText.text = message;
    }
    private void OnClaimClicked() => NotifyRequest(ClaimRequested, "claim");
    private void OnRefreshClicked() => NotifyRequest(RefreshRequested, "refresh");
    private static void NotifyRequest(Action subscribers, string command)
    {
        if (subscribers == null) throw new InvalidOperationException("DailyLoginPanel requires an owner for " + command + " requests.");
        foreach (Action subscriber in subscribers.GetInvocationList()) { try { subscriber(); } catch (Exception exception) { Debug.LogException(exception); } }
    }
    private void ClearCalendar()
    {
        foreach (var button in dayButtons) button.onClick.RemoveListener(OnClaimClicked);
        dayButtons.Clear();
        if (refreshButton != null) refreshButton.onClick.RemoveListener(OnRefreshClicked);
        if (refreshRow != null) refreshRow.SetActive(false);
        for (int index = calendarGrid.childCount - 1; index >= 0; index--) { var child = calendarGrid.GetChild(index); child.SetParent(null, false); Destroy(child.gameObject); }
    }
    private void OnDisable()
    {
        if (service != null) service.StateChanged -= Render;
        if (calendarGrid != null) ClearCalendar();
        if (statusText != null) statusText.text = "";
        if (monthHeaderText != null) monthHeaderText.text = "";
    }
}
