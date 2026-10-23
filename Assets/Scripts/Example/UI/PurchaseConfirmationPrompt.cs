using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using TMPro;

public class PurchaseConfirmationPrompt : MonoBehaviour
{
    private Action onConfirm;
    private Action onCancel;
    private readonly List<Button> createdButtons = new();

    public static PurchaseConfirmationPrompt Show(Transform parent, string itemName, Action onConfirm, Action onCancel)
    {
        if (parent == null)
            throw new ArgumentNullException(nameof(parent));

        var overlayObject = new GameObject("PurchaseConfirmation");
        overlayObject.transform.SetParent(parent, false);
        var prompt = overlayObject.AddComponent<PurchaseConfirmationPrompt>();
        prompt.onConfirm = onConfirm;
        prompt.onCancel = onCancel;
        prompt.BuildUI(itemName);
        return prompt;
    }

    private void BuildUI(string itemName)
    {
        var rectTransform = gameObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.sizeDelta = Vector2.zero;
        rectTransform.anchoredPosition = Vector2.zero;

        var background = gameObject.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.5f);

        var panelObject = CreateChild("Panel", transform);
        var panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(240, 100);
        var panelImage = panelObject.AddComponent<Image>();
        panelImage.color = new Color(0.12f, 0.12f, 0.17f, 0.98f);

        var verticalLayout = panelObject.AddComponent<VerticalLayoutGroup>();
        verticalLayout.spacing = 10;
        verticalLayout.padding = new RectOffset(16, 16, 14, 14);
        verticalLayout.childAlignment = TextAnchor.MiddleCenter;
        verticalLayout.childControlWidth = true;
        verticalLayout.childControlHeight = true;
        verticalLayout.childForceExpandWidth = true;
        verticalLayout.childForceExpandHeight = false;

        var questionObject = CreateChild("Question", panelObject.transform);
        var questionLayout = questionObject.AddComponent<LayoutElement>();
        questionLayout.preferredHeight = 24;
        var questionText = questionObject.AddComponent<TextMeshProUGUI>();
        questionText.text = $"Equip {itemName}?";
        questionText.fontSize = 14;
        questionText.color = new Color(0.9f, 0.9f, 0.95f);
        questionText.alignment = TextAlignmentOptions.Center;

        var buttonContainer = CreateChild("Buttons", panelObject.transform);
        var buttonContainerLayout = buttonContainer.AddComponent<LayoutElement>();
        buttonContainerLayout.preferredHeight = 30;
        var horizontalLayout = buttonContainer.AddComponent<HorizontalLayoutGroup>();
        horizontalLayout.spacing = 8;
        horizontalLayout.childAlignment = TextAnchor.MiddleCenter;
        horizontalLayout.childControlWidth = true;
        horizontalLayout.childControlHeight = true;
        horizontalLayout.childForceExpandWidth = true;
        horizontalLayout.childForceExpandHeight = true;

        CreateButton("Equip", new Color(0.18f, 0.45f, 0.28f), buttonContainer.transform, OnConfirmClicked);
        CreateButton("Skip", new Color(0.25f, 0.25f, 0.3f), buttonContainer.transform, OnCancelClicked);
    }

    private void CreateButton(string label, Color buttonColor, Transform parent, Action onClick)
    {
        var buttonObject = CreateChild($"Button_{label}", parent);
        var buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = buttonColor;
        var button = buttonObject.AddComponent<Button>();
        button.onClick.AddListener(() => onClick());
        createdButtons.Add(button);

        var textObject = CreateChild("Text", buttonObject.transform);
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        var buttonText = textObject.AddComponent<TextMeshProUGUI>();
        buttonText.text = label;
        buttonText.fontSize = 13;
        buttonText.color = Color.white;
        buttonText.alignment = TextAlignmentOptions.Center;
    }

    private void OnConfirmClicked()
    {
        onConfirm?.Invoke();
        Destroy(gameObject);
    }

    private void OnCancelClicked()
    {
        onCancel?.Invoke();
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        foreach (var button in createdButtons)
        {
            if (button != null)
                button.onClick.RemoveAllListeners();
        }
    }

    private static GameObject CreateChild(string name, Transform parent)
    {
        var childObject = new GameObject(name);
        childObject.AddComponent<RectTransform>();
        childObject.transform.SetParent(parent, false);
        return childObject;
    }
}
