using System.Collections.Generic;

public interface IInteractable
{
    /// <summary>
    /// Returns true if the interaction executed, false if rejected (e.g. animation in progress).
    /// </summary>
    bool Interact();
    string GetPromptText();

    /// <summary>
    /// Returns the current state of this interactable as key-value pairs.
    /// Used by Agent to perceive entity state (e.g. door open/closed, lamp on/off).
    /// </summary>
    Dictionary<string, object> GetState();
}
