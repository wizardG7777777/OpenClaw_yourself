using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace MCP.Gateway
{
    /// <summary>
    /// Manages outbound requests from Unity to the Python backend.
    /// Handles request ID generation, callback registration, response matching,
    /// and timeout management. Composed by MCPGateway (not a MonoBehaviour).
    /// </summary>
    public class OutboundRequestManager
    {
        /// <summary>
        /// Internal representation of a pending outbound request.
        /// </summary>
        private struct PendingRequest
        {
            public string Id;
            public Action<bool, JObject> Callback;
            public float CreatedAt;
            public float Timeout;
        }

        /// <summary>Default timeout in seconds for outbound requests.</summary>
        public const float DefaultTimeout = 30f;

        private readonly Dictionary<string, PendingRequest> _pending = new Dictionary<string, PendingRequest>();

        // Reusable list to avoid allocations during Tick iteration.
        private readonly List<string> _timedOutIds = new List<string>();

        /// <summary>
        /// Number of in-flight outbound requests.
        /// </summary>
        public int PendingCount => _pending.Count;

        /// <summary>
        /// Creates an outbound request, registers the callback, and returns the
        /// fully assembled JSON frame ready to send over the WebSocket.
        /// </summary>
        /// <param name="method">The method name to invoke on the backend.</param>
        /// <param name="params">Optional JSON parameters for the request.</param>
        /// <param name="onResponse">
        /// Callback invoked when the response arrives or the request times out.
        /// First argument is true on success, false on error/timeout.
        /// Second argument is the data payload (success) or error info (failure).
        /// </param>
        /// <param name="timeout">
        /// Per-request timeout override in seconds. Uses <see cref="DefaultTimeout"/> when omitted.
        /// </param>
        /// <returns>A JSON string representing the request frame.</returns>
        public string CreateRequest(string method, JObject @params, Action<bool, JObject> onResponse,
            float timeout = DefaultTimeout)
        {
            string id = "req_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            var pending = new PendingRequest
            {
                Id = id,
                Callback = onResponse,
                CreatedAt = Time.unscaledTime,
                Timeout = timeout
            };

            _pending[id] = pending;

            var frame = new JObject
            {
                ["type"] = "req",
                ["id"] = id,
                ["method"] = method,
                ["params"] = @params ?? new JObject()
            };

            return frame.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Attempts to match an incoming response frame to a pending request.
        /// If matched, invokes the registered callback and removes the entry.
        /// </summary>
        /// <param name="resFrame">The parsed JSON response frame from the backend.</param>
        /// <returns>True if the response matched a pending request; false otherwise.</returns>
        public bool HandleResponse(JObject resFrame)
        {
            if (resFrame == null)
                return false;

            string id = resFrame["id"]?.Value<string>();
            if (string.IsNullOrEmpty(id))
                return false;

            if (!_pending.TryGetValue(id, out PendingRequest pending))
                return false;

            _pending.Remove(id);

            bool ok = resFrame["ok"]?.Value<bool>() ?? false;
            JObject data = resFrame["data"] as JObject ?? new JObject();

            try
            {
                pending.Callback?.Invoke(ok, data);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OutboundRequestManager] Callback threw for {id}: {ex.Message}");
            }

            return true;
        }

        /// <summary>
        /// Called every frame by MCPGateway to expire timed-out requests.
        /// Invokes each timed-out callback with a failure error object.
        /// </summary>
        public void Tick()
        {
            if (_pending.Count == 0)
                return;

            float now = Time.unscaledTime;
            _timedOutIds.Clear();

            foreach (var kvp in _pending)
            {
                if (now - kvp.Value.CreatedAt > kvp.Value.Timeout)
                    _timedOutIds.Add(kvp.Key);
            }

            for (int i = 0; i < _timedOutIds.Count; i++)
            {
                string id = _timedOutIds[i];
                if (_pending.TryGetValue(id, out PendingRequest pending))
                {
                    _pending.Remove(id);
                    Debug.LogWarning($"[OutboundRequestManager] Request {id} timed out after {pending.Timeout}s.");

                    var error = new JObject
                    {
                        ["code"] = "TIMEOUT",
                        ["message"] = $"Request timed out after {pending.Timeout}s."
                    };

                    try
                    {
                        pending.Callback?.Invoke(false, error);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[OutboundRequestManager] Timeout callback threw for {id}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Cancels all pending requests, invoking each callback with a cancellation error.
        /// Call when the WebSocket disconnects or MCPGateway is destroyed.
        /// </summary>
        public void CancelAll()
        {
            if (_pending.Count == 0)
                return;

            // Copy values to avoid modifying the dictionary during iteration.
            var snapshot = new List<PendingRequest>(_pending.Values);
            _pending.Clear();

            var error = new JObject
            {
                ["code"] = "CANCELLED",
                ["message"] = "Request cancelled due to shutdown or disconnect."
            };

            for (int i = 0; i < snapshot.Count; i++)
            {
                try
                {
                    snapshot[i].Callback?.Invoke(false, error);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[OutboundRequestManager] Cancel callback threw for {snapshot[i].Id}: {ex.Message}");
                }
            }
        }
    }
}
