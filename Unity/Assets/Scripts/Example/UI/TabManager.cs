using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class TabManager : MonoBehaviour
{
    public static TabManager Instance { get; private set; }

    [SerializeField] private List<Button> tabButtons = new();
    [SerializeField] private List<GameObject> tabPanels = new();

    private int activeIndex = -1;

    private readonly Color activeColor = new Color(0.3f, 0.5f, 0.8f);
    private readonly Color inactiveColor = new Color(0.2f, 0.2f, 0.25f);

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        for (int i = 0; i < tabButtons.Count; i++)
        {
            int index = i;
            tabButtons[i].onClick.AddListener(() => ShowTab(index));
        }

        if (tabPanels.Count > 0)
            ShowTab(0);
    }

    public void ShowTab(int index)
    {
        if (index < 0 || index >= tabPanels.Count) return;

        activeIndex = index;

        for (int i = 0; i < tabPanels.Count; i++)
        {
            if (tabPanels[i] != null)
                tabPanels[i].SetActive(i == index);
        }

        for (int i = 0; i < tabButtons.Count; i++)
        {
            var img = tabButtons[i].GetComponent<Image>();
            if (img != null)
                img.color = (i == index) ? activeColor : inactiveColor;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
