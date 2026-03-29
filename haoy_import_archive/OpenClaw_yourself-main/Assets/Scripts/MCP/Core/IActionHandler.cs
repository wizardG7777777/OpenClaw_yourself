namespace MCP.Core
{
    public interface IActionHandler
    {
        void StartAction(ActionInstance action);
        void Cancel();
        void UpdateAction();
        bool IsComplete { get; }
    }
}
