using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 3.5f;
    public float rotationSpeed = 10f;

    [Header("Animation")]
    public Animation legacyAnimation;

    private Rigidbody rb;
    private NavMeshAgent navAgent;
    private Vector3 moveInput;
    private bool isWalking = false;

    // True when NavMeshAgent is actively navigating to a destination
    private bool IsAgentNavigating =>
        navAgent != null && navAgent.enabled && navAgent.hasPath && !navAgent.isStopped;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        navAgent = GetComponent<NavMeshAgent>();
        if (legacyAnimation == null)
            legacyAnimation = GetComponentInChildren<Animation>();
    }

    void Update()
    {
        if (IsAgentNavigating)
        {
            // Let NavMeshAgent drive — only update animation from agent velocity
            bool agentWalking = navAgent.velocity.magnitude > 0.1f;
            SetWalkAnimation(agentWalking);
            return;
        }

        // Read input (new Input System)
        Vector2 input = Vector2.zero;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) input.y += 1f;
            if (Keyboard.current.sKey.isPressed) input.y -= 1f;
            if (Keyboard.current.aKey.isPressed) input.x -= 1f;
            if (Keyboard.current.dKey.isPressed) input.x += 1f;
        }

        // Convert input to camera-relative direction
        Camera cam = Camera.main;
        if (cam != null && input.sqrMagnitude > 0.01f)
        {
            Vector3 camForward = cam.transform.forward;
            Vector3 camRight = cam.transform.right;
            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            moveInput = (camForward * input.y + camRight * input.x).normalized;
        }
        else
        {
            moveInput = Vector3.zero;
        }

        SetWalkAnimation(moveInput.magnitude > 0.1f);
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        if (IsAgentNavigating)
        {
            // Yield Rigidbody control to NavMeshAgent
            rb.isKinematic = true;
            if (navAgent != null)
            {
                navAgent.updatePosition = true;
                navAgent.updateRotation = true;
            }
            return;
        }

        // Keyboard mode: stop NavMeshAgent from overriding position
        if (navAgent != null && navAgent.enabled)
        {
            navAgent.updatePosition = false;
            navAgent.updateRotation = false;
        }

        // Restore physics and apply manual input
        rb.isKinematic = false;

        Vector3 newPosition = rb.position + moveInput * moveSpeed * Time.fixedDeltaTime;
        newPosition.y = rb.position.y;
        rb.MovePosition(newPosition);

        if (moveInput.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveInput, Vector3.up);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
        }
    }

    private void SetWalkAnimation(bool walking)
    {
        if (legacyAnimation == null || walking == isWalking) return;
        isWalking = walking;
        if (isWalking) legacyAnimation.Play();
        else legacyAnimation.Stop();
    }
}
