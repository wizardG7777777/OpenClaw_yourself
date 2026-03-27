using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MCP.Core;

namespace MCP.Gateway
{
    /// <summary>
    /// Publish-subscribe event dispatch system for events flowing between Unity and the Python backend.
    /// Composed by MCPGateway; not a MonoBehaviour.
    /// </summary>
    public class BackendEventDispatcher
    {
        private readonly Dictionary<string, List<Action<JObject>>> _handlers =
            new Dictionary<string, List<Action<JObject>>>();

        private readonly HashSet<string> _notifiedActionIds = new HashSet<string>();

        // ──────────────────────────────────────────────
        //  Inbound handler registration
        // ──────────────────────────────────────────────

        /// <summary>
        /// Register a handler for an inbound event from the backend.
        /// Multiple handlers can be registered for the same event name.
        /// </summary>
        /// <param name="eventName">The event name to listen for (e.g. "character_move").</param>
        /// <param name="handler">Callback invoked with the event's <c>data</c> payload.</param>
        public void RegisterHandler(string eventName, Action<JObject> handler)
        {
            if (!_handlers.TryGetValue(eventName, out var list))
            {
                list = new List<Action<JObject>>();
                _handlers[eventName] = list;
            }

            list.Add(handler);
        }

        /// <summary>
        /// Remove a specific handler previously registered for an event name.
        /// </summary>
        /// <param name="eventName">The event name the handler was registered under.</param>
        /// <param name="handler">The handler to remove.</param>
        public void UnregisterHandler(string eventName, Action<JObject> handler)
        {
            if (_handlers.TryGetValue(eventName, out var list))
            {
                list.Remove(handler);

                if (list.Count == 0)
                {
                    _handlers.Remove(eventName);
                }
            }
        }

        // ──────────────────────────────────────────────
        //  Inbound event dispatch
        // ──────────────────────────────────────────────

        /// <summary>
        /// Dispatch an inbound event frame received from the backend.
        /// Extracts the <c>event</c> name and <c>data</c> payload, then invokes all
        /// registered handlers. Exceptions from individual handlers are caught and
        /// logged without interrupting dispatch to remaining handlers.
        /// </summary>
        /// <param name="eventFrame">
        /// JSON frame in the form:
        /// <c>{"type": "event", "event": "character_move", "data": {...}}</c>
        /// </param>
        public void DispatchInbound(JObject eventFrame)
        {
            string eventName = eventFrame.Value<string>("event");
            JObject data = eventFrame.Value<JObject>("data") ?? new JObject();

            if (!_handlers.TryGetValue(eventName, out var list) || list.Count == 0)
            {
                Debug.Log($"[BackendEventDispatcher] No handlers registered for event '{eventName}'");
                return;
            }

            // Iterate over a copy so handlers can safely unregister during dispatch.
            var snapshot = new List<Action<JObject>>(list);
            foreach (var handler in snapshot)
            {
                try
                {
                    handler.Invoke(data);
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[BackendEventDispatcher] Handler threw exception for event '{eventName}': {ex}");
                }
            }
        }

        // ──────────────────────────────────────────────
        //  Outbound event creation
        // ──────────────────────────────────────────────

        /// <summary>
        /// Create a fully assembled outbound event frame as a JSON string, ready to send
        /// to the backend.
        /// </summary>
        /// <param name="eventName">Event name (e.g. "action_completed").</param>
        /// <param name="data">Payload object; serialised via <c>JObject.FromObject</c>.</param>
        /// <returns>JSON string of the event frame.</returns>
        public string CreateEvent(string eventName, object data)
        {
            var frame = new JObject
            {
                ["type"] = "event",
                ["event"] = eventName,
                ["data"] = JObject.FromObject(data)
            };

            return frame.ToString(Newtonsoft.Json.Formatting.None);
        }

        // ──────────────────────────────────────────────
        //  Action completion monitoring
        // ──────────────────────────────────────────────

        /// <summary>
        /// Check whether the current action has entered a terminal state and, if so,
        /// produce an outbound event frame for the backend. Called by MCPGateway in
        /// <c>Update()</c>.
        /// </summary>
        /// <param name="getCurrentAction">
        /// Delegate that returns the current <see cref="ActionInstance"/>, or <c>null</c>
        /// if no action is active.
        /// </param>
        /// <returns>
        /// A JSON event frame string to send to the backend, or <c>null</c> if there is
        /// nothing to report.
        /// </returns>
        public string CheckActionCompletion(Func<ActionInstance> getCurrentAction)
        {
            ActionInstance action = getCurrentAction();
            if (action == null)
            {
                return null;
            }

            if (_notifiedActionIds.Contains(action.ActionId))
            {
                return null;
            }

            string eventName;
            switch (action.Status)
            {
                case ActionStatus.Completed:
                    eventName = "action_completed";
                    break;
                case ActionStatus.Failed:
                    eventName = "action_failed";
                    break;
                case ActionStatus.Cancelled:
                    eventName = "action_cancelled";
                    break;
                default:
                    return null;
            }

            _notifiedActionIds.Add(action.ActionId);

            var payload = new JObject
            {
                ["action_id"] = action.ActionId,
                ["tool"] = action.ToolName,
                ["status"] = action.Status.ToString()
            };

            if (action.Status == ActionStatus.Completed && action.Result != null)
            {
                payload["result"] = action.Result is JToken token
                    ? token
                    : JToken.FromObject(action.Result);
            }

            if (action.Status == ActionStatus.Failed && action.ErrorCode != null)
            {
                payload["error_code"] = action.ErrorCode;
            }

            return CreateEvent(eventName, payload);
        }

        /// <summary>
        /// Clear the set of already-notified action IDs (e.g. on reconnect).
        /// </summary>
        public void ResetNotified()
        {
            _notifiedActionIds.Clear();
        }

        // ──────────────────────────────────────────────
        //  Cleanup
        // ──────────────────────────────────────────────

        /// <summary>
        /// Remove all registered event handlers.
        /// </summary>
        public void ClearAllHandlers()
        {
            _handlers.Clear();
        }
    }
}
