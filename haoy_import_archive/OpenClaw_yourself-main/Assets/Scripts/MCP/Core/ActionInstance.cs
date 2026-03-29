namespace MCP.Core
{
    public class ActionInstance
    {
        public string ActionId;
        public string ToolName;
        public ActionStatus Status;
        public ResolvedTarget Target;
        public float CreatedAt;
        public float Timeout;
        public object Result;
        public string ErrorCode;
        public string CancelledActionId;
    }
}
