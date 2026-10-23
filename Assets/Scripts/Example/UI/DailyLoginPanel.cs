using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class DailyLoginPanel : MonoBehaviour
{
    [SerializeField] private Transform calendarGrid;
    [SerializeField] private TMP_Text monthHeaderText;
    [SerializeField] private TMP_Text statusText;

    private DailyLoginCalendarResponse calendarData;
    private List<DayCellElements> dayCells = new();
    private bool isPopulated;
    private bool isClaiming;

    private static readonly Color claimedColor = new Color(0.2f, 0.6f, 0.3f, 0.9f);
    private static readonly Color claimableColor = new Color(0.8f, 0.55f, 0.1f, 0.9f);
    private static readonly Color missedColor = new Color(0.25f, 0.25f, 0.3f, 0.6f);
    private static readonly Color futureColor = new Color(0.15f, 0.15f, 0.2f, 0.5f);
    private static readonly Color emptyCellColor = new Color(0.1f, 0.1f, 0.12f, 0.3f);

    private void OnEnable()
    {
        if (!isPopulated)
            StartCoroutine(WaitAndPopulate());
        else
            StartCoroutine(RefreshCalendar());
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        isClaiming = false;
    }

    private void OnDestroy()
    {
        foreach (var cell in dayCells)
        {
            if (cell.button != null)
                cell.button.onClick.RemoveAllListeners();
        }
    }

    private IEnumerator WaitAndPopulate()
    {
        while (DevAuthBootstrap.Instance == null || !DevAuthBootstrap.Instance.IsAuthenticated)
            yield return null;

        if (!gameObject.activeInHierarchy)
            yield break;

        yield return FetchAndBuildCalendar();
        isPopulated = true;
    }

    private IEnumerator RefreshCalendar()
    {
        yield return FetchCalendarData();
        if (calendarData != null)
            UpdateAllCellStates();
    }

    private IEnumerator FetchCalendarData()
    {
        yield return DailyLoginAPI.Instance.GetCalendar(
            (DailyLoginCalendarResponse response) => calendarData = response,
            (string error) => SetStatus($"Failed to load calendar: {error}")
        );
    }

    private IEnumerator FetchAndBuildCalendar()
    {
        yield return FetchCalendarData();

        if (calendarData == null)
        {
            SetStatus("Failed to load daily login calendar.");
            yield break;
        }

        BuildCalendarGrid();
    }

    private void BuildCalendarGrid()
    {
        foreach (var cell in dayCells)
        {
            if (cell.rootObject != null)
                Destroy(cell.rootObject);
        }
        dayCells.Clear();

        if (monthHeaderText != null)
        {
            string monthName = new DateTime(calendarData.year, calendarData.month, 1).ToString("MMMM yyyy");
            monthHeaderText.text = monthName;
        }

        int firstDayOfWeek = (int)new DateTime(calendarData.year, calendarData.month, 1).DayOfWeek;
        int daysInMonth = calendarData.days.Count;

        for (int emptyIndex = 0; emptyIndex < firstDayOfWeek; emptyIndex++)
        {
            CreateEmptyCell();
        }

        foreach (var dayEntry in calendarData.days)
        {
            var cell = CreateDayCell(dayEntry);
            dayCells.Add(cell);
        }

        int totalCells = firstDayOfWeek + daysInMonth;
        int remainingCells = (7 - (totalCells % 7)) % 7;
        for (int trailingIndex = 0; trailingIndex < remainingCells; trailingIndex++)
        {
            CreateEmptyCell();
        }
    }

    private void CreateEmptyCell()
    {
        var cellObject = ShopUIFactory.CreateUIObject("EmptyCell", calendarGrid);
        var cellImage = cellObject.AddComponent<Image>();
        cellImage.color = emptyCellColor;
    }

    private DayCellElements CreateDayCell(DailyLoginDayEntry dayEntry)
    {
        var cellObject = ShopUIFactory.CreateUIObject($"Day_{dayEntry.day}", calendarGrid);
        var cellImage = cellObject.AddComponent<Image>();

        var verticalLayout = cellObject.AddComponent<VerticalLayoutGroup>();
        verticalLayout.spacing = 1;
        verticalLayout.padding = new RectOffset(2, 2, 3, 3);
        verticalLayout.childAlignment = TextAnchor.UpperCenter;
        verticalLayout.childControlWidth = true;
        verticalLayout.childControlHeight = true;
        verticalLayout.childForceExpandWidth = true;
        verticalLayout.childForceExpandHeight = false;

        var dayNumberObject = ShopUIFactory.CreateUIObject("DayNumber", cellObject.transform);
        var dayNumberLayout = dayNumberObject.AddComponent<LayoutElement>();
        dayNumberLayout.preferredHeight = 16;
        var dayNumberText = dayNumberObject.AddComponent<TextMeshProUGUI>();
        dayNumberText.text = dayEntry.day.ToString();
        dayNumberText.fontSize = 11;
        dayNumberText.fontStyle = FontStyles.Bold;
        dayNumberText.alignment = TextAlignmentOptions.Top;
        dayNumberText.color = Color.white;

        var rewardObject = ShopUIFactory.CreateUIObject("Reward", cellObject.transform);
        var rewardLayout = rewardObject.AddComponent<LayoutElement>();
        rewardLayout.flexibleHeight = 1;
        var rewardText = rewardObject.AddComponent<TextMeshProUGUI>();
        if (dayEntry.reward != null)
            rewardText.text = FormatRewardShort(dayEntry.reward);
        else
            rewardText.text = "";
        rewardText.fontSize = 9;
        rewardText.alignment = TextAlignmentOptions.Center;
        rewardText.color = new Color(1f, 0.85f, 0.3f);

        var statusLabelObject = ShopUIFactory.CreateUIObject("Status", cellObject.transform);
        var statusLabelLayout = statusLabelObject.AddComponent<LayoutElement>();
        statusLabelLayout.preferredHeight = 14;
        var statusLabelText = statusLabelObject.AddComponent<TextMeshProUGUI>();
        statusLabelText.fontSize = 8;
        statusLabelText.alignment = TextAlignmentOptions.Bottom;

        Button cellButton = null;
        bool isToday = dayEntry.day == calendarData.today;
        bool isClaimable = isToday && !dayEntry.isClaimed;

        if (isClaimable)
        {
            cellButton = cellObject.AddComponent<Button>();
            cellButton.targetGraphic = cellImage;
            int capturedDay = dayEntry.day;
            cellButton.onClick.AddListener(() => OnDayCellClicked(capturedDay));
        }

        var cell = new DayCellElements
        {
            rootObject = cellObject,
            backgroundImage = cellImage,
            dayNumberText = dayNumberText,
            rewardText = rewardText,
            statusLabelText = statusLabelText,
            button = cellButton,
            dayNumber = dayEntry.day,
        };

        ApplyCellState(cell, dayEntry);
        return cell;
    }

    private void ApplyCellState(DayCellElements cell, DailyLoginDayEntry dayEntry)
    {
        bool isToday = dayEntry.day == calendarData.today;
        bool isPast = calendarData.today > 0 && dayEntry.day < calendarData.today;
        bool isFuture = calendarData.today > 0 && dayEntry.day > calendarData.today;

        if (dayEntry.isClaimed)
        {
            cell.backgroundImage.color = claimedColor;
            cell.statusLabelText.text = "Done";
            cell.statusLabelText.color = Color.white;
        }
        else if (isToday)
        {
            cell.backgroundImage.color = claimableColor;
            cell.statusLabelText.text = "Claim";
            cell.statusLabelText.color = Color.white;
        }
        else if (isPast)
        {
            cell.backgroundImage.color = missedColor;
            cell.statusLabelText.text = "Missed";
            cell.statusLabelText.color = new Color(0.6f, 0.4f, 0.4f);
        }
        else if (isFuture)
        {
            cell.backgroundImage.color = futureColor;
            cell.statusLabelText.text = "";
            cell.statusLabelText.color = Color.grey;
        }
        else
        {
            cell.backgroundImage.color = futureColor;
            cell.statusLabelText.text = "";
        }
    }

    private void UpdateAllCellStates()
    {
        if (calendarData == null) return;

        var dayEntryByDay = new Dictionary<int, DailyLoginDayEntry>();
        foreach (var dayEntry in calendarData.days)
            dayEntryByDay[dayEntry.day] = dayEntry;

        foreach (var cell in dayCells)
        {
            if (dayEntryByDay.TryGetValue(cell.dayNumber, out var dayEntry))
                ApplyCellState(cell, dayEntry);
        }
    }

    private void OnDayCellClicked(int dayNumber)
    {
        if (isClaiming) return;
        StartCoroutine(ClaimReward(dayNumber));
    }

    private IEnumerator ClaimReward(int dayNumber)
    {
        isClaiming = true;
        SetStatus("Claiming...");

        try
        {
            bool isDone = false;
            yield return DailyLoginAPI.Instance.ClaimDailyReward(
                (DailyLoginClaimResponse claimResponse) =>
                {
                    SetStatus(claimResponse.message);
                    StartCoroutine(ClearStatusAfterDelay(3f));
                    MarkDayClaimed(claimResponse.day);

                    if (WalletDisplay.Instance != null)
                        StartCoroutine(WalletDisplay.Instance.Refresh());

                    AchievementService.Instance?.ReportEvent(GameEventType.Login, default);

                    isDone = true;
                },
                (string error) =>
                {
                    SetStatus(error);
                    StartCoroutine(ClearStatusAfterDelay(3f));
                    isDone = true;
                }
            );
            while (!isDone) yield return null;
        }
        finally
        {
            isClaiming = false;
        }
    }

    private void MarkDayClaimed(int dayNumber)
    {
        if (calendarData == null) return;

        foreach (var dayEntry in calendarData.days)
        {
            if (dayEntry.day == dayNumber)
            {
                dayEntry.isClaimed = true;
                break;
            }
        }

        foreach (var cell in dayCells)
        {
            if (cell.dayNumber == dayNumber)
            {
                if (cell.button != null)
                {
                    cell.button.onClick.RemoveAllListeners();
                    cell.button.interactable = false;
                }

                cell.backgroundImage.color = claimedColor;
                cell.statusLabelText.text = "Done";
                cell.statusLabelText.color = Color.white;
                break;
            }
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private IEnumerator ClearStatusAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SetStatus("");
    }

    private static string FormatRewardShort(DailyLoginRewardInfo rewardInfo)
    {
        string abbreviatedCurrency = rewardInfo.currency switch
        {
            "gold" => "g",
            "snowflakes" => "sf",
            "gears" => "gr",
            _ => rewardInfo.currency,
        };
        return $"{rewardInfo.amount}{abbreviatedCurrency}";
    }

    private struct DayCellElements
    {
        public GameObject rootObject;
        public Image backgroundImage;
        public TMP_Text dayNumberText;
        public TMP_Text rewardText;
        public TMP_Text statusLabelText;
        public Button button;
        public int dayNumber;
    }
}
