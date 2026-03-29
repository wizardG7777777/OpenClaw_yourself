using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCP.Core;
using MCP.Router;

namespace MCP.Gateway
{
    /// <summary>
    /// Unified entry point for all MCP requests.
    /// Handles structural validation, whitelist checking, request ID assignment,
    /// and basic QPS throttling. Does NOT validate value domains or business semantics.
    /// </summary>
    public class MCPGateway : MonoBehaviour
    {
        [Header("Routing")]
        [SerializeField] private MCPRouter router;

        [Header("Throttling")]
        [SerializeField] private float qpsLimit = 10f;

        [Header("Session")]
        [SerializeField] private string sessionPlayerId = "player_1";

        private readonly Queue<float> _requestTimestamps = new Queue<float>();

        private void Awake()
        {
            if (router == null)
                router = FindAnyObjectByType<MCPRouter>();
            // Ensure game loop runs when Unity Editor is in background (needed for MCP automation)
            Application.runInBackground = true;
        }

        /// <summary>
        /// Main entry point. Accepts raw JSON, validates structure and whitelist,
        /// assigns a request ID, and forwards to the Router.
        /// </summary>
        public MCPResponse ProcessRequest(string rawJson)
        {
            // --- Throttle check ---
            float now = Time.unscaledTime;
            PruneTimestamps(now);

            if (_requestTimestamps.Count >= qpsLimit)
            {
                Debug.LogWarning("[MCPGateway] QPS limit exceeded. Rejecting request.");
                return ErrorResponse("RATE_LIMITED", "Too many requests. Try again shortly.", true);
            }

            _requestTimestamps.Enqueue(now);

            // --- Parse JSON ---
            JObject parsed;
            try
            {
                parsed = JObject.Parse(rawJson);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCPGateway] JSON parse failed: {ex.Message}");
                return ErrorResponse("INVALID_PARAMS", $"Malformed JSON: {ex.Message}", false);
            }

            // --- Structural validation ---
            ValidationResult structureResult = RequestValidator.ValidateStructure(parsed);
            if (!structureResult.IsValid)
            {
                Debug.LogWarning($"[MCPGateway] Structural validation failed: {structureResult.ErrorReason}");
                return ErrorResponse("INVALID_PARAMS", structureResult.ErrorReason, false,
                    structureResult.ErrorField, structureResult.Suggestion);
            }

            string toolName = parsed["tool"].Value<string>();

            // --- Whitelist check ---
            if (router == null)
                router = FindAnyObjectByType<MCPRouter>();
            if (router == null)
                return ErrorResponse("INTERNAL_ERROR", "MCPRouter not found in scene.", false);
            IReadOnlyCollection<string> allowedTools = router.Registry.GetAllowedTools();
            ValidationResult toolResult = RequestValidator.ValidateToolExists(toolName, allowedTools);
            if (!toolResult.IsValid)
            {
                Debug.LogWarning($"[MCPGateway] Unknown tool: {toolName}");
                return ErrorResponse("INVALID_TOOL", toolResult.ErrorReason, false,
                    toolResult.ErrorField, toolResult.Suggestion);
            }

            // --- Build MCPRequest ---
            string requestId = Guid.NewGuid().ToString("N").Substring(0, 8);

            var args = parsed["args"].ToObject<Dictionary<string, object>>();

            var request = new MCPRequest
            {
                RequestId = requestId,
                Tool = toolName,
                Args = args,
                PlayerId = sessionPlayerId
            };

            Debug.Log($"[MCPGateway] [{requestId}] tool={toolName} player={sessionPlayerId}");

            // --- Forward to Router ---
            return router.Route(request);
        }

        private void PruneTimestamps(float now)
        {
            float windowStart = now - 1f;
            while (_requestTimestamps.Count > 0 && _requestTimestamps.Peek() < windowStart)
                _requestTimestamps.Dequeue();
        }

        private static MCPResponse ErrorResponse(string code, string message, bool retryable,
            string field = null, string suggestion = null)
        {
            var details = new Dictionary<string, object>();
            if (field != null) details["field"] = field;
            if (suggestion != null) details["suggestion"] = suggestion;

            return new MCPResponse
            {
                Ok = false,
                Error = new MCPError
                {
                    Code = code,
                    Message = message,
                    Retryable = retryable,
                    Details = details.Count > 0 ? details : null
                }
            };
        }
    }
}
