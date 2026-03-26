using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace MCP.Gateway
{
    public static class RequestValidator
    {
        /// <summary>
        /// Validates the structural integrity of a raw MCP request.
        /// Checks that "tool" is a non-empty string and "args" is an object (or absent).
        /// Does NOT validate value domains or business semantics.
        /// </summary>
        public static ValidationResult ValidateStructure(JObject request)
        {
            // Check "tool" field exists and is a non-empty string
            JToken toolToken = request["tool"];
            if (toolToken == null || toolToken.Type != JTokenType.String)
            {
                return ValidationResult.Failure(
                    "tool", toolToken?.ToString() ?? "(missing)",
                    "Field 'tool' must be a non-empty string.",
                    "Provide a valid tool name, e.g. \"tool\": \"move\"");
            }

            string toolValue = toolToken.Value<string>();
            if (string.IsNullOrWhiteSpace(toolValue))
            {
                return ValidationResult.Failure(
                    "tool", "",
                    "Field 'tool' must not be empty.",
                    "Provide a valid tool name, e.g. \"tool\": \"move\"");
            }

            // Check "args" field — if present it must be an object; if absent we default it
            JToken argsToken = request["args"];
            if (argsToken != null && argsToken.Type != JTokenType.Object)
            {
                return ValidationResult.Failure(
                    "args", argsToken.ToString(),
                    "Field 'args' must be a JSON object.",
                    "Provide args as an object, e.g. \"args\": {}");
            }

            // If args is missing, inject an empty object so downstream code can rely on it
            if (argsToken == null)
            {
                request["args"] = new JObject();
            }

            return ValidationResult.Success();
        }

        /// <summary>
        /// Checks whether the requested tool name exists in the allowed tool set.
        /// </summary>
        public static ValidationResult ValidateToolExists(string toolName, IReadOnlyCollection<string> allowedTools)
        {
            if (allowedTools == null || !allowedTools.Contains(toolName))
            {
                return ValidationResult.Failure(
                    "tool", toolName,
                    $"Unknown tool '{toolName}'. Not in whitelist.",
                    "Check available tools via the tool registry.");
            }

            return ValidationResult.Success();
        }
    }
}
