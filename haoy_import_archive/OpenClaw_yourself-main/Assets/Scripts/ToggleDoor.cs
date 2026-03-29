using System.Collections.Generic;
using UnityEngine;

public class ToggleDoor : MonoBehaviour, IInteractable
{
    [Tooltip("How far the door opens in degrees around Y axis")]
    public float openAngle = 90f;

    [Tooltip("Rotation speed in degrees per second")]
    public float rotateSpeed = 180f;

    private bool isOpen = false;
    private bool isMoving = false;
    private Quaternion closedRotation;
    private Quaternion openRotation;
    private Transform pivot;

    void Awake()
    {
        pivot = transform.parent;
        if (pivot == null)
        {
            Debug.LogError($"ToggleDoor on '{gameObject.name}' requires a parent pivot object!");
            return;
        }
        closedRotation = pivot.localRotation;
        openRotation = closedRotation * Quaternion.Euler(0f, openAngle, 0f);
    }

    void Update()
    {
        if (!isMoving || pivot == null) return;

        Quaternion target = isOpen ? openRotation : closedRotation;
        pivot.localRotation = Quaternion.RotateTowards(
            pivot.localRotation, target, rotateSpeed * Time.deltaTime);

        if (Quaternion.Angle(pivot.localRotation, target) < 0.1f)
        {
            pivot.localRotation = target;
            isMoving = false;
        }
    }

    public bool Interact()
    {
        if (isMoving) return false;
        isOpen = !isOpen;
        isMoving = true;
        Debug.Log($"{gameObject.name} is now {(isOpen ? "OPEN" : "CLOSED")}");
        return true;
    }

    public string GetPromptText()
    {
        return isOpen ? "Close Door" : "Open Door";
    }

    public Dictionary<string, object> GetState()
    {
        return new Dictionary<string, object>
        {
            { "status", isOpen ? "open" : "closed" },
            { "is_moving", isMoving }
        };
    }
}
