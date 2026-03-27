using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;
using MCP.Gateway;

public class MemoryInputUI : MonoBehaviour
{
    public static MemoryInputUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject memoryPanel;

    [Header("Character Selection")]
    [SerializeField] private Dropdown characterDropdown;

    [Header("Memory Input")]
    [SerializeField] private InputField memoryContentInput;
    [SerializeField] private Slider importanceSlider;
    [SerializeField] private Text importanceValueText;

    [Header("Buttons")]
    [SerializeField] private Button submitButton;
    [SerializeField] private Button closeButton;

    [Header("Memory List")]
    [SerializeField] private ScrollRect memoryListScrollRect;
    [SerializeField] private Text memoryListText;

    [Header("Status")]
    [SerializeField] private Text statusText;

    [Header("Settings")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F3;

    private List<string> _characterIds = new List<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (memoryPanel != null)
            memoryPanel.SetActive(false);
    }

    private void Start()
    {
        if (submitButton != null)
            submitButton.onClick.AddListener(OnSubmitClicked);

        if (closeButton != null)
            closeButton.onClick.AddListener(ClosePanel);

        if (importanceSlider != null)
        {
            importanceSlider.minValue = 1;
            importanceSlider.maxValue = 10;
            importanceSlider.wholeNumbers = true;
            importanceSlider.value = 5;
            importanceSlider.onValueChanged.AddListener(OnImportanceChanged);
            OnImportanceChanged(importanceSlider.value);
        }

        if (characterDropdown != null)
            characterDropdown.onValueChanged.AddListener(OnCharacterSelected);
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            if (memoryPanel != null)
            {
                if (memoryPanel.activeSelf)
                    ClosePanel();
                else
                    OpenPanel();
            }
        }
    }

    // ─── Public API ───────────────────────────────────────────────

    public void OpenPanel()
    {
        if (memoryPanel != null)
            memoryPanel.SetActive(true);

        RefreshCharacterDropdown();

        if (memoryContentInput != null)
            memoryContentInput.text = string.Empty;

        if (importanceSlider != null)
            importanceSlider.value = 5;

        if (statusText != null)
            statusText.text = string.Empty;

        RefreshMemoryList();
    }

    public void OpenForCharacter(string characterId)
    {
        OpenPanel();

        int index = _characterIds.IndexOf(characterId);
        if (index >= 0 && characterDropdown != null)
        {
            characterDropdown.value = index;
        }
    }

    public void ClosePanel()
    {
        if (memoryPanel != null)
            memoryPanel.SetActive(false);
    }

    // ─── Character Dropdown ───────────────────────────────────────

    private void RefreshCharacterDropdown()
    {
        if (characterDropdown == null) return;

        characterDropdown.ClearOptions();
        _characterIds.Clear();

        if (NpcRegistry.Instance == null) return;

        var allNpcs = NpcRegistry.Instance.GetAll();
        if (allNpcs == null) return;

        var options = new List<string>();
        foreach (var npc in allNpcs)
        {
            options.Add($"{npc.displayName} ({npc.characterId})");
            _characterIds.Add(npc.characterId);
        }

        characterDropdown.AddOptions(options);
    }

    private void OnCharacterSelected(int index)
    {
        RefreshMemoryList();
    }

    // ─── Importance Slider ────────────────────────────────────────

    private void OnImportanceChanged(float value)
    {
        if (importanceValueText != null)
            importanceValueText.text = $"重要性: {(int)value}";
    }

    // ─── Submit ───────────────────────────────────────────────────

    private void OnSubmitClicked()
    {
        if (_characterIds.Count == 0) return;

        string selectedCharacterId = _characterIds[characterDropdown.value];

        if (memoryContentInput == null || string.IsNullOrEmpty(memoryContentInput.text.Trim()))
        {
            if (statusText != null)
                statusText.text = "请输入记忆内容";
            return;
        }

        var param = new JObject
        {
            ["character_id"] = selectedCharacterId,
            ["content"] = memoryContentInput.text.Trim(),
            ["importance"] = (int)importanceSlider.value
        };

        var gateway = FindAnyObjectByType<MCPGateway>();

        if (gateway != null && gateway.IsBackendConnected)
        {
            if (statusText != null)
                statusText.text = "正在提交...";

            gateway.SendToBackend("add_memory", param, OnSubmitResponse);
        }
        else
        {
            if (statusText != null)
                statusText.text = "（离线模式）记忆已本地记录";

            AppendLocalMemory(selectedCharacterId, memoryContentInput.text.Trim(), (int)importanceSlider.value);

            if (memoryContentInput != null)
                memoryContentInput.text = string.Empty;
        }
    }

    private void OnSubmitResponse(bool ok, JObject data)
    {
        if (ok)
        {
            if (statusText != null)
                statusText.text = "记忆已保存！";

            if (memoryContentInput != null)
                memoryContentInput.text = string.Empty;

            if (importanceSlider != null)
                importanceSlider.value = 5;

            RefreshMemoryList();
        }
        else
        {
            string errorMsg = data != null ? data.ToString() : "未知错误";
            if (statusText != null)
                statusText.text = "保存失败：" + errorMsg;
        }
    }

    // ─── Memory List ──────────────────────────────────────────────

    private void RefreshMemoryList()
    {
        if (_characterIds.Count == 0 || characterDropdown == null)
        {
            if (memoryListText != null)
                memoryListText.text = "请先选择角色";
            return;
        }

        string selectedCharacterId = _characterIds[characterDropdown.value];

        var gateway = FindAnyObjectByType<MCPGateway>();

        if (gateway != null && gateway.IsBackendConnected)
        {
            var param = new JObject
            {
                ["character_id"] = selectedCharacterId
            };
            gateway.SendToBackend("list_memories", param, OnMemoryListResponse);
        }
        else
        {
            if (memoryListText != null)
                memoryListText.text = "（离线模式）无法加载记忆列表";
        }
    }

    private void OnMemoryListResponse(bool ok, JObject data)
    {
        if (ok)
        {
            if (data == null || data["memories"] == null)
            {
                if (memoryListText != null)
                    memoryListText.text = "暂无记忆";
                return;
            }

            var memories = data["memories"] as JArray;
            if (memories == null || memories.Count == 0)
            {
                if (memoryListText != null)
                    memoryListText.text = "暂无记忆";
                return;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var mem in memories)
            {
                int importance = mem["importance"] != null ? (int)mem["importance"] : 0;
                string content = mem["content"] != null ? mem["content"].ToString() : "";
                sb.AppendLine($"[重要性:{importance}] {content}");
            }

            if (memoryListText != null)
                memoryListText.text = sb.ToString();
        }
        else
        {
            if (memoryListText != null)
                memoryListText.text = "加载失败";
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────

    private void AppendLocalMemory(string characterId, string content, int importance)
    {
        if (memoryListText == null) return;

        string entry = $"[重要性:{importance}] {content}";
        if (string.IsNullOrEmpty(memoryListText.text) || memoryListText.text.StartsWith("（离线模式）"))
            memoryListText.text = entry;
        else
            memoryListText.text += "\n" + entry;
    }
}
