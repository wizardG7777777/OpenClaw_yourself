using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using MCP.Entity;

/// <summary>
/// Controls an NPC character driven by a Python backend via WebSocket.
/// Implements IInteractable for player interaction and integrates with
/// the EntityRegistry/SemanticResolver system for MCP tool discovery.
/// </summary>
[RequireComponent(typeof(EntityIdentity))]
[RequireComponent(typeof(NavMeshAgent))]
public class NpcController : MonoBehaviour, IInteractable
{
    // ------------------------------------------------------------------
    //  State
    // ------------------------------------------------------------------

    /// <summary>Possible NPC behavioural states.</summary>
    public enum NpcState { Idle, Walking, Talking }

    /// <summary>Current behavioural state (read-only from outside).</summary>
    public NpcState CurrentState { get; private set; } = NpcState.Idle;

    // ------------------------------------------------------------------
    //  Identity
    // ------------------------------------------------------------------

    [Header("Identity")]
    [Tooltip("Matches the backend database characters.id")]
    [SerializeField] public string characterId;

    [Tooltip("Human-readable name shown in UI prompts")]
    [SerializeField] public string displayName;

    // ------------------------------------------------------------------
    //  Movement
    // ------------------------------------------------------------------

    [Header("Movement")]
    [Tooltip("Extra tolerance added to stoppingDistance when checking arrival")]
    [SerializeField] private float arrivalTolerance = 0.1f;

    /// <summary>
    /// Optional callback invoked when the agent reaches its destination.
    /// Typically set by NpcRegistry to notify the backend on arrival.
    /// </summary>
    public System.Action OnArrivalCallback;

    // ------------------------------------------------------------------
    //  Animation
    // ------------------------------------------------------------------

    [Header("Animation")]
    [Tooltip("Optional Animator (Mecanim). If null, falls back to legacy Animation component.")]
    [SerializeField] private Animator animator;

    // ------------------------------------------------------------------
    //  Private references
    // ------------------------------------------------------------------

    private NavMeshAgent agent;
    private EntityIdentity entityIdentity;
    private Animation legacyAnimation;

    // ==================================================================
    //  Unity lifecycle
    // ==================================================================

    private void Awake()
    {
        // Cache required components
        agent = GetComponent<NavMeshAgent>();
        entityIdentity = GetComponent<EntityIdentity>();

        // Auto-configure EntityIdentity when fields are not already set
        if (entityIdentity != null)
        {
            if (string.IsNullOrEmpty(entityIdentity.entityId))
                entityIdentity.entityId = characterId;

            if (string.IsNullOrEmpty(entityIdentity.displayName))
                entityIdentity.displayName = displayName;

            if (string.IsNullOrEmpty(entityIdentity.entityType))
                entityIdentity.entityType = "npc";

            entityIdentity.interactable = true;
        }

        // Animator fallback: try serialized field, then Mecanim on this GO,
        // then legacy Animation (same pattern as PlayerMovement).
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator == null)
            legacyAnimation = GetComponentInChildren<Animation>();
    }

    private void Update()
    {
        if (CurrentState != NpcState.Walking) return;
        if (agent == null || !agent.enabled) return;

        // Check if the agent has arrived at its destination
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + arrivalTolerance)
        {
            Debug.Log($"[NpcController] {displayName} arrived at destination.");
            SetState(NpcState.Idle);

            System.Action callback = OnArrivalCallback;
            OnArrivalCallback = null;
            callback?.Invoke();
        }
    }

    private void OnEnable()
    {
        if (NpcRegistry.Instance != null)
        {
            NpcRegistry.Instance.Register(this);
        }
    }

    private void OnDisable()
    {
        if (NpcRegistry.Instance != null)
        {
            NpcRegistry.Instance.Unregister(this);
        }
    }

    // ==================================================================
    //  IInteractable
    // ==================================================================

    /// <inheritdoc/>
    public bool Interact()
    {
        if (CurrentState == NpcState.Talking)
        {
            Debug.Log($"[NpcController] {displayName} is already talking — interaction rejected.");
            return false;
        }

        Debug.Log($"[NpcController] Player interacting with {displayName}");
        SetState(NpcState.Talking);

        // Try to open the dialogue UI
        if (NpcDialogueUI.Instance != null)
        {
            NpcDialogueUI.Instance.OpenDialogue(this);
        }
        else
        {
            // No dialogue UI in scene — show placeholder and reset state
            Debug.Log($"[NpcController] {displayName}: 测试对话，占位符内容");
            SetState(NpcState.Idle);
        }

        return true;
    }

    /// <inheritdoc/>
    public string GetPromptText()
    {
        return $"\u4e0e {displayName} \u5bf9\u8bdd";
    }

    /// <inheritdoc/>
    public Dictionary<string, object> GetState()
    {
        return new Dictionary<string, object>
        {
            { "character_id", characterId },
            { "display_name", displayName },
            { "status", CurrentState.ToString().ToLower() },
            { "is_talking", CurrentState == NpcState.Talking }
        };
    }

    // ==================================================================
    //  Movement API
    // ==================================================================

    /// <summary>
    /// Commands the NPC to walk to <paramref name="destination"/> using NavMeshAgent.
    /// </summary>
    public void MoveTo(Vector3 destination)
    {
        if (agent == null)
        {
            Debug.LogWarning("[NpcController] NavMeshAgent is missing — cannot move.");
            return;
        }

        agent.isStopped = false;
        agent.SetDestination(destination);
        SetState(NpcState.Walking);
        Debug.Log($"[NpcController] {displayName} moving to {destination}.");
    }

    /// <summary>
    /// Immediately stops the NavMeshAgent and transitions to Idle.
    /// </summary>
    public void StopMovement()
    {
        if (agent != null && agent.enabled)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        SetState(NpcState.Idle);
        Debug.Log($"[NpcController] {displayName} stopped movement.");
    }

    // ==================================================================
    //  External state control
    // ==================================================================

    /// <summary>
    /// Enter or exit the Talking state from external code (e.g. dialogue system).
    /// </summary>
    public void SetTalking(bool talking)
    {
        SetState(talking ? NpcState.Talking : NpcState.Idle);
    }

    /// <summary>
    /// Force the NPC back to Idle regardless of current state.
    /// </summary>
    public void SetIdle()
    {
        if (CurrentState == NpcState.Walking)
            StopMovement();
        else
            SetState(NpcState.Idle);
    }

    // ==================================================================
    //  Internal helpers
    // ==================================================================

    private void SetState(NpcState newState)
    {
        if (CurrentState == newState) return;

        NpcState previousState = CurrentState;
        CurrentState = newState;

        Debug.Log($"[NpcController] {displayName} state: {previousState} -> {newState}");
        UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        bool isWalking = CurrentState == NpcState.Walking;
        bool isTalking = CurrentState == NpcState.Talking;

        // Mecanim path
        if (animator != null)
        {
            animator.SetBool("IsWalking", isWalking);
            animator.SetBool("IsTalking", isTalking);
            return;
        }

        // Legacy Animation fallback (play on walk, stop otherwise)
        if (legacyAnimation != null)
        {
            if (isWalking)
                legacyAnimation.Play();
            else
                legacyAnimation.Stop();
        }
    }
}
