using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class AchievementsPanel : MonoBehaviour
{
    [SerializeField] private Transform contentContainer;
    [SerializeField] private TMP_Text statusText;

    private List<AchievementRowUI> achievementRows = new();
    private bool isPopulated;
    private AchievementServiceSubscriber serviceSubscriber;

    private void Awake()
    {
        serviceSubscriber = new AchievementServiceSubscriber(this, OnAchievementUnlocked);
    }

    private void OnEnable()
    {
        if (!isPopulated)
            StartCoroutine(WaitAndPopulate());
        else
        {
            serviceSubscriber.Subscribe();
            ClearRows();
            PopulateAchievements();
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        serviceSubscriber.Unsubscribe();
    }

    private void OnDestroy()
    {
        serviceSubscriber.Unsubscribe();
    }

    private IEnumerator WaitAndPopulate()
    {
        while (AchievementService.Instance == null || !AchievementService.Instance.IsLoaded())
            yield return null;

        if (!gameObject.activeInHierarchy)
            yield break;

        serviceSubscriber.Subscribe();
        PopulateAchievements();
        isPopulated = true;

        yield return null;
        if (contentContainer != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentContainer.GetComponent<RectTransform>());
        Canvas.ForceUpdateCanvases();
        yield return null;
        var scrollRect = GetComponent<ScrollRect>();
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 1f;
    }

    private void OnAchievementUnlocked(string achievementId)
    {
        ClearRows();
        PopulateAchievements();
    }

    private void ClearRows()
    {
        achievementRows.Clear();

        if (contentContainer != null)
        {
            for (int index = contentContainer.childCount - 1; index >= 0; index--)
                Destroy(contentContainer.GetChild(index).gameObject);
        }
    }

    private void PopulateAchievements()
    {
        var service = AchievementService.Instance;
        if (service == null) return;

        var definitions = service.GetAllDefinitions();
        if (definitions.Count == 0)
        {
            ShopUIFactory.CreateEmptyStateMessage("No achievements available.", contentContainer);
            return;
        }

        var unlockedDefinitions = new List<AchievementDefinition>();
        var inProgressDefinitions = new List<AchievementDefinition>();

        foreach (var definition in definitions)
        {
            var progress = service.GetProgress(definition.achievementId);
            if (progress != null && progress.isUnlocked)
                unlockedDefinitions.Add(definition);
            else
                inProgressDefinitions.Add(definition);
        }

        if (unlockedDefinitions.Count > 0)
        {
            ShopUIFactory.CreateSectionHeader("Unlocked", contentContainer);
            foreach (var definition in unlockedDefinitions)
                CreateAchievementRow(definition, service.GetProgress(definition.achievementId));
        }

        if (inProgressDefinitions.Count > 0)
        {
            ShopUIFactory.CreateSectionHeader("In Progress", contentContainer);
            foreach (var definition in inProgressDefinitions)
                CreateAchievementRow(definition, service.GetProgress(definition.achievementId));
        }

        if (unlockedDefinitions.Count == 0 && inProgressDefinitions.Count == 0)
            ShopUIFactory.CreateEmptyStateMessage("No achievements yet.", contentContainer);
    }

    private void CreateAchievementRow(AchievementDefinition definition, AchievementProgress progress)
    {
        var rowObject = ShopUIFactory.CreateUIObject(
            $"Achievement_{definition.achievementId}", contentContainer, stretch: true);
        var layout = rowObject.AddComponent<LayoutElement>();
        layout.minHeight = 50;
        layout.preferredHeight = 50;

        var rowLayout = rowObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 8;
        rowLayout.padding = new RectOffset(10, 10, 6, 6);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        var background = rowObject.AddComponent<Image>();
        bool isUnlocked = progress != null && progress.isUnlocked;
        background.color = isUnlocked
            ? new Color(0.15f, 0.2f, 0.15f, 0.9f)
            : new Color(0.12f, 0.12f, 0.16f, 0.8f);

        var iconObject = ShopUIFactory.CreateUIObject("Icon", rowObject.transform);
        var iconLayout = iconObject.AddComponent<LayoutElement>();
        iconLayout.preferredWidth = 32;
        iconLayout.minWidth = 32;
        iconLayout.flexibleWidth = 0;
        var iconImage = iconObject.AddComponent<Image>();
        iconImage.color = isUnlocked
            ? new Color(1f, 0.85f, 0.3f)
            : new Color(0.4f, 0.4f, 0.45f);

        var textContainer = ShopUIFactory.CreateUIObject("TextContainer", rowObject.transform);
        var textContainerLayout = textContainer.AddComponent<LayoutElement>();
        textContainerLayout.flexibleWidth = 1;
        var verticalLayout = textContainer.AddComponent<VerticalLayoutGroup>();
        verticalLayout.spacing = 1;
        verticalLayout.childControlWidth = true;
        verticalLayout.childControlHeight = true;
        verticalLayout.childForceExpandWidth = true;
        verticalLayout.childForceExpandHeight = false;

        var nameObject = ShopUIFactory.CreateUIObject("Name", textContainer.transform);
        var nameText = nameObject.AddComponent<TextMeshProUGUI>();
        nameText.text = !string.IsNullOrEmpty(definition.displayName)
            ? definition.displayName
            : definition.achievementId;
        nameText.fontSize = 14;
        nameText.fontStyle = isUnlocked ? FontStyles.Bold : FontStyles.Normal;
        nameText.color = isUnlocked ? Color.white : new Color(0.7f, 0.7f, 0.7f);
        nameText.alignment = TextAlignmentOptions.MidlineLeft;

        var descriptionObject = ShopUIFactory.CreateUIObject("Description", textContainer.transform);
        var descriptionText = descriptionObject.AddComponent<TextMeshProUGUI>();
        descriptionText.text = definition.description ?? "";
        descriptionText.fontSize = 11;
        descriptionText.color = new Color(0.6f, 0.6f, 0.65f);
        descriptionText.alignment = TextAlignmentOptions.MidlineLeft;

        var statusObject = ShopUIFactory.CreateUIObject("Status", rowObject.transform);
        var statusLayout = statusObject.AddComponent<LayoutElement>();
        statusLayout.preferredWidth = 80;
        statusLayout.minWidth = 80;
        statusLayout.flexibleWidth = 0;
        var achievementStatusText = statusObject.AddComponent<TextMeshProUGUI>();
        achievementStatusText.fontSize = 13;
        achievementStatusText.alignment = TextAlignmentOptions.MidlineRight;

        if (isUnlocked)
        {
            achievementStatusText.text = "Done";
            achievementStatusText.color = new Color(0.3f, 0.9f, 0.3f);
        }
        else
        {
            achievementStatusText.text = FormatProgress(definition, progress);
            achievementStatusText.color = new Color(0.7f, 0.7f, 0.7f);
        }

        achievementRows.Add(new AchievementRowUI
        {
            definition = definition,
            statusText = achievementStatusText,
            iconImage = iconImage,
            nameText = nameText,
            background = background,
        });
    }

    private string FormatProgress(AchievementDefinition definition, AchievementProgress progress)
    {
        int currentCount = progress?.currentCount ?? 0;

        switch (definition.conditionType)
        {
            case AchievementConditionType.ThresholdCounter:
                return $"{currentCount}/{definition.targetCount}";
            case AchievementConditionType.CollectionCompletion:
                return "Incomplete";
            case AchievementConditionType.OneTimeTrigger:
                return "Locked";
            default:
                return "";
        }
    }


    private struct AchievementRowUI
    {
        public AchievementDefinition definition;
        public TMP_Text statusText;
        public Image iconImage;
        public TMP_Text nameText;
        public Image background;
    }
}
