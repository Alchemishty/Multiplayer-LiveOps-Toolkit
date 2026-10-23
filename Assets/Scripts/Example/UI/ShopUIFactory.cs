using UnityEngine;
using UnityEngine.UI;
using TMPro;

public struct ItemRowElements
{
    public GameObject rootObject;
    public Button actionButton;
    public Image buttonImage;
    public TMP_Text buttonText;
    public TMP_Text costText;
}

public static class ShopUIFactory
{
    public static GameObject CreateUIObject(string name, Transform parent, bool stretch = false)
    {
        var uiObject = new GameObject(name);
        var rectTransform = uiObject.AddComponent<RectTransform>();
        uiObject.transform.SetParent(parent, false);
        if (stretch)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;
        }
        return uiObject;
    }

    public static GameObject CreateSectionHeader(string title, Transform container)
    {
        var headerObject = CreateUIObject($"Header_{title}", container, stretch: true);
        var layout = headerObject.AddComponent<LayoutElement>();
        layout.minHeight = 30;
        layout.preferredHeight = 30;

        var background = headerObject.AddComponent<Image>();
        background.color = new Color(0.2f, 0.25f, 0.35f, 0.9f);

        var textObject = CreateUIObject("Text", headerObject.transform, stretch: true);
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.offsetMin = new Vector2(10, 0);
        var headerText = textObject.AddComponent<TextMeshProUGUI>();
        headerText.text = title;
        headerText.fontSize = 16;
        headerText.fontStyle = FontStyles.Bold;
        headerText.color = new Color(0.7f, 0.8f, 1f);
        headerText.alignment = TextAlignmentOptions.MidlineLeft;

        return headerObject;
    }

    public static GameObject CreateEmptyStateMessage(string message, Transform container)
    {
        var containerObject = CreateUIObject("EmptyState", container, stretch: true);
        var containerLayout = containerObject.AddComponent<LayoutElement>();
        containerLayout.flexibleHeight = 1;
        containerLayout.minHeight = 200;

        var textObject = CreateUIObject("Text", containerObject.transform, stretch: true);
        var emptyStateText = textObject.AddComponent<TextMeshProUGUI>();
        emptyStateText.text = message;
        emptyStateText.fontSize = 16;
        emptyStateText.color = new Color(0.8f, 0.5f, 0.3f);
        emptyStateText.alignment = TextAlignmentOptions.Center;

        return containerObject;
    }

    public static ItemRowElements CreateShopItemRow(string displayName, Transform container)
    {
        var rowObject = CreateUIObject("ItemRow", container, stretch: true);
        var layout = rowObject.AddComponent<LayoutElement>();
        layout.minHeight = 40;
        layout.preferredHeight = 40;

        var rowLayout = rowObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 6;
        rowLayout.padding = new RectOffset(10, 10, 4, 4);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        var background = rowObject.AddComponent<Image>();
        background.color = new Color(0.12f, 0.12f, 0.16f, 0.8f);

        var nameObject = CreateUIObject("Name", rowObject.transform);
        var nameLayout = nameObject.AddComponent<LayoutElement>();
        nameLayout.flexibleWidth = 1;
        var nameText = nameObject.AddComponent<TextMeshProUGUI>();
        nameText.text = displayName;
        nameText.fontSize = 14;
        nameText.color = Color.white;
        nameText.alignment = TextAlignmentOptions.MidlineLeft;

        var costObject = CreateUIObject("Cost", rowObject.transform);
        var costLayout = costObject.AddComponent<LayoutElement>();
        costLayout.preferredWidth = 140;
        costLayout.flexibleWidth = 0;
        var costText = costObject.AddComponent<TextMeshProUGUI>();
        costText.fontSize = 12;
        costText.color = new Color(1f, 0.85f, 0.3f);
        costText.alignment = TextAlignmentOptions.MidlineRight;

        var buttonElements = CreateActionButton(rowObject.transform);

        return new ItemRowElements
        {
            rootObject = rowObject,
            actionButton = buttonElements.actionButton,
            buttonImage = buttonElements.buttonImage,
            buttonText = buttonElements.buttonText,
            costText = costText,
        };
    }

    public static ItemRowElements CreateShopSetRow(string displayName, Transform container)
    {
        var rowObject = CreateUIObject("SetRow", container, stretch: true);
        var rowLayout = rowObject.AddComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 2;
        rowLayout.padding = new RectOffset(10, 10, 6, 6);
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        var rowLayoutElement = rowObject.AddComponent<LayoutElement>();
        rowLayoutElement.minHeight = 60;

        var background = rowObject.AddComponent<Image>();
        background.color = new Color(0.14f, 0.14f, 0.18f, 0.9f);

        var headerRow = CreateUIObject("Header", rowObject.transform);
        var headerLayout = headerRow.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 6;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = true;
        var headerLayoutElement = headerRow.AddComponent<LayoutElement>();
        headerLayoutElement.minHeight = 36;
        headerLayoutElement.preferredHeight = 36;

        var nameObject = CreateUIObject("Name", headerRow.transform);
        var nameLayout = nameObject.AddComponent<LayoutElement>();
        nameLayout.flexibleWidth = 1;
        var nameText = nameObject.AddComponent<TextMeshProUGUI>();
        nameText.text = displayName;
        nameText.fontSize = 15;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = new Color(0.9f, 0.85f, 0.7f);
        nameText.alignment = TextAlignmentOptions.MidlineLeft;

        var costObject = CreateUIObject("Cost", headerRow.transform);
        var costLayout = costObject.AddComponent<LayoutElement>();
        costLayout.preferredWidth = 140;
        costLayout.flexibleWidth = 0;
        var costText = costObject.AddComponent<TextMeshProUGUI>();
        costText.fontSize = 12;
        costText.color = new Color(1f, 0.85f, 0.3f);
        costText.alignment = TextAlignmentOptions.MidlineRight;

        var buttonElements = CreateActionButton(headerRow.transform);

        return new ItemRowElements
        {
            rootObject = rowObject,
            actionButton = buttonElements.actionButton,
            buttonImage = buttonElements.buttonImage,
            buttonText = buttonElements.buttonText,
            costText = costText,
        };
    }

    public static ItemRowElements CreateInventoryItemRow(string displayName, Transform container)
    {
        var rowObject = CreateUIObject("ItemRow", container, stretch: true);
        var layout = rowObject.AddComponent<LayoutElement>();
        layout.minHeight = 40;
        layout.preferredHeight = 40;

        var rowLayout = rowObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 6;
        rowLayout.padding = new RectOffset(10, 10, 4, 4);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        var background = rowObject.AddComponent<Image>();
        background.color = new Color(0.12f, 0.12f, 0.16f, 0.8f);

        var nameObject = CreateUIObject("Name", rowObject.transform);
        var nameLayout = nameObject.AddComponent<LayoutElement>();
        nameLayout.flexibleWidth = 1;
        var nameText = nameObject.AddComponent<TextMeshProUGUI>();
        nameText.text = displayName;
        nameText.fontSize = 14;
        nameText.color = Color.white;
        nameText.alignment = TextAlignmentOptions.MidlineLeft;

        var buttonElements = CreateActionButton(rowObject.transform);

        return new ItemRowElements
        {
            rootObject = rowObject,
            actionButton = buttonElements.actionButton,
            buttonImage = buttonElements.buttonImage,
            buttonText = buttonElements.buttonText,
            costText = null,
        };
    }

    public static ItemRowElements CreateInventorySetRow(string displayName, Transform container)
    {
        var rowObject = CreateUIObject("SetRow", container, stretch: true);
        var layout = rowObject.AddComponent<LayoutElement>();
        layout.minHeight = 40;
        layout.preferredHeight = 40;

        var rowLayout = rowObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 6;
        rowLayout.padding = new RectOffset(10, 10, 4, 4);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        var background = rowObject.AddComponent<Image>();
        background.color = new Color(0.14f, 0.14f, 0.18f, 0.9f);

        var nameObject = CreateUIObject("Name", rowObject.transform);
        var nameLayout = nameObject.AddComponent<LayoutElement>();
        nameLayout.flexibleWidth = 1;
        var nameText = nameObject.AddComponent<TextMeshProUGUI>();
        nameText.text = displayName;
        nameText.fontSize = 14;
        nameText.color = new Color(0.9f, 0.85f, 0.7f);
        nameText.alignment = TextAlignmentOptions.MidlineLeft;

        var buttonElements = CreateActionButton(rowObject.transform);

        return new ItemRowElements
        {
            rootObject = rowObject,
            actionButton = buttonElements.actionButton,
            buttonImage = buttonElements.buttonImage,
            buttonText = buttonElements.buttonText,
            costText = null,
        };
    }

    private struct ActionButtonElements
    {
        public Button actionButton;
        public Image buttonImage;
        public TMP_Text buttonText;
    }

    private static ActionButtonElements CreateActionButton(Transform parent)
    {
        var buttonObject = CreateUIObject("Button", parent);
        var buttonLayout = buttonObject.AddComponent<LayoutElement>();
        buttonLayout.preferredWidth = 90;
        buttonLayout.minWidth = 90;
        buttonLayout.flexibleWidth = 0;
        var buttonImage = buttonObject.AddComponent<Image>();
        var button = buttonObject.AddComponent<Button>();

        var buttonTextObject = CreateUIObject("Text", buttonObject.transform);
        var buttonTextRect = buttonTextObject.GetComponent<RectTransform>();
        buttonTextRect.anchorMin = Vector2.zero;
        buttonTextRect.anchorMax = Vector2.one;
        buttonTextRect.sizeDelta = Vector2.zero;
        var buttonText = buttonTextObject.AddComponent<TextMeshProUGUI>();
        buttonText.fontSize = 13;
        buttonText.alignment = TextAlignmentOptions.Center;
        buttonText.color = Color.white;

        return new ActionButtonElements
        {
            actionButton = button,
            buttonImage = buttonImage,
            buttonText = buttonText,
        };
    }
}
