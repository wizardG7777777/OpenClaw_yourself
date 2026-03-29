using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Follow Settings")]
    public Vector3 offset = new Vector3(0f, 6f, -5f);
    public float smoothSpeed = 5f;

    [Header("Fixed Rotation")]
    public Vector3 fixedRotation = new Vector3(55f, 0f, 0f);

void LateUpdate()
    {
        if (target == null) return;

        // Fixed top-down: directly follow target, no Lerp
        transform.position = target.position + offset;
    }


void Start()
    {
        transform.eulerAngles = fixedRotation;
    }
}
