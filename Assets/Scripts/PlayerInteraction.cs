using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteraction : MonoBehaviour
{
    [Header("Interaction Settings")]
    public float interactRange = 3f;
    public LayerMask interactLayer = ~0;

    void Update()
    {
        // Mouse click interaction (raycast from camera)
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            if (Physics.Raycast(ray, out RaycastHit hit, 50f, interactLayer))
            {
                IInteractable interactable = hit.collider.GetComponent<IInteractable>();
                if (interactable != null)
                {
                    float dist = Vector3.Distance(transform.position, hit.collider.transform.position);
                    if (dist <= interactRange)
                    {
                        interactable.Interact();
                    }
                }
            }
        }

        // E key interaction (nearest interactable in range)
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            Collider[] colliders = Physics.OverlapSphere(transform.position, interactRange, interactLayer);
            float closestDist = float.MaxValue;
            IInteractable closest = null;

            foreach (var col in colliders)
            {
                IInteractable interactable = col.GetComponent<IInteractable>();
                if (interactable != null)
                {
                    float dist = Vector3.Distance(transform.position, col.transform.position);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest = interactable;
                    }
                }
            }

            if (closest != null)
            {
                closest.Interact();
            }
        }
    }
}
