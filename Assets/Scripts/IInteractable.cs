public interface IInteractable
{
    /// <summary>
    /// Returns true if the interaction executed, false if rejected (e.g. animation in progress).
    /// </summary>
    bool Interact();
    string GetPromptText();
}
