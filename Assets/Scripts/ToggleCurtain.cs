using UnityEngine;

public class ToggleCurtain : MonoBehaviour, IInteractable
{
    [Header("Curtain Settings")]
    [Tooltip("How far the curtain slides open along X axis (positive = right, negative = left)")]
    public float slideOffset = 1.5f;

    [Tooltip("How fast the curtain moves")]
    public float slideSpeed = 3f;

    private Vector3 closedPosition;
    private Vector3 openPosition;
    private bool isOpen = false;
    private bool isMoving = false;

    void Awake()
    {
        closedPosition = transform.localPosition;
        openPosition = closedPosition + new Vector3(slideOffset, 0f, 0f);
    }

    void Update()
    {
        if (!isMoving) return;

        Vector3 target = isOpen ? openPosition : closedPosition;
        transform.localPosition = Vector3.MoveTowards(transform.localPosition, target, slideSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.localPosition, target) < 0.001f)
        {
            transform.localPosition = target;
            isMoving = false;
        }
    }

    public bool Interact()
    {
        isOpen = !isOpen;
        isMoving = true;
        Debug.Log($"{gameObject.name} is now {(isOpen ? "OPEN" : "CLOSED")}");
        return true;
    }

    public string GetPromptText()
    {
        return isOpen ? "Close Curtain" : "Open Curtain";
    }
}
