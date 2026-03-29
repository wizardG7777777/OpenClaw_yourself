using System;

namespace MCP.Router
{
    public class ToolDefinition
    {
        public string ToolName;
        public string Description;
        public bool IsExclusive;
        public float DefaultTimeout;
        public string[] RequiredParams;
        public string[] OptionalParams;
        public Type HandlerType;

        public ToolDefinition(
            string toolName,
            string description,
            bool isExclusive,
            float defaultTimeout = 0f,
            string[] requiredParams = null,
            string[] optionalParams = null,
            Type handlerType = null)
        {
            ToolName = toolName;
            Description = description;
            IsExclusive = isExclusive;
            DefaultTimeout = defaultTimeout;
            RequiredParams = requiredParams ?? Array.Empty<string>();
            OptionalParams = optionalParams ?? Array.Empty<string>();
            HandlerType = handlerType;
        }
    }
}
