using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;
using MCP.Gateway;

public class CharacterCreationUI : MonoBehaviour
{
    public static CharacterCreationUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject creationPanel;

    [Header("Input Fields")]
    [SerializeField] private InputField nameInput;
    [SerializeField] private Dropdown relationshipDropdown;
    [SerializeField] private InputField personalityInput;
    [SerializeField] private InputField appearanceInput;
    [SerializeField] private InputField backstoryInput;
    [SerializeField] private InputField voiceStyleInput;

    [Header("Buttons")]
    [SerializeField] private Button createButton;
    [SerializeField] private Button closeButton;

    [Header("Character List")]
    [SerializeField] private GameObject characterListPanel;
    [SerializeField] private Transform characterListContent;
    [SerializeField] private GameObject characterItemPrefab;

    [Header("Status")]
    [SerializeField] private Text statusText;

    [Header("NPC Spawning")]
    [SerializeField] private GameObject npcPrefab;
    [SerializeField] private Transform defaultSpawnPoint;

    [Header("Toggle")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F2;

    private MCPGateway _gateway;
    private string _editingCharacterId;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (creationPanel != null)
            creationPanel.SetActive(false);
    }

    private void Start()
    {
        _gateway = FindAnyObjectByType<MCPGateway>();

        if (relationshipDropdown != null)
        {
            relationshipDropdown.ClearOptions();
            relationshipDropdown.AddOptions(new System.Collections.Generic.List<string>
            {
                "亲人", "朋友", "宠物", "其他"
            });
        }

        if (createButton != null)
            createButton.onClick.AddListener(OnCreateClicked);

        if (closeButton != null)
            closeButton.onClick.AddListener(ClosePanel);
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            if (creationPanel != null)
            {
                if (creationPanel.activeSelf)
                    ClosePanel();
                else
                    OpenPanel();
            }
        }
    }

    public void OpenPanel()
    {
        if (creationPanel != null)
            creationPanel.SetActive(true);

        ClearFields();

        if (statusText != null)
            statusText.text = "";

        _editingCharacterId = null;

        if (createButton != null)
        {
            var btnText = createButton.GetComponentInChildren<Text>();
            if (btnText != null)
                btnText.text = "创建";
        }

        RefreshCharacterList();
    }

    public void ClosePanel()
    {
        if (creationPanel != null)
            creationPanel.SetActive(false);
    }

    public void OpenForEdit(string characterId)
    {
        OpenPanel();

        NpcController npc = NpcRegistry.Instance.GetByCharacterId(characterId);
        if (npc == null)
        {
            if (statusText != null)
                statusText.text = "未找到角色：" + characterId;
            return;
        }

        _editingCharacterId = characterId;

        if (nameInput != null)
            nameInput.text = npc.displayName;

        if (createButton != null)
        {
            var btnText = createButton.GetComponentInChildren<Text>();
            if (btnText != null)
                btnText.text = "保存";
        }
    }

    private void OnCreateClicked()
    {
        if (nameInput == null || string.IsNullOrEmpty(nameInput.text.Trim()))
        {
            if (statusText != null)
                statusText.text = "请输入角色名称";
            return;
        }

        string charName = nameInput.text.Trim();
        string relationship = relationshipDropdown != null
            ? relationshipDropdown.options[relationshipDropdown.value].text
            : "其他";
        string personality = personalityInput != null ? personalityInput.text : "";
        string appearance = appearanceInput != null ? appearanceInput.text : "";
        string backstory = backstoryInput != null ? backstoryInput.text : "";
        string voiceStyle = voiceStyleInput != null ? voiceStyleInput.text : "";

        JObject parameters = new JObject
        {
            ["name"] = charName,
            ["relationship"] = relationship,
            ["personality"] = personality,
            ["appearance"] = appearance,
            ["backstory"] = backstory,
            ["voice_style"] = voiceStyle
        };

        bool isConnected = _gateway != null && _gateway.IsBackendConnected;

        if (_editingCharacterId != null)
        {
            parameters["id"] = _editingCharacterId;

            if (isConnected)
            {
                if (statusText != null)
                    statusText.text = "正在更新...";
                _gateway.SendToBackend("update_character", parameters, OnUpdateResponse);
            }
            else
            {
                // Offline edit: update NPC directly
                NpcController npc = NpcRegistry.Instance.GetByCharacterId(_editingCharacterId);
                if (npc != null)
                {
                    npc.displayName = charName;
                }
                if (statusText != null)
                    statusText.text = "（离线模式）角色已更新";
                _editingCharacterId = null;
                RefreshCharacterList();
            }
        }
        else
        {
            if (isConnected)
            {
                if (statusText != null)
                    statusText.text = "正在创建...";
                _gateway.SendToBackend("create_character", parameters, OnCreateResponse);
            }
            else
            {
                // Offline fallback
                string localId = "local_" + System.Guid.NewGuid().ToString("N").Substring(0, 6);
                SpawnNpc(localId, charName);
                if (statusText != null)
                    statusText.text = "（离线模式）角色已创建";
                ClearFields();
                RefreshCharacterList();
            }
        }
    }

    private void OnCreateResponse(bool ok, JObject data)
    {
        if (ok)
        {
            string id = data["id"]?.ToString() ?? "unknown";
            string charName = nameInput != null ? nameInput.text.Trim() : "NPC";
            SpawnNpc(id, charName);

            if (statusText != null)
                statusText.text = "角色创建成功！";

            ClearFields();
            RefreshCharacterList();
        }
        else
        {
            string error = data?["error"]?.ToString() ?? "未知错误";
            if (statusText != null)
                statusText.text = "创建失败：" + error;
        }
    }

    private void OnUpdateResponse(bool ok, JObject data)
    {
        if (ok)
        {
            if (statusText != null)
                statusText.text = "角色已更新";
            _editingCharacterId = null;
            RefreshCharacterList();
        }
        else
        {
            string error = data?["error"]?.ToString() ?? "未知错误";
            if (statusText != null)
                statusText.text = "更新失败：" + error;
        }
    }

    public NpcController SpawnNpc(string characterId, string displayName)
    {
        Vector3 spawnPos = Vector3.zero;

        if (defaultSpawnPoint != null)
        {
            spawnPos = defaultSpawnPoint.position;
        }
        else
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                spawnPos = player.transform.position + player.transform.forward * 2f;
            }
        }

        GameObject npcObj;
        if (npcPrefab != null)
        {
            npcObj = Instantiate(npcPrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            npcObj = new GameObject("NPC_" + displayName);
            npcObj.transform.position = spawnPos;
            npcObj.AddComponent<NpcController>();
            npcObj.AddComponent<CapsuleCollider>();
        }

        NpcController npc = npcObj.GetComponent<NpcController>();
        if (npc == null)
            npc = npcObj.AddComponent<NpcController>();

        npc.characterId = characterId;
        npc.displayName = displayName;

        if (NpcRegistry.Instance != null)
            NpcRegistry.Instance.Register(npc);

        return npc;
    }

    public void RefreshCharacterList()
    {
        if (characterListContent == null) return;

        // Clear existing children
        for (int i = characterListContent.childCount - 1; i >= 0; i--)
        {
            Destroy(characterListContent.GetChild(i).gameObject);
        }

        if (NpcRegistry.Instance == null) return;

        var allNpcs = NpcRegistry.Instance.GetAll();
        foreach (NpcController npc in allNpcs)
        {
            string cId = npc.characterId;
            string dName = npc.displayName;

            if (characterItemPrefab != null)
            {
                GameObject item = Instantiate(characterItemPrefab, characterListContent);
                Text itemText = item.GetComponentInChildren<Text>();
                if (itemText != null)
                    itemText.text = dName + " (" + cId + ")";

                // Wire delete button if present
                Button[] buttons = item.GetComponentsInChildren<Button>();
                foreach (Button btn in buttons)
                {
                    if (btn.gameObject.name.Contains("Delete") || btn.gameObject.name.Contains("delete"))
                    {
                        string capturedId = cId;
                        btn.onClick.AddListener(() => DeleteCharacter(capturedId));
                    }
                    else if (btn.gameObject.name.Contains("Edit") || btn.gameObject.name.Contains("edit"))
                    {
                        string capturedId = cId;
                        btn.onClick.AddListener(() => OpenForEdit(capturedId));
                    }
                }
            }
            else
            {
                GameObject item = new GameObject("CharacterItem");
                item.transform.SetParent(characterListContent, false);
                Text text = item.AddComponent<Text>();
                text.text = dName + " (" + cId + ")";
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 14;
                text.color = Color.white;
            }
        }
    }

    public void DeleteCharacter(string characterId)
    {
        if (NpcRegistry.Instance == null) return;

        NpcController npc = NpcRegistry.Instance.GetByCharacterId(characterId);
        if (npc == null) return;

        NpcRegistry.Instance.Unregister(npc);
        Destroy(npc.gameObject);

        if (_gateway != null && _gateway.IsBackendConnected)
        {
            JObject parameters = new JObject { ["id"] = characterId };
            _gateway.SendToBackend("delete_character", parameters, (ok, data) => { });
        }

        RefreshCharacterList();

        if (statusText != null)
            statusText.text = "角色已删除";
    }

    private void ClearFields()
    {
        if (nameInput != null) nameInput.text = "";
        if (relationshipDropdown != null) relationshipDropdown.value = 0;
        if (personalityInput != null) personalityInput.text = "";
        if (appearanceInput != null) appearanceInput.text = "";
        if (backstoryInput != null) backstoryInput.text = "";
        if (voiceStyleInput != null) voiceStyleInput.text = "";
        _editingCharacterId = null;
    }
}
