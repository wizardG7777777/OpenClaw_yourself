using System.Collections.Generic;

namespace MCP.Router
{
    public static class ParameterNormalizer
    {
        public static Dictionary<string, object> Normalize(
            Dictionary<string, object> args,
            ToolDefinition tool,
            out string errorParam)
        {
            errorParam = null;
            var normalized = new Dictionary<string, object>();

            if (args != null)
            {
                foreach (var kvp in args)
                    normalized[kvp.Key] = kvp.Value;
            }

            // Validate required params
            foreach (var param in tool.RequiredParams)
            {
                if (!normalized.ContainsKey(param) || normalized[param] == null)
                {
                    errorParam = param;
                    return null;
                }
            }

            // Ensure optional params have entries (null if absent)
            foreach (var param in tool.OptionalParams)
            {
                if (!normalized.ContainsKey(param))
                    normalized[param] = null;
            }

            // Validate timeout override if present
            if (tool.IsExclusive && normalized.ContainsKey("timeout") && normalized["timeout"] != null)
            {
                if (float.TryParse(normalized["timeout"].ToString(), out float t) && t > 0f)
                    normalized["timeout"] = t;
                else
                    normalized["timeout"] = tool.DefaultTimeout;
            }

            return normalized;
        }
    }
}
