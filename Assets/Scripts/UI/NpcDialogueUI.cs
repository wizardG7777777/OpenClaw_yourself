using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;
using MCP.Gateway;

/// <summary>
/// Singleton dialogue UI that handles player-NPC conversations.
/// Sends player messages to the Python backend via MCPGateway and
/// displays replies with a typewriter effect.
/// </summary>
public class NpcDialogueUI : MonoBehaviour, IUIPanel
{
    // ------------------------------------------------------------------
    //  Singleton
    // ------------------------------------------------------------------

    public static NpcDialogueUI Instance { get; private set; }

    // ------------------------------------------------------------------
    //  Serialized references
    // ------------------------------------------------------------------

    [Header("Dialogue Panel")]
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private Text npcNameText;
    [SerializeField] private Text dialogueText;
    [SerializeField] private InputField playerInputField;
    [SerializeField] private Button sendButton;
    [SerializeField] private ScrollRect historyScrollRect;
    [SerializeField] private Text historyText;

    [Header("Speech Bubble")]
    [SerializeField] private GameObject speechBubble;
    [SerializeField] private Text speechBubbleText;

    [Header("Settings")]
    [SerializeField] private float typewriterSpeed = 0.03f;
    [SerializeField] private float bubbleDisplayTime = 5f;
    [SerializeField] private float bubbleHeight = 2f;

    // ------------------------------------------------------------------
    //  Private state
    // ------------------------------------------------------------------

    private NpcController currentNpc;
    private Coroutine typewriterCoroutine;
    private Coroutine bubbleCoroutine;

    // IUIPanel implementation
    public bool IsExclusive => true;
    public bool IsOpen => dialoguePanel != null && dialoguePanel.activeSelf;

    // ==================================================================
    //  Unity lifecycle
    // ==================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        if (speechBubble != null)
            speechBubble.SetActive(false);
    }

    private void Start()
    {
        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendClicked);

        if (playerInputField != null)
            playerInputField.onEndEdit.AddListener(OnInputEndEdit);
    }

    private void Update()
    {
        if (dialoguePanel != null && dialoguePanel.activeSelf)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                CloseDialogue();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ==================================================================
    //  Public API
    // ==================================================================

    /// <summary>
    /// Opens the dialogue panel for the given NPC.
    /// </summary>
    public void Open()
    {
        // Use OpenDialogue(npc) instead; this satisfies the IUIPanel interface.
        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);
    }

    public void OpenDialogue(NpcController npc)
    {
        if (npc == null) return;

        // Register with UIManager as exclusive panel
        if (UIManager.Instance != null)
            UIManager.Instance.RequestOpen(this);
        else
            Open();

        currentNpc = npc;

        if (npcNameText != null)
            npcNameText.text = npc.displayName;

        if (dialogueText != null)
            dialogueText.text = "...";

        if (playerInputField != null)
        {
            playerInputField.text = string.Empty;
            playerInputField.ActivateInputField();
        }

        npc.SetTalking(true);

        Debug.Log($"[NpcDialogueUI] Opened dialogue with {npc.displayName}");
    }

    /// <summary>
    /// Closes the dialogue panel and releases the current NPC.
    /// </summary>
    public void Close()
    {
        CloseDialogue();
    }

    public void CloseDialogue()
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        if (UIManager.Instance != null)
            UIManager.Instance.NotifyClose(this);

        if (typewriterCoroutine != null)
        {
            StopCoroutine(typewriterCoroutine);
            typewriterCoroutine = null;
        }

        if (currentNpc != null)
        {
            currentNpc.SetTalking(false);
            Debug.Log($"[NpcDialogueUI] Closed dialogue with {currentNpc.displayName}");
            currentNpc = null;
        }
    }

    /// <summary>
    /// Shows a world-space speech bubble above the given NPC with a typewriter effect.
    /// Does nothing if the speechBubble reference is not assigned.
    /// </summary>
    public void ShowBubble(NpcController npc, string text)
    {
        if (speechBubble == null) return;
        if (npc == null) return;

        speechBubble.transform.position = npc.transform.position + Vector3.up * bubbleHeight;
        speechBubble.SetActive(true);

        if (bubbleCoroutine != null)
            StopCoroutine(bubbleCoroutine);

        bubbleCoroutine = StartCoroutine(BubbleCoroutine(text));
    }

    // ==================================================================
    //  Send flow
    // ==================================================================

    private void OnInputEndEdit(string text)
    {
        // Submit on Enter key (not Tab or click-away)
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
            OnSendClicked();
    }

    private void OnSendClicked()
    {
        if (playerInputField == null || currentNpc == null) return;

        string text = playerInputField.text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // Append player message to history
        AppendHistory($"你: {text}\n");

        // Clear input and refocus
        playerInputField.text = string.Empty;
        playerInputField.ActivateInputField();

        // Show loading state
        if (dialogueText != null)
            dialogueText.text = "...";

        // Find gateway and send
        MCPGateway gateway = FindAnyObjectByType<MCPGateway>();

        if (gateway == null || !gateway.IsBackendConnected)
        {
            if (dialogueText != null)
                dialogueText.text = "\uff08\u540e\u7aef\u672a\u8fde\u63a5\uff09";

            AppendHistory($"{currentNpc.displayName}: \uff08\u540e\u7aef\u672a\u8fde\u63a5\uff09\n");
            ScrollHistoryToBottom();
            return;
        }

        JObject @params = new JObject
        {
            ["character_id"] = currentNpc.characterId,
            ["message"] = text
        };

        gateway.SendToBackend("talk_to_character", @params, OnResponse, 20f);
    }

    private void OnResponse(bool ok, JObject data)
    {
        if (ok)
        {
            string reply = data?["reply"]?.ToString() ?? string.Empty;
            string npcName = currentNpc != null ? currentNpc.displayName : "NPC";

            AppendHistory($"{npcName}: {reply}\n");

            if (typewriterCoroutine != null)
                StopCoroutine(typewriterCoroutine);

            typewriterCoroutine = StartCoroutine(TypewriterCoroutine(dialogueText, reply));

            // Optionally show speech bubble
            if (currentNpc != null)
                ShowBubble(currentNpc, reply);
        }
        else
        {
            if (dialogueText != null)
                dialogueText.text = "\u5bf9\u8bdd\u5931\u8d25\uff0c\u8bf7\u91cd\u8bd5";

            AppendHistory("\u5bf9\u8bdd\u5931\u8d25\uff0c\u8bf7\u91cd\u8bd5\n");
        }

        ScrollHistoryToBottom();
    }

    // ==================================================================
    //  Typewriter effect
    // ==================================================================

    private IEnumerator TypewriterCoroutine(Text target, string fullText)
    {
        if (target == null) yield break;

        target.text = string.Empty;

        foreach (char c in fullText)
        {
            target.text += c;
            yield return new WaitForSeconds(typewriterSpeed);
        }

        typewriterCoroutine = null;
    }

    // ==================================================================
    //  Speech bubble coroutine
    // ==================================================================

    private IEnumerator BubbleCoroutine(string text)
    {
        if (speechBubbleText != null)
        {
            speechBubbleText.text = string.Empty;

            foreach (char c in text)
            {
                speechBubbleText.text += c;
                yield return new WaitForSeconds(typewriterSpeed);
            }
        }

        yield return new WaitForSeconds(bubbleDisplayTime);

        if (speechBubble != null)
            speechBubble.SetActive(false);

        bubbleCoroutine = null;
    }

    // ==================================================================
    //  History helpers
    // ==================================================================

    private void AppendHistory(string line)
    {
        if (historyText != null)
            historyText.text += line;
    }

    private void ScrollHistoryToBottom()
    {
        if (historyScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            historyScrollRect.verticalNormalizedPosition = 0f;
        }
    }
}
