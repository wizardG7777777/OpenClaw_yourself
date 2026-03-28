using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour, IUIPanel
{
    public static InventoryUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject inventoryPanel;

    [Header("Item List")]
    [SerializeField] private Transform itemListContent;
    [SerializeField] private GameObject itemPrefab;

    [Header("Status")]
    [SerializeField] private Text titleText;

    public bool IsExclusive => false;
    public bool IsOpen => inventoryPanel != null && inventoryPanel.activeSelf;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.tabKey.wasPressedThisFrame)
        {
            if (IsOpen)
                ClosePanel();
            else if (UIManager.Instance != null)
                UIManager.Instance.RequestOpen(this);
            else
                Open();
            return;
        }

        if (IsOpen && keyboard.escapeKey.wasPressedThisFrame)
        {
            ClosePanel();
        }
    }

    public void Open()
    {
        if (inventoryPanel != null)
            inventoryPanel.SetActive(true);

        RefreshItems();
    }

    public void Close()
    {
        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);

        if (UIManager.Instance != null)
            UIManager.Instance.NotifyClose(this);
    }

    private void ClosePanel() => Close();

    private void RefreshItems()
    {
        if (itemListContent == null) return;

        // Clear existing items
        for (int i = itemListContent.childCount - 1; i >= 0; i--)
            Destroy(itemListContent.GetChild(i).gameObject);

        if (ItemRegistry.Instance == null) return;

        foreach (Item item in ItemRegistry.Instance.GetAll())
        {
            if (itemPrefab != null)
            {
                GameObject entry = Instantiate(itemPrefab, itemListContent);
                Text entryText = entry.GetComponentInChildren<Text>();
                if (entryText != null)
                    entryText.text = $"{item.displayName}  x{item.quantity}\n<size=12><color=#AAAAAA>{item.description}</color></size>";
            }
            else
            {
                GameObject entry = new GameObject("InventoryItem");
                entry.transform.SetParent(itemListContent, false);
                Text text = entry.AddComponent<Text>();
                text.text = $"{item.displayName}  x{item.quantity}\n{item.description}";
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 16;
                text.color = Color.white;
                text.supportRichText = true;

                var layout = entry.AddComponent<LayoutElement>();
                layout.minHeight = 50;
            }
        }
    }
}
