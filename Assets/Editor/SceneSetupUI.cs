using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Editor tool to set up all UI elements for the NPC system.
/// Run via menu: Tools > Setup > UI Scene Setup
/// </summary>
public class SceneSetupUI : EditorWindow
{
    [MenuItem("Tools/Setup/UI Scene Setup")]
    public static void ShowWindow()
    {
        GetWindow<SceneSetupUI>("UI Scene Setup");
    }

    [MenuItem("Tools/Setup/Run Complete UI Setup")]
    public static void RunCompleteSetup()
    {
        SetupCompleteUI();
    }

    private void OnGUI()
    {
        GUILayout.Label("NPC System UI Setup", EditorStyles.boldLabel);
        GUILayout.Space(10);

        if (GUILayout.Button("Setup Complete UI", GUILayout.Height(40)))
        {
            SetupCompleteUI();
        }

        GUILayout.Space(10);
        GUILayout.Label("Individual Setup:", EditorStyles.boldLabel);

        if (GUILayout.Button("1. Create Canvas & EventSystem"))
        {
            CreateCanvasAndEventSystem();
        }

        if (GUILayout.Button("2. Setup Dialogue UI"))
        {
            SetupDialogueUI();
        }

        if (GUILayout.Button("3. Setup Character Creation UI"))
        {
            SetupCharacterCreationUI();
        }

        if (GUILayout.Button("4. Setup Memory Input UI"))
        {
            SetupMemoryInputUI();
        }
    }

    private static void SetupCompleteUI()
    {
        CreateCanvasAndEventSystem();
        SetupDialogueUI();
        SetupCharacterCreationUI();
        SetupMemoryInputUI();

        Debug.Log("[SceneSetupUI] Complete UI setup finished!");
        EditorUtility.DisplayDialog("Success", "UI Scene setup complete!", "OK");
    }

    #region Canvas & EventSystem

    private static void CreateCanvasAndEventSystem()
    {
        // Check if Canvas already exists
        Canvas existingCanvas = Object.FindFirstObjectByType<Canvas>();
        if (existingCanvas != null)
        {
            Debug.Log("[SceneSetupUI] Canvas already exists, skipping creation.");
        }
        else
        {
            // Create Canvas
            GameObject canvasGO = new GameObject("Canvas_UI");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            Undo.RegisterCreatedObjectUndo(canvasGO, "Create Canvas");
            Debug.Log("[SceneSetupUI] Created Canvas_UI");
        }

        // Check if EventSystem exists
        EventSystem existingEventSystem = Object.FindFirstObjectByType<EventSystem>();
        if (existingEventSystem != null)
        {
            EnsureInputSystemModule(existingEventSystem);
            Debug.Log("[SceneSetupUI] EventSystem already exists, skipping creation.");
        }
        else
        {
            // Create EventSystem
            GameObject eventSystemGO = new GameObject("EventSystem");
            eventSystemGO.AddComponent<EventSystem>();
            eventSystemGO.AddComponent<InputSystemUIInputModule>();

            Undo.RegisterCreatedObjectUndo(eventSystemGO, "Create EventSystem");
            Debug.Log("[SceneSetupUI] Created EventSystem");
        }
    }

    private static void EnsureInputSystemModule(EventSystem eventSystem)
    {
        if (eventSystem == null)
            return;

        GameObject eventSystemGO = eventSystem.gameObject;
        bool changed = false;

        StandaloneInputModule standaloneInput = eventSystemGO.GetComponent<StandaloneInputModule>();
        if (standaloneInput != null)
        {
            Undo.DestroyObjectImmediate(standaloneInput);
            changed = true;
        }

        if (eventSystemGO.GetComponent<InputSystemUIInputModule>() == null)
        {
            Undo.AddComponent<InputSystemUIInputModule>(eventSystemGO);
            changed = true;
        }

        if (changed)
        {
            EditorUtility.SetDirty(eventSystemGO);
            Debug.Log("[SceneSetupUI] EventSystem input module upgraded to InputSystemUIInputModule.");
        }
    }

    private static Canvas GetOrCreateCanvas()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            CreateCanvasAndEventSystem();
            canvas = Object.FindFirstObjectByType<Canvas>();
        }
        return canvas;
    }

    #endregion

    #region Dialogue UI

    private static void SetupDialogueUI()
    {
        Canvas canvas = GetOrCreateCanvas();

        // Check if already exists
        NpcDialogueUI existing = Object.FindFirstObjectByType<NpcDialogueUI>();
        if (existing != null)
        {
            Debug.Log("[SceneSetupUI] NpcDialogueUI already exists, skipping.");
            return;
        }

        // Create Dialogue Panel
        GameObject dialoguePanel = CreatePanel(canvas.transform, "DialoguePanel", new Color(0.1f, 0.1f, 0.1f, 0.95f));
        SetRectTransform(dialoguePanel.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
            new Vector2(600, 400), new Vector3(0, 20, 0));

        // NPC Name Text
        GameObject npcNameTextGO = CreateText(dialoguePanel.transform, "NpcNameText", "NPC Name", 24, FontStyle.Bold);
        SetRectTransform(npcNameTextGO.GetComponent<RectTransform>(),
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
            new Vector2(0, 40), new Vector3(0, -10, 0));

        // Dialogue Text
        GameObject dialogueTextGO = CreateText(dialoguePanel.transform, "DialogueText", "...", 18);
        SetRectTransform(dialogueTextGO.GetComponent<RectTransform>(),
            new Vector2(0, 0.5f), new Vector2(1, 1), new Vector2(0.5f, 1),
            new Vector2(-40, 200), new Vector3(20, -60, 0));

        // History ScrollView
        GameObject historyScrollView = CreateScrollView(dialoguePanel.transform, "HistoryScrollView");
        SetRectTransform(historyScrollView.GetComponent<RectTransform>(),
            new Vector2(0, 0.4f), new Vector2(1, 0.85f), new Vector2(0.5f, 0.5f),
            new Vector2(-40, 0), new Vector3(0, 0, 0));

        // Player Input Field
        GameObject inputFieldGO = CreateInputField(dialoguePanel.transform, "PlayerInputField", "输入对话...");
        SetRectTransform(inputFieldGO.GetComponent<RectTransform>(),
            new Vector2(0, 0), new Vector2(0.8f, 0.15f), new Vector2(0, 0.5f),
            new Vector2(-20, 50), new Vector3(10, 10, 0));

        // Send Button
        GameObject sendButtonGO = CreateButton(dialoguePanel.transform, "SendButton", "发送");
        SetRectTransform(sendButtonGO.GetComponent<RectTransform>(),
            new Vector2(0.82f, 0), new Vector2(1, 0.15f), new Vector2(0.5f, 0.5f),
            new Vector2(-20, 50), new Vector3(-10, 10, 0));

        // Create NpcDialogueUI component on Canvas
        NpcDialogueUI dialogueUI = canvas.gameObject.AddComponent<NpcDialogueUI>();

        // Assign references
        SerializedObject so = new SerializedObject(dialogueUI);
        so.FindProperty("dialoguePanel").objectReferenceValue = dialoguePanel;
        so.FindProperty("npcNameText").objectReferenceValue = npcNameTextGO.GetComponent<Text>();
        so.FindProperty("dialogueText").objectReferenceValue = dialogueTextGO.GetComponent<Text>();
        so.FindProperty("playerInputField").objectReferenceValue = inputFieldGO.GetComponent<InputField>();
        so.FindProperty("sendButton").objectReferenceValue = sendButtonGO.GetComponent<Button>();

        // Get ScrollRect from historyScrollView
        ScrollRect historyScrollRect = historyScrollView.GetComponent<ScrollRect>();
        so.FindProperty("historyScrollRect").objectReferenceValue = historyScrollRect;

        // Get history text from content
        Text historyText = historyScrollRect.content.GetComponentInChildren<Text>();
        so.FindProperty("historyText").objectReferenceValue = historyText;

        so.ApplyModifiedProperties();

        // Hide panel by default
        dialoguePanel.SetActive(false);

        Undo.RegisterCreatedObjectUndo(dialoguePanel, "Setup Dialogue UI");
        Debug.Log("[SceneSetupUI] Setup Dialogue UI complete");

        // Create Speech Bubble (World Space)
        GameObject speechBubble = CreatePanel(null, "SpeechBubble", new Color(0, 0, 0, 0.8f));
        speechBubble.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

        // Add Canvas component for world space rendering
        Canvas bubbleCanvas = speechBubble.AddComponent<Canvas>();
        bubbleCanvas.renderMode = RenderMode.WorldSpace;
        bubbleCanvas.sortingOrder = 100;

        // Resize
        RectTransform bubbleRT = speechBubble.GetComponent<RectTransform>();
        bubbleRT.sizeDelta = new Vector2(300, 100);

        // Add bubble text
        GameObject bubbleTextGO = CreateText(speechBubble.transform, "SpeechBubbleText", "...", 16);
        SetRectTransform(bubbleTextGO.GetComponent<RectTransform>(),
            new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
            new Vector2(-20, -20), Vector3.zero);

        // Assign speech bubble references
        so = new SerializedObject(dialogueUI);
        so.FindProperty("speechBubble").objectReferenceValue = speechBubble;
        so.FindProperty("speechBubbleText").objectReferenceValue = bubbleTextGO.GetComponent<Text>();
        so.ApplyModifiedProperties();

        speechBubble.SetActive(false);
        Undo.RegisterCreatedObjectUndo(speechBubble, "Create Speech Bubble");
    }

    #endregion

    #region Character Creation UI

    private static void SetupCharacterCreationUI()
    {
        Canvas canvas = GetOrCreateCanvas();

        // Check if already exists
        CharacterCreationUI existing = Object.FindFirstObjectByType<CharacterCreationUI>();
        if (existing != null)
        {
            Debug.Log("[SceneSetupUI] CharacterCreationUI already exists, skipping.");
            return;
        }

        // Create Creation Panel
        GameObject creationPanel = CreatePanel(canvas.transform, "CreationPanel", new Color(0.1f, 0.1f, 0.15f, 0.98f));
        SetRectTransform(creationPanel.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(700, 600), Vector3.zero);

        // Title
        GameObject titleGO = CreateText(creationPanel.transform, "Title", "创建角色 (F2)", 28, FontStyle.Bold);
        SetRectTransform(titleGO.GetComponent<RectTransform>(),
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
            new Vector2(0, 50), new Vector3(0, -10, 0));

        // Name Input
        GameObject nameLabel = CreateText(creationPanel.transform, "NameLabel", "名称:", 16);
        SetRectTransform(nameLabel.GetComponent<RectTransform>(),
            new Vector2(0.05f, 0.85f), new Vector2(0.2f, 0.9f), new Vector2(0, 0.5f),
            new Vector2(0, 30), Vector3.zero);

        GameObject nameInput = CreateInputField(creationPanel.transform, "NameInput", "输入角色名称...");
        SetRectTransform(nameInput.GetComponent<RectTransform>(),
            new Vector2(0.22f, 0.85f), new Vector2(0.6f, 0.9f), new Vector2(0, 0.5f),
            new Vector2(0, 40), Vector3.zero);

        // Relationship Dropdown
        GameObject relLabel = CreateText(creationPanel.transform, "RelLabel", "关系:", 16);
        SetRectTransform(relLabel.GetComponent<RectTransform>(),
            new Vector2(0.05f, 0.77f), new Vector2(0.2f, 0.82f), new Vector2(0, 0.5f),
            new Vector2(0, 30), Vector3.zero);

        GameObject relDropdown = CreateDropdown(creationPanel.transform, "RelationshipDropdown");
        SetRectTransform(relDropdown.GetComponent<RectTransform>(),
            new Vector2(0.22f, 0.77f), new Vector2(0.6f, 0.82f), new Vector2(0, 0.5f),
            new Vector2(0, 40), Vector3.zero);

        // Personality Input
        GameObject persLabel = CreateText(creationPanel.transform, "PersLabel", "性格:", 16);
        SetRectTransform(persLabel.GetComponent<RectTransform>(),
            new Vector2(0.05f, 0.6f), new Vector2(0.2f, 0.65f), new Vector2(0, 0.5f),
            new Vector2(0, 80), Vector3.zero);

        GameObject persInput = CreateInputField(creationPanel.transform, "PersonalityInput", "描述角色性格...", true);
        SetRectTransform(persInput.GetComponent<RectTransform>(),
            new Vector2(0.22f, 0.55f), new Vector2(0.95f, 0.7f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 0), Vector3.zero);

        // Appearance Input
        GameObject appLabel = CreateText(creationPanel.transform, "AppLabel", "外貌:", 16);
        SetRectTransform(appLabel.GetComponent<RectTransform>(),
            new Vector2(0.05f, 0.42f), new Vector2(0.2f, 0.47f), new Vector2(0, 0.5f),
            new Vector2(0, 80), Vector3.zero);

        GameObject appInput = CreateInputField(creationPanel.transform, "AppearanceInput", "描述角色外貌...", true);
        SetRectTransform(appInput.GetComponent<RectTransform>(),
            new Vector2(0.22f, 0.37f), new Vector2(0.95f, 0.52f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 0), Vector3.zero);

        // Backstory Input
        GameObject storyLabel = CreateText(creationPanel.transform, "StoryLabel", "背景:", 16);
        SetRectTransform(storyLabel.GetComponent<RectTransform>(),
            new Vector2(0.05f, 0.24f), new Vector2(0.2f, 0.29f), new Vector2(0, 0.5f),
            new Vector2(0, 80), Vector3.zero);

        GameObject storyInput = CreateInputField(creationPanel.transform, "BackstoryInput", "描述角色背景故事...", true);
        SetRectTransform(storyInput.GetComponent<RectTransform>(),
            new Vector2(0.22f, 0.19f), new Vector2(0.95f, 0.34f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 0), Vector3.zero);

        // Voice Style Input
        GameObject voiceLabel = CreateText(creationPanel.transform, "VoiceLabel", "说话风格:", 16);
        SetRectTransform(voiceLabel.GetComponent<RectTransform>(),
            new Vector2(0.05f, 0.1f), new Vector2(0.2f, 0.15f), new Vector2(0, 0.5f),
            new Vector2(0, 30), Vector3.zero);

        GameObject voiceInput = CreateInputField(creationPanel.transform, "VoiceStyleInput", "例如：温柔、幽默、严肃...");
        SetRectTransform(voiceInput.GetComponent<RectTransform>(),
            new Vector2(0.22f, 0.1f), new Vector2(0.6f, 0.15f), new Vector2(0, 0.5f),
            new Vector2(0, 40), Vector3.zero);

        // Buttons
        GameObject createButton = CreateButton(creationPanel.transform, "CreateButton", "创建");
        SetRectTransform(createButton.GetComponent<RectTransform>(),
            new Vector2(0.65f, 0.02f), new Vector2(0.8f, 0.08f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 40), Vector3.zero);

        GameObject closeButton = CreateButton(creationPanel.transform, "CloseButton", "关闭");
        SetRectTransform(closeButton.GetComponent<RectTransform>(),
            new Vector2(0.82f, 0.02f), new Vector2(0.97f, 0.08f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 40), Vector3.zero);

        // Character List Panel (Right side)
        GameObject listPanel = CreatePanel(creationPanel.transform, "CharacterListPanel", new Color(0.15f, 0.15f, 0.2f, 0.9f));
        SetRectTransform(listPanel.GetComponent<RectTransform>(),
            new Vector2(0.62f, 0.55f), new Vector2(0.98f, 0.9f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 0), Vector3.zero);

        GameObject listTitle = CreateText(listPanel.transform, "ListTitle", "角色列表", 18, FontStyle.Bold);
        SetRectTransform(listTitle.GetComponent<RectTransform>(),
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
            new Vector2(0, 30), new Vector3(0, -5, 0));

        // ScrollView for character list
        GameObject listScrollView = CreateScrollView(listPanel.transform, "CharacterListScrollView");
        SetRectTransform(listScrollView.GetComponent<RectTransform>(),
            new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.85f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 0), Vector3.zero);

        // Status Text
        GameObject statusText = CreateText(creationPanel.transform, "StatusText", "", 14);
        statusText.GetComponent<Text>().color = Color.yellow;
        SetRectTransform(statusText.GetComponent<RectTransform>(),
            new Vector2(0.05f, 0.02f), new Vector2(0.6f, 0.08f), new Vector2(0, 0.5f),
            new Vector2(0, 30), Vector3.zero);

        // Create CharacterCreationUI component
        CharacterCreationUI creationUI = canvas.gameObject.AddComponent<CharacterCreationUI>();

        // Assign references
        SerializedObject so = new SerializedObject(creationUI);
        so.FindProperty("creationPanel").objectReferenceValue = creationPanel;
        so.FindProperty("nameInput").objectReferenceValue = nameInput.GetComponent<InputField>();
        so.FindProperty("relationshipDropdown").objectReferenceValue = relDropdown.GetComponent<Dropdown>();
        so.FindProperty("personalityInput").objectReferenceValue = persInput.GetComponent<InputField>();
        so.FindProperty("appearanceInput").objectReferenceValue = appInput.GetComponent<InputField>();
        so.FindProperty("backstoryInput").objectReferenceValue = storyInput.GetComponent<InputField>();
        so.FindProperty("voiceStyleInput").objectReferenceValue = voiceInput.GetComponent<InputField>();
        so.FindProperty("createButton").objectReferenceValue = createButton.GetComponent<Button>();
        so.FindProperty("closeButton").objectReferenceValue = closeButton.GetComponent<Button>();
        so.FindProperty("characterListPanel").objectReferenceValue = listPanel;
        so.FindProperty("characterListContent").objectReferenceValue = listScrollView.GetComponent<ScrollRect>().content;
        so.FindProperty("statusText").objectReferenceValue = statusText.GetComponent<Text>();
        so.ApplyModifiedProperties();

        // Hide panel by default
        creationPanel.SetActive(false);

        Undo.RegisterCreatedObjectUndo(creationPanel, "Setup Character Creation UI");
        Debug.Log("[SceneSetupUI] Setup Character Creation UI complete");
    }

    #endregion

    #region Memory Input UI

    private static void SetupMemoryInputUI()
    {
        Canvas canvas = GetOrCreateCanvas();

        // Check if already exists
        MemoryInputUI existing = Object.FindFirstObjectByType<MemoryInputUI>();
        if (existing != null)
        {
            Debug.Log("[SceneSetupUI] MemoryInputUI already exists, skipping.");
            return;
        }

        // Create Memory Panel
        GameObject memoryPanel = CreatePanel(canvas.transform, "MemoryPanel", new Color(0.1f, 0.12f, 0.1f, 0.98f));
        SetRectTransform(memoryPanel.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(600, 500), Vector3.zero);

        // Title
        GameObject titleGO = CreateText(memoryPanel.transform, "Title", "记忆管理 (F3)", 28, FontStyle.Bold);
        SetRectTransform(titleGO.GetComponent<RectTransform>(),
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
            new Vector2(0, 50), new Vector3(0, -10, 0));

        // Character Dropdown
        GameObject charLabel = CreateText(memoryPanel.transform, "CharLabel", "选择角色:", 16);
        SetRectTransform(charLabel.GetComponent<RectTransform>(),
            new Vector2(0.05f, 0.85f), new Vector2(0.25f, 0.9f), new Vector2(0, 0.5f),
            new Vector2(0, 35), Vector3.zero);

        GameObject charDropdown = CreateDropdown(memoryPanel.transform, "CharacterDropdown");
        SetRectTransform(charDropdown.GetComponent<RectTransform>(),
            new Vector2(0.28f, 0.85f), new Vector2(0.7f, 0.9f), new Vector2(0, 0.5f),
            new Vector2(0, 35), Vector3.zero);

        // Memory Content Input
        GameObject contentLabel = CreateText(memoryPanel.transform, "ContentLabel", "记忆内容:", 16);
        SetRectTransform(contentLabel.GetComponent<RectTransform>(),
            new Vector2(0.05f, 0.65f), new Vector2(0.25f, 0.7f), new Vector2(0, 0.5f),
            new Vector2(0, 100), Vector3.zero);

        GameObject contentInput = CreateInputField(memoryPanel.transform, "MemoryContentInput", "输入要添加的记忆...", true);
        SetRectTransform(contentInput.GetComponent<RectTransform>(),
            new Vector2(0.05f, 0.45f), new Vector2(0.95f, 0.75f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 0), Vector3.zero);

        // Importance Slider
        GameObject impLabel = CreateText(memoryPanel.transform, "ImpLabel", "重要性:", 16);
        SetRectTransform(impLabel.GetComponent<RectTransform>(),
            new Vector2(0.05f, 0.38f), new Vector2(0.2f, 0.42f), new Vector2(0, 0.5f),
            new Vector2(0, 30), Vector3.zero);

        GameObject impSlider = CreateSlider(memoryPanel.transform, "ImportanceSlider");
        SetRectTransform(impSlider.GetComponent<RectTransform>(),
            new Vector2(0.22f, 0.38f), new Vector2(0.6f, 0.42f), new Vector2(0, 0.5f),
            new Vector2(0, 30), Vector3.zero);

        // Importance Value Text
        GameObject impValueText = CreateText(memoryPanel.transform, "ImportanceValueText", "重要性: 5", 16);
        SetRectTransform(impValueText.GetComponent<RectTransform>(),
            new Vector2(0.62f, 0.38f), new Vector2(0.8f, 0.42f), new Vector2(0, 0.5f),
            new Vector2(0, 30), Vector3.zero);

        // Buttons
        GameObject submitButton = CreateButton(memoryPanel.transform, "SubmitButton", "提交");
        SetRectTransform(submitButton.GetComponent<RectTransform>(),
            new Vector2(0.55f, 0.02f), new Vector2(0.72f, 0.08f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 40), Vector3.zero);

        GameObject closeButton = CreateButton(memoryPanel.transform, "CloseButton", "关闭");
        SetRectTransform(closeButton.GetComponent<RectTransform>(),
            new Vector2(0.75f, 0.02f), new Vector2(0.92f, 0.08f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 40), Vector3.zero);

        // Memory List
        GameObject listLabel = CreateText(memoryPanel.transform, "ListLabel", "已有记忆:", 16);
        SetRectTransform(listLabel.GetComponent<RectTransform>(),
            new Vector2(0.05f, 0.32f), new Vector2(0.25f, 0.36f), new Vector2(0, 0.5f),
            new Vector2(0, 30), Vector3.zero);

        GameObject memoryListScroll = CreateScrollView(memoryPanel.transform, "MemoryListScrollView");
        SetRectTransform(memoryListScroll.GetComponent<RectTransform>(),
            new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.35f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 0), Vector3.zero);

        // Status Text
        GameObject statusText = CreateText(memoryPanel.transform, "StatusText", "", 14);
        statusText.GetComponent<Text>().color = Color.yellow;
        SetRectTransform(statusText.GetComponent<RectTransform>(),
            new Vector2(0.05f, 0.02f), new Vector2(0.5f, 0.08f), new Vector2(0, 0.5f),
            new Vector2(0, 30), Vector3.zero);

        // Create MemoryInputUI component
        MemoryInputUI memoryUI = canvas.gameObject.AddComponent<MemoryInputUI>();

        // Assign references
        SerializedObject so = new SerializedObject(memoryUI);
        so.FindProperty("memoryPanel").objectReferenceValue = memoryPanel;
        so.FindProperty("characterDropdown").objectReferenceValue = charDropdown.GetComponent<Dropdown>();
        so.FindProperty("memoryContentInput").objectReferenceValue = contentInput.GetComponent<InputField>();
        so.FindProperty("importanceSlider").objectReferenceValue = impSlider.GetComponent<Slider>();
        so.FindProperty("importanceValueText").objectReferenceValue = impValueText.GetComponent<Text>();
        so.FindProperty("submitButton").objectReferenceValue = submitButton.GetComponent<Button>();
        so.FindProperty("closeButton").objectReferenceValue = closeButton.GetComponent<Button>();
        so.FindProperty("memoryListScrollRect").objectReferenceValue = memoryListScroll.GetComponent<ScrollRect>();

        // Get memory list text from content
        Text memoryListText = memoryListScroll.GetComponent<ScrollRect>().content.GetComponentInChildren<Text>();
        so.FindProperty("memoryListText").objectReferenceValue = memoryListText;
        so.FindProperty("statusText").objectReferenceValue = statusText.GetComponent<Text>();
        so.ApplyModifiedProperties();

        // Hide panel by default
        memoryPanel.SetActive(false);

        Undo.RegisterCreatedObjectUndo(memoryPanel, "Setup Memory Input UI");
        Debug.Log("[SceneSetupUI] Setup Memory Input UI complete");
    }

    #endregion

    #region Helper Methods

    private static GameObject CreatePanel(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        Image image = go.AddComponent<Image>();
        image.color = color;

        // Add outline/shadow effect
        if (parent != null)
        {
            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
            outline.effectDistance = new Vector2(2, -2);
        }

        return go;
    }

    private static GameObject CreateText(Transform parent, string name, string text, int fontSize, FontStyle style = FontStyle.Normal)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        Text txt = go.AddComponent<Text>();
        txt.text = text;
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleLeft;

        return go;
    }

    private static GameObject CreateInputField(Transform parent, string name, string placeholder, bool multiLine = false)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        Image bg = go.AddComponent<Image>();
        bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);

        InputField input = go.AddComponent<InputField>();

        // Placeholder
        GameObject placeholderGO = CreateText(go.transform, "Placeholder", placeholder, 14);
        placeholderGO.GetComponent<Text>().color = new Color(0.5f, 0.5f, 0.5f, 1f);
        SetRectTransform(placeholderGO.GetComponent<RectTransform>(),
            new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
            new Vector2(-10, -10), new Vector3(5, 0, 0));

        // Text
        GameObject textGO = CreateText(go.transform, "Text", "", 14);
        SetRectTransform(textGO.GetComponent<RectTransform>(),
            new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
            new Vector2(-10, -10), new Vector3(5, 0, 0));

        input.placeholder = placeholderGO.GetComponent<Text>();
        input.textComponent = textGO.GetComponent<Text>();

        if (multiLine)
        {
        input.lineType = InputField.LineType.MultiLineSubmit;
        }

        return go;
    }

    private static GameObject CreateButton(Transform parent, string name, string text)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        Image bg = go.AddComponent<Image>();
        bg.color = new Color(0.25f, 0.45f, 0.65f, 1f);

        Button btn = go.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.highlightedColor = new Color(0.35f, 0.55f, 0.75f, 1f);
        colors.pressedColor = new Color(0.2f, 0.4f, 0.6f, 1f);
        btn.colors = colors;

        GameObject textGO = CreateText(go.transform, "Text", text, 16, FontStyle.Bold);
        textGO.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
        SetRectTransform(textGO.GetComponent<RectTransform>(),
            new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector3.zero);

        return go;
    }

    private static GameObject CreateDropdown(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        Image bg = go.AddComponent<Image>();
        bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);

        Dropdown dropdown = go.AddComponent<Dropdown>();

        // Label
        GameObject labelGO = CreateText(go.transform, "Label", "选择...", 14);
        labelGO.GetComponent<Text>().alignment = TextAnchor.MiddleLeft;
        SetRectTransform(labelGO.GetComponent<RectTransform>(),
            new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
            new Vector2(-30, -10), new Vector3(10, 0, 0));

        // Arrow
        GameObject arrowGO = new GameObject("Arrow");
        arrowGO.transform.SetParent(go.transform, false);
        Image arrowImg = arrowGO.AddComponent<Image>();
        arrowImg.color = new Color(0.7f, 0.7f, 0.7f, 1f);
        SetRectTransform(arrowGO.GetComponent<RectTransform>(),
            new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
            new Vector2(25, 25), new Vector3(-15, 0, 0));

        // Template for dropdown items
        GameObject templateGO = CreatePanel(go.transform, "Template", new Color(0.15f, 0.15f, 0.15f, 1f));
        templateGO.SetActive(false);
        SetRectTransform(templateGO.GetComponent<RectTransform>(),
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1),
            new Vector2(0, 150), Vector3.zero);

        ScrollRect scrollRect = templateGO.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        // Viewport
        GameObject viewportGO = new GameObject("Viewport");
        viewportGO.transform.SetParent(templateGO.transform, false);
        Image viewportMask = viewportGO.AddComponent<Image>();
        viewportMask.color = new Color(0.1f, 0.1f, 0.1f, 1f);
        Mask mask = viewportGO.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        SetRectTransform(viewportGO.GetComponent<RectTransform>(),
            new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 1),
            new Vector2(-20, 0), Vector3.zero);

        // Content
        GameObject contentGO = new GameObject("Content");
        contentGO.transform.SetParent(viewportGO.transform, false);
        RectTransform contentRT = contentGO.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.sizeDelta = new Vector2(0, 30);
        contentRT.anchoredPosition = Vector2.zero;

        ContentSizeFitter fitter = contentGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.MinSize;

        VerticalLayoutGroup vlg = contentGO.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(5, 5, 5, 5);
        vlg.spacing = 2;

        // Item
        GameObject itemGO = new GameObject("Item");
        itemGO.transform.SetParent(contentGO.transform, false);
        itemGO.AddComponent<RectTransform>();
        SetRectTransform(itemGO.GetComponent<RectTransform>(),
            new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 25), Vector3.zero);

        Toggle itemToggle = itemGO.AddComponent<Toggle>();

        GameObject itemBgGO = new GameObject("Item Background");
        itemBgGO.transform.SetParent(itemGO.transform, false);
        Image itemBg = itemBgGO.AddComponent<Image>();
        itemBg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
        SetRectTransform(itemBgGO.GetComponent<RectTransform>(),
            new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector3.zero);

        GameObject itemCheckGO = new GameObject("Item Checkmark");
        itemCheckGO.transform.SetParent(itemGO.transform, false);
        Image itemCheck = itemCheckGO.AddComponent<Image>();
        itemCheck.color = new Color(0.3f, 0.6f, 0.9f, 1f);
        SetRectTransform(itemCheckGO.GetComponent<RectTransform>(),
            new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(20, 20), new Vector3(10, 0, 0));

        GameObject itemLabelGO = CreateText(itemGO.transform, "Item Label", "Option", 14);
        SetRectTransform(itemLabelGO.GetComponent<RectTransform>(),
            new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
            new Vector2(-30, -10), new Vector3(25, 0, 0));

        itemToggle.targetGraphic = itemBg;
        itemToggle.graphic = itemCheck;

        // Setup dropdown references
        dropdown.targetGraphic = bg;
        dropdown.template = templateGO.GetComponent<RectTransform>();
        dropdown.captionText = labelGO.GetComponent<Text>();
        dropdown.itemText = itemLabelGO.GetComponent<Text>();

        // Setup scroll rect
        scrollRect.viewport = viewportGO.GetComponent<RectTransform>();
        scrollRect.content = contentRT;

        return go;
    }

    private static GameObject CreateSlider(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        Slider slider = go.AddComponent<Slider>();
        slider.minValue = 1;
        slider.maxValue = 10;
        slider.wholeNumbers = true;
        slider.value = 5;

        // Background
        GameObject bgGO = new GameObject("Background");
        bgGO.transform.SetParent(go.transform, false);
        Image bg = bgGO.AddComponent<Image>();
        bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        SetRectTransform(bgGO.GetComponent<RectTransform>(),
            new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 12), Vector3.zero);

        // Fill Area
        GameObject fillAreaGO = new GameObject("Fill Area");
        fillAreaGO.transform.SetParent(go.transform, false);
        fillAreaGO.AddComponent<RectTransform>();
        SetRectTransform(fillAreaGO.GetComponent<RectTransform>(),
            new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(-10, 0), Vector3.zero);

        // Fill
        GameObject fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        Image fill = fillGO.AddComponent<Image>();
        fill.color = new Color(0.3f, 0.6f, 0.9f, 1f);
        SetRectTransform(fillGO.GetComponent<RectTransform>(),
            new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(0, 10), Vector3.zero);

        // Handle Slide Area
        GameObject handleAreaGO = new GameObject("Handle Slide Area");
        handleAreaGO.transform.SetParent(go.transform, false);
        handleAreaGO.AddComponent<RectTransform>();
        SetRectTransform(handleAreaGO.GetComponent<RectTransform>(),
            new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(-20, 0), Vector3.zero);

        // Handle
        GameObject handleGO = new GameObject("Handle");
        handleGO.transform.SetParent(handleAreaGO.transform, false);
        Image handle = handleGO.AddComponent<Image>();
        handle.color = new Color(0.5f, 0.8f, 1f, 1f);
        SetRectTransform(handleGO.GetComponent<RectTransform>(),
            new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(20, 20), Vector3.zero);

        slider.fillRect = fillGO.GetComponent<RectTransform>();
        slider.handleRect = handleGO.GetComponent<RectTransform>();
        slider.targetGraphic = handle;

        return go;
    }

    private static GameObject CreateScrollView(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        ScrollRect scrollRect = go.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        // Viewport
        GameObject viewportGO = new GameObject("Viewport");
        viewportGO.transform.SetParent(go.transform, false);
        Image viewportImg = viewportGO.AddComponent<Image>();
        viewportImg.color = new Color(0.15f, 0.15f, 0.15f, 0.5f);
        Mask viewportMask = viewportGO.AddComponent<Mask>();
        viewportMask.showMaskGraphic = true;

        RectTransform viewportRT = viewportGO.GetComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.pivot = new Vector2(0, 1);
        viewportRT.sizeDelta = Vector2.zero;
        viewportRT.anchoredPosition = Vector2.zero;

        // Content with Text
        GameObject contentGO = new GameObject("Content");
        contentGO.transform.SetParent(viewportGO.transform, false);

        RectTransform contentRT = contentGO.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.sizeDelta = new Vector2(0, 0);
        contentRT.anchoredPosition = Vector2.zero;

        ContentSizeFitter csf = contentGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Text component for content
        GameObject textGO = CreateText(contentGO.transform, "Text", "", 14);
        RectTransform textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0, 1);
        textRT.anchorMax = new Vector2(1, 1);
        textRT.pivot = new Vector2(0.5f, 1);
        textRT.sizeDelta = new Vector2(-20, 0);
        textRT.anchoredPosition = new Vector2(0, -10);

        scrollRect.viewport = viewportRT;
        scrollRect.content = contentRT;

        return go;
    }

    private static void SetRectTransform(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 sizeDelta, Vector3 anchoredPosition)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition3D = anchoredPosition;
    }

    #endregion
}
