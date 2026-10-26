using UnityEngine;
using MultiplayerLiveOpsToolkit.Achievements;
using UnityEngine.UI;
using TMPro;

public static class AchievementToastBuilder
{
    public static GameObject Build(AchievementEntry definition, Transform canvasTransform)
    {
        if (definition == null)
            throw new System.ArgumentNullException(nameof(definition));
        if (canvasTransform == null)
            throw new System.ArgumentNullException(nameof(canvasTransform));

        var toastObject = new GameObject("AchievementToastPopup");
        toastObject.transform.SetParent(canvasTransform, false);
        toastObject.transform.SetAsLastSibling();

        var rectTransform = toastObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(1f, 1f);
        rectTransform.anchorMax = new Vector2(1f, 1f);
        rectTransform.pivot = new Vector2(1f, 1f);
        rectTransform.anchoredPosition = new Vector2(-8, -48);
        rectTransform.sizeDelta = new Vector2(260, 52);

        var background = toastObject.AddComponent<Image>();
        background.color = new Color(0.12f, 0.12f, 0.17f, 0.96f);

        var horizontalLayout = toastObject.AddComponent<HorizontalLayoutGroup>();
        horizontalLayout.spacing = 8;
        horizontalLayout.padding = new RectOffset(10, 12, 6, 6);
        horizontalLayout.childAlignment = TextAnchor.MiddleLeft;
        horizontalLayout.childControlWidth = true;
        horizontalLayout.childControlHeight = true;
        horizontalLayout.childForceExpandWidth = false;
        horizontalLayout.childForceExpandHeight = true;

        CreateIconElement(toastObject.transform);
        CreateTextElements(toastObject.transform, definition);

        return toastObject;
    }

    private static void CreateIconElement(Transform parent)
    {
        var iconObject = new GameObject("Icon");
        iconObject.transform.SetParent(parent, false);
        iconObject.AddComponent<RectTransform>();
        var iconLayout = iconObject.AddComponent<LayoutElement>();
        iconLayout.preferredWidth = 24;
        iconLayout.preferredHeight = 24;
        var iconImage = iconObject.AddComponent<Image>();
        iconImage.color = new Color(1f, 0.82f, 0.25f);
    }

    private static void CreateTextElements(Transform parent, AchievementEntry definition)
    {
        var textContainer = new GameObject("TextContainer");
        textContainer.transform.SetParent(parent, false);
        textContainer.AddComponent<RectTransform>();
        var textContainerLayout = textContainer.AddComponent<LayoutElement>();
        textContainerLayout.flexibleWidth = 1;

        var verticalLayout = textContainer.AddComponent<VerticalLayoutGroup>();
        verticalLayout.spacing = 1;
        verticalLayout.childControlWidth = true;
        verticalLayout.childControlHeight = true;
        verticalLayout.childForceExpandWidth = true;
        verticalLayout.childForceExpandHeight = false;

        var titleObject = new GameObject("Title");
        titleObject.transform.SetParent(textContainer.transform, false);
        titleObject.AddComponent<RectTransform>();
        var titleText = titleObject.AddComponent<TextMeshProUGUI>();
        titleText.text = "Achievement unlocked";
        titleText.fontSize = 10;
        titleText.color = new Color(1f, 0.82f, 0.25f);
        titleText.alignment = TextAlignmentOptions.MidlineLeft;

        var nameObject = new GameObject("Name");
        nameObject.transform.SetParent(textContainer.transform, false);
        nameObject.AddComponent<RectTransform>();
        var nameText = nameObject.AddComponent<TextMeshProUGUI>();
        nameText.text = !string.IsNullOrEmpty(definition.DisplayName)
            ? definition.DisplayName
            : definition.AchievementId;
        nameText.richText = false;
        nameText.fontSize = 13;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = new Color(0.9f, 0.9f, 0.95f);
        nameText.alignment = TextAlignmentOptions.MidlineLeft;
    }
}
