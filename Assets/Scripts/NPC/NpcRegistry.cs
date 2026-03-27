using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCP.Gateway;
using MCP.Entity;

/// <summary>
/// Singleton registry that tracks all active NPCs and handles MCP backend events
/// for character movement and state changes.
/// </summary>
public class NpcRegistry : MonoBehaviour
{
    /// <summary>Singleton instance accessible from anywhere.</summary>
    public static NpcRegistry Instance { get; private set; }

    private readonly Dictionary<string, NpcController> _npcs = new Dictionary<string, NpcController>();
    private MCPGateway _gateway;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        _gateway = FindAnyObjectByType<MCPGateway>();
        if (_gateway != null)
        {
            _gateway.RegisterEventHandler("character_move", OnCharacterMove);
            _gateway.RegisterEventHandler("character_state_changed", OnCharacterStateChanged);
            Debug.Log("[NpcRegistry] Registered MCP event handlers.");
        }
        else
        {
            Debug.LogWarning("[NpcRegistry] MCPGateway not found. Backend events will not be received.");
        }
    }

    private void OnDisable()
    {
        if (_gateway != null)
        {
            _gateway.UnregisterEventHandler("character_move", OnCharacterMove);
            _gateway.UnregisterEventHandler("character_state_changed", OnCharacterStateChanged);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ────────────────────────────────────────────
    //  NPC Tracking
    // ────────────────────────────────────────────

    /// <summary>Registers an NPC so it can be looked up by character ID.</summary>
    public void Register(NpcController npc)
    {
        if (npc == null)
        {
            Debug.LogWarning("[NpcRegistry] Attempted to register a null NpcController.");
            return;
        }

        if (string.IsNullOrEmpty(npc.characterId))
        {
            Debug.LogWarning($"[NpcRegistry] NpcController on '{npc.gameObject.name}' has no characterId. Skipping registration.");
            return;
        }

        if (_npcs.ContainsKey(npc.characterId))
        {
            Debug.LogWarning($"[NpcRegistry] Overwriting existing NPC with characterId '{npc.characterId}'.");
        }

        _npcs[npc.characterId] = npc;
        Debug.Log($"[NpcRegistry] Registered NPC '{npc.characterId}'.");
    }

    /// <summary>Unregisters an NPC, removing it from the lookup dictionary.</summary>
    public void Unregister(NpcController npc)
    {
        if (npc == null)
            return;

        string key = npc.characterId;
        if (string.IsNullOrEmpty(key))
            return;

        if (_npcs.TryGetValue(key, out var registered) && registered == npc)
        {
            _npcs.Remove(key);
            Debug.Log($"[NpcRegistry] Unregistered NPC '{key}'.");
        }
    }

    /// <summary>Returns the NpcController associated with the given character ID, or null if not found.</summary>
    public NpcController GetByCharacterId(string characterId)
    {
        if (string.IsNullOrEmpty(characterId))
            return null;

        _npcs.TryGetValue(characterId, out var npc);
        return npc;
    }

    /// <summary>Returns a read-only collection of all registered NPCs.</summary>
    public IReadOnlyCollection<NpcController> GetAll()
    {
        return _npcs.Values;
    }

    // ────────────────────────────────────────────
    //  MCP Event Handlers
    // ────────────────────────────────────────────

    /// <summary>
    /// Handles the "character_move" event from the backend.
    /// Expected payload: { "character_id": "char_01", "target_id": "sofa_01" }
    /// </summary>
    private void OnCharacterMove(JObject data)
    {
        if (data == null)
        {
            Debug.LogWarning("[NpcRegistry] OnCharacterMove received null data.");
            return;
        }

        string characterId = data["character_id"]?.Value<string>();
        string targetId = data["target_id"]?.Value<string>();

        if (string.IsNullOrEmpty(characterId))
        {
            Debug.LogWarning("[NpcRegistry] OnCharacterMove: missing 'character_id' in payload.");
            return;
        }

        if (string.IsNullOrEmpty(targetId))
        {
            Debug.LogWarning($"[NpcRegistry] OnCharacterMove: missing 'target_id' for character '{characterId}'.");
            return;
        }

        NpcController npc = GetByCharacterId(characterId);
        if (npc == null)
        {
            Debug.LogWarning($"[NpcRegistry] OnCharacterMove: no NPC found with characterId '{characterId}'.");
            return;
        }

        // Resolve target position via EntityRegistry
        EntityIdentity targetEntity = EntityRegistry.Instance != null
            ? EntityRegistry.Instance.GetById(targetId)
            : null;

        if (targetEntity == null)
        {
            Debug.LogWarning($"[NpcRegistry] OnCharacterMove: target '{targetId}' not found in EntityRegistry.");
            return;
        }

        Vector3 targetPosition = targetEntity.transform.position;
        npc.MoveTo(targetPosition);

        // Set up arrival callback to notify backend
        MCPGateway gw = _gateway;
        npc.OnArrivalCallback = () =>
        {
            if (gw != null)
            {
                gw.SendToBackend("movement_completed",
                    new JObject { ["character_id"] = characterId },
                    (ok, responseData) =>
                    {
                        if (!ok)
                            Debug.LogWarning($"[NpcRegistry] movement_completed for '{characterId}' failed: {responseData}");
                        else
                            Debug.Log($"[NpcRegistry] movement_completed acknowledged for '{characterId}'.");
                    });
            }
        };

        Debug.Log($"[NpcRegistry] NPC '{characterId}' moving to '{targetId}' at {targetPosition}.");
    }

    /// <summary>
    /// Handles the "character_state_changed" event from the backend.
    /// Expected payload: { "character_id": "char_01", "status": "idle" }
    /// </summary>
    private void OnCharacterStateChanged(JObject data)
    {
        if (data == null)
        {
            Debug.LogWarning("[NpcRegistry] OnCharacterStateChanged received null data.");
            return;
        }

        string characterId = data["character_id"]?.Value<string>();
        string status = data["status"]?.Value<string>();

        if (string.IsNullOrEmpty(characterId))
        {
            Debug.LogWarning("[NpcRegistry] OnCharacterStateChanged: missing 'character_id' in payload.");
            return;
        }

        NpcController npc = GetByCharacterId(characterId);
        if (npc == null)
        {
            Debug.LogWarning($"[NpcRegistry] OnCharacterStateChanged: no NPC found with characterId '{characterId}'.");
            return;
        }

        switch (status)
        {
            case "idle":
                npc.SetIdle();
                Debug.Log($"[NpcRegistry] NPC '{characterId}' set to idle.");
                break;
            case "talking":
                npc.SetTalking(true);
                Debug.Log($"[NpcRegistry] NPC '{characterId}' set to talking.");
                break;
            case "walking":
                // Walking is handled by character_move; nothing to do here.
                break;
            default:
                Debug.LogWarning($"[NpcRegistry] OnCharacterStateChanged: unknown status '{status}' for '{characterId}'.");
                break;
        }
    }
}
