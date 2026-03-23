using UnityEngine;

public class ToggleLight : MonoBehaviour, IInteractable
{
    private Light lightComponent;
    private bool isOn = true;

void Awake()
    {
        lightComponent = GetComponent<Light>();
        if (lightComponent == null)
            lightComponent = GetComponentInChildren<Light>();
        if (lightComponent != null)
            isOn = lightComponent.enabled;
    }

    public bool Interact()
    {
        if (lightComponent == null) return false;
        isOn = !isOn;
        lightComponent.enabled = isOn;
        Debug.Log($"{gameObject.name} light is now {(isOn ? "ON" : "OFF")}");
        return true;
    }

    public string GetPromptText()
    {
        return isOn ? "Turn Off Light" : "Turn On Light";
    }
}
