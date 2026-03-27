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
    /// Also manages WebSocket-based backend communication via helper classes.
    /// </summary>
    public class MCPGateway : MonoBehaviour
    {
        [Header("Routing")]
        [SerializeField] private MCPRouter router;

        [Header("Throttling")]
        [SerializeField] private float qpsLimit = 10f;

        [Header("Session")]
        [SerializeField] private string sessionPlayerId = "player_1";

        [Header("Backend Connection")]
        [SerializeField] private string backendUrl = "ws://localhost:8765/ws";
        [SerializeField] private bool autoConnectOnStart = true;

        private readonly Queue<float> _requestTimestamps = new Queue<float>();

        private WebSocketTransport _transport;
        private OutboundRequestManager _outboundRequests;
        private BackendEventDispatcher _eventDispatcher;

        /// <summary>Whether the backend WebSocket connection is currently active.</summary>
        public bool IsBackendConnected => _transport?.IsConnected ?? false;

        private void Awake()
        {
            if (router == null)
                router = FindAnyObjectByType<MCPRouter>();
            // Ensure game loop runs when Unity Editor is in background (needed for MCP automation)
            Application.runInBackground = true;

            // Initialize backend communication helpers
            _transport = new WebSocketTransport(backendUrl);
            _outboundRequests = new OutboundRequestManager();
            _eventDispatcher = new BackendEventDispatcher();
        }

        private void OnEnable()
        {
            if (autoConnectOnStart)
            {
                _transport.Start();
            }
        }

        private void OnDisable()
        {
            _transport.Stop();
            _outboundRequests.CancelAll();
            _eventDispatcher.ClearAllHandlers();
        }

        private void Update()
        {
            // Drain incoming WebSocket messages and dispatch by frame type
            var messages = _transport.DrainMessages();
            if (messages != null)
            {
                foreach (var msg in messages)
                {
                    JObject frame;
                    try
                    {
                        frame = JObject.Parse(msg);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MCPGateway] Failed to parse backend message: {ex.Message}");
                        continue;
                    }

                    string type = frame["type"]?.Value<string>();
                    switch (type)
                    {
                        case "req":
                            HandleInboundBackendRequest(frame);
                            break;
                        case "res":
                            _outboundRequests.HandleResponse(frame);
                            break;
                        case "event":
                            _eventDispatcher.DispatchInbound(frame);
                            break;
                        default:
                            Debug.LogWarning($"[MCPGateway] Unknown backend frame type: {type}");
                            break;
                    }
                }
            }

            // Tick timeout management for outbound requests
            _outboundRequests.Tick();

            // Check for action completion events to send to backend
            string completionEvent = _eventDispatcher.CheckActionCompletion(() => router.GetCurrentAction());
            if (completionEvent != null)
            {
                _transport.Send(completionEvent);
            }
        }

        /// <summary>
        /// Handles an inbound request from the backend WebSocket, translates it to MCP format,
        /// processes it, and sends the response back over the WebSocket.
        /// </summary>
        private void HandleInboundBackendRequest(JObject frame)
        {
            string id = frame["id"]?.Value<string>();
            string method = frame["method"]?.Value<string>();
            JObject reqParams = frame["params"] as JObject ?? new JObject();

            // Map to MCP format expected by ProcessRequest
            var mcpJson = new JObject
            {
                ["tool"] = method,
                ["args"] = reqParams
            };

            MCPResponse response = ProcessRequest(mcpJson.ToString());

            // Wrap MCPResponse into a res frame matching backend protocol
            var resFrame = new JObject
            {
                ["type"] = "res",
                ["id"] = id,
                ["ok"] = response.Ok
            };

            if (response.Ok)
            {
                // For successful responses, send payload data
                // Queries: response.Data contains the result
                // Actions: include action_id and status
                if (response.Data != null)
                    resFrame["data"] = response.Data is JToken token ? token : JToken.FromObject(response.Data);
                else
                    resFrame["data"] = new JObject();

                if (response.ActionId != null)
                    resFrame["data"]["action_id"] = response.ActionId;
                if (response.Status != null)
                    resFrame["data"]["status"] = response.Status;
                if (response.CancelledActionId != null)
                    resFrame["data"]["cancelled_action_id"] = response.CancelledActionId;
            }
            else
            {
                // For errors, send error details
                resFrame["data"] = response.Error != null
                    ? JObject.FromObject(response.Error)
                    : new JObject { ["message"] = "Unknown error" };
            }

            _transport.Send(resFrame.ToString(Newtonsoft.Json.Formatting.None));
        }

        /// <summary>
        /// Sends a request to the backend over WebSocket and invokes a callback with the response.
        /// </summary>
        public void SendToBackend(string method, JObject @params, Action<bool, JObject> onResponse, float timeout = 30f)
        {
            if (!IsBackendConnected)
            {
                onResponse?.Invoke(false, new JObject
                {
                    ["error"] = "Backend is not connected"
                });
                return;
            }

            string reqFrame = _outboundRequests.CreateRequest(method, @params, onResponse, timeout);
            _transport.Send(reqFrame);
        }

        /// <summary>
        /// Sends an event to the backend over WebSocket.
        /// </summary>
        public void SendEventToBackend(string eventName, object data)
        {
            string eventFrame = _eventDispatcher.CreateEvent(eventName, data);
            _transport.Send(eventFrame);
        }

        /// <summary>
        /// Registers a handler for a named backend event.
        /// </summary>
        public void RegisterEventHandler(string eventName, Action<JObject> handler)
        {
            _eventDispatcher.RegisterHandler(eventName, handler);
        }

        /// <summary>
        /// Unregisters a handler for a named backend event.
        /// </summary>
        public void UnregisterEventHandler(string eventName, Action<JObject> handler)
        {
            _eventDispatcher.UnregisterHandler(eventName, handler);
        }

        private void OnDestroy()
        {
            _transport?.Stop();
            _outboundRequests?.CancelAll();
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
