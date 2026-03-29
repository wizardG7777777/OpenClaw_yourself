using System;
using System.Collections.Generic;
using System.Reflection;
using MCP.Core;
using MCP.Entity;
using MCP.Gateway;
using MCP.Router;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Tests.SystemTest
{
    /// <summary>
    /// Tests the complete MCP gateway flow by orchestrating the individual components
    /// that MCPGateway composes (BackendEventDispatcher, OutboundRequestManager, MCPRouter)
    /// in the same way MCPGateway.Update() would, using MockTransport to simulate backend
    /// communication without a live WebSocket connection.
    /// </summary>
    public class GatewayFlowTests
    {
        // ── scene objects ──────────────────────────────────────────────────────────
        private GameObject _itemRegistryGo;
        private GameObject _entityRegistryGo;
        private EntityRegistry _entityRegistry;
        private GameObject _routerGo;
        private MCPRouter _router;
        private GameObject _playerGo;

        // ── components under test ─────────────────────────────────────────────────
        private BackendEventDispatcher _eventDispatcher;
        private OutboundRequestManager _outboundRequests;
        private MockTransport _transport;

        // ── helper: gateway for ProcessRequest / HandleInboundBackendRequest tests ─
        private GameObject _gatewayGo;
        private MCPGateway _gateway;

        [SetUp]
        public void SetUp()
        {
            // --- ItemRegistry ---
            _itemRegistryGo = new GameObject("ItemRegistry");
            var itemRegistry = _itemRegistryGo.AddComponent<ItemRegistry>();
            typeof(ItemRegistry)
                .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                ?.GetSetMethod(true)
                ?.Invoke(null, new object[] { itemRegistry });
            typeof(ItemRegistry)
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(itemRegistry, null);

            // --- EntityRegistry ---
            _entityRegistryGo = new GameObject("EntityRegistry");
            _entityRegistry = _entityRegistryGo.AddComponent<EntityRegistry>();
            BindEntityRegistryInstance(_entityRegistry);

            // --- MCPRouter ---
            _routerGo = new GameObject("MCPRouter");
            _router = _routerGo.AddComponent<MCPRouter>();

            // --- Player ---
            _playerGo = new GameObject("Player");
            _playerGo.tag = "Player";
            _playerGo.transform.position = Vector3.zero;

            // --- Gateway helpers (standalone, no WebSocket) ---
            _eventDispatcher = new BackendEventDispatcher();
            _outboundRequests = new OutboundRequestManager();
            _transport = new MockTransport();

            // --- MCPGateway MonoBehaviour (needed for ProcessRequest tests) ---
            _gatewayGo = new GameObject("MCPGateway");
            _gateway = _gatewayGo.AddComponent<MCPGateway>();
            // Inject router so the gateway does not search the scene
            typeof(MCPGateway)
                .GetField("router", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(_gateway, _router);
        }

        [TearDown]
        public void TearDown()
        {
            if (_gatewayGo != null) UnityEngine.Object.DestroyImmediate(_gatewayGo);
            if (_playerGo != null) UnityEngine.Object.DestroyImmediate(_playerGo);
            if (_routerGo != null) UnityEngine.Object.DestroyImmediate(_routerGo);
            if (_entityRegistryGo != null) UnityEngine.Object.DestroyImmediate(_entityRegistryGo);
            if (_itemRegistryGo != null) UnityEngine.Object.DestroyImmediate(_itemRegistryGo);

            typeof(ItemRegistry)
                .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                ?.GetSetMethod(true)
                ?.Invoke(null, new object[] { null });

            BindEntityRegistryInstance(null);

            // Destroy any EntityIdentity objects left in the scene
            var remaining = UnityEngine.Object.FindObjectsByType<EntityIdentity>(FindObjectsSortMode.None);
            foreach (var e in remaining)
                UnityEngine.Object.DestroyImmediate(e.gameObject);
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  Test 1 — Full query flow
        //  Simulate inbound req frame for a query tool → process through router →
        //  verify that the resulting res frame is ok=true and carries the expected data.
        // ══════════════════════════════════════════════════════════════════════════
        [Test]
        public void FullQueryFlow_InboundReqFrame_ProducesOkResFrame()
        {
            // Arrange: build an inbound backend request frame for get_inventory
            var inboundFrame = new JObject
            {
                ["type"] = "req",
                ["id"] = "test_req_001",
                ["method"] = "get_inventory",
                ["params"] = new JObject()
            };

            // Inject it so DrainMessages returns it
            _transport.InjectIncoming(inboundFrame.ToString(Newtonsoft.Json.Formatting.None));

            // Act: process exactly as MCPGateway.Update() would
            foreach (var msg in _transport.DrainMessages())
            {
                var frame = JObject.Parse(msg);
                if (frame["type"]?.Value<string>() == "req")
                    HandleInboundBackendRequest(frame);
            }

            // Assert: exactly one response was sent back
            Assert.AreEqual(1, _transport.SentMessages.Count, "Expected exactly one response frame");

            var resFrame = _transport.GetLastSentFrame();
            Assert.IsNotNull(resFrame, "Response frame must not be null");
            Assert.AreEqual("res", resFrame["type"]?.Value<string>(), "Frame type must be 'res'");
            Assert.AreEqual("test_req_001", resFrame["id"]?.Value<string>(), "Response must echo back the request ID");
            Assert.IsTrue(resFrame["ok"]?.Value<bool>() ?? false, "Query response must be ok=true");

            var data = resFrame["data"] as JObject;
            Assert.IsNotNull(data, "Response data must be a JSON object");
            Assert.AreEqual("get_inventory", data["tool"]?.Value<string>(), "Data must contain the tool name");
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  Test 2 — Full action flow
        //  Simulate inbound req frame for move_to → verify res frame has an
        //  action_id and status="running".
        // ══════════════════════════════════════════════════════════════════════════
        [Test]
        public void FullActionFlow_InboundReqFrameForMoveTo_ProducesRunningResFrame()
        {
            // Arrange: register a target entity so the router can resolve it
            var targetGo = CreateEntity("lamp_01", "台灯", new Vector3(3f, 0f, 0f));
            try
            {
                var inboundFrame = new JObject
                {
                    ["type"] = "req",
                    ["id"] = "test_req_002",
                    ["method"] = "move_to",
                    ["params"] = new JObject { ["target_id"] = "lamp_01" }
                };

                _transport.InjectIncoming(inboundFrame.ToString(Newtonsoft.Json.Formatting.None));

                // Act
                foreach (var msg in _transport.DrainMessages())
                {
                    var frame = JObject.Parse(msg);
                    if (frame["type"]?.Value<string>() == "req")
                        HandleInboundBackendRequest(frame);
                }

                // Assert
                Assert.AreEqual(1, _transport.SentMessages.Count, "Expected exactly one response frame");

                var resFrame = _transport.GetLastSentFrame();
                Assert.IsNotNull(resFrame);
                Assert.AreEqual("res", resFrame["type"]?.Value<string>());
                Assert.AreEqual("test_req_002", resFrame["id"]?.Value<string>());
                Assert.IsTrue(resFrame["ok"]?.Value<bool>() ?? false, "Action response must be ok=true");

                var data = resFrame["data"] as JObject;
                Assert.IsNotNull(data, "Response data must be present");
                Assert.IsNotNull(data["action_id"]?.Value<string>(), "Response must include action_id");
                Assert.IsFalse(string.IsNullOrEmpty(data["action_id"]?.Value<string>()), "action_id must not be empty");
                Assert.AreEqual("running", data["status"]?.Value<string>(), "Action status must be 'running'");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(targetGo);
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  Test 3 — Action completion flow
        //  Manually create an ActionInstance, set it to Completed, call
        //  CheckActionCompletion → verify an "action_completed" event frame is
        //  produced and contains the expected fields.
        // ══════════════════════════════════════════════════════════════════════════
        [Test]
        public void ActionCompletionFlow_CompletedAction_ProducesActionCompletedEventFrame()
        {
            // Arrange: create an action that has already completed
            var completedAction = new ActionInstance
            {
                ActionId = "act_complete_001",
                ToolName = "move_to",
                Status = ActionStatus.Completed,
                Result = new Dictionary<string, object>
                {
                    { "arrived", true },
                    { "position", "lamp_01" }
                }
            };

            // Act: simulate MCPGateway.Update() calling CheckActionCompletion
            string eventJson = _eventDispatcher.CheckActionCompletion(() => completedAction);

            // If an event was produced, forward it to the transport as Update() would
            if (eventJson != null)
                _transport.Send(eventJson);

            // Assert: one event frame was sent
            Assert.IsNotNull(eventJson, "CheckActionCompletion must return an event frame for a Completed action");
            Assert.AreEqual(1, _transport.SentMessages.Count);

            var eventFrame = _transport.GetLastSentFrame();
            Assert.IsNotNull(eventFrame);
            Assert.AreEqual("event", eventFrame["type"]?.Value<string>(), "Frame type must be 'event'");
            Assert.AreEqual("action_completed", eventFrame["event"]?.Value<string>(), "Event name must be 'action_completed'");

            var payload = eventFrame["data"] as JObject;
            Assert.IsNotNull(payload);
            Assert.AreEqual("act_complete_001", payload["action_id"]?.Value<string>());
            Assert.AreEqual("move_to", payload["tool"]?.Value<string>());
            Assert.AreEqual("Completed", payload["status"]?.Value<string>());
            Assert.IsNotNull(payload["result"], "Payload must include result for a Completed action");
        }

        [Test]
        public void ActionCompletionFlow_CompletedAction_IsNotReportedTwice()
        {
            // Arrange
            var completedAction = new ActionInstance
            {
                ActionId = "act_complete_002",
                ToolName = "interact_with",
                Status = ActionStatus.Completed
            };

            // Act: check twice
            string first = _eventDispatcher.CheckActionCompletion(() => completedAction);
            string second = _eventDispatcher.CheckActionCompletion(() => completedAction);

            // Assert
            Assert.IsNotNull(first, "First check must produce an event");
            Assert.IsNull(second, "Second check must return null (already notified)");
        }

        [Test]
        public void ActionCompletionFlow_FailedAction_ProducesActionFailedEvent()
        {
            // Arrange
            var failedAction = new ActionInstance
            {
                ActionId = "act_fail_001",
                ToolName = "move_to",
                Status = ActionStatus.Failed,
                ErrorCode = ErrorCodes.ACTION_TIMEOUT
            };

            // Act
            string eventJson = _eventDispatcher.CheckActionCompletion(() => failedAction);

            // Assert
            Assert.IsNotNull(eventJson);
            var frame = JObject.Parse(eventJson);
            Assert.AreEqual("action_failed", frame["event"]?.Value<string>());

            var payload = frame["data"] as JObject;
            Assert.IsNotNull(payload);
            Assert.AreEqual(ErrorCodes.ACTION_TIMEOUT, payload["error_code"]?.Value<string>());
        }

        [Test]
        public void ActionCompletionFlow_RunningAction_ProducesNoEvent()
        {
            // Arrange: still-running action
            var runningAction = new ActionInstance
            {
                ActionId = "act_run_001",
                ToolName = "move_to",
                Status = ActionStatus.Running
            };

            // Act
            string eventJson = _eventDispatcher.CheckActionCompletion(() => runningAction);

            // Assert
            Assert.IsNull(eventJson, "A running action must produce no completion event");
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  Test 4 — Event handler registration
        //  Register a handler for "character_state_changed", dispatch an event frame
        //  through DispatchInbound, and verify the handler was invoked with the
        //  correct data payload.
        // ══════════════════════════════════════════════════════════════════════════
        [Test]
        public void EventHandlerRegistration_DispatchInbound_HandlerReceivesData()
        {
            // Arrange
            JObject receivedData = null;
            Action<JObject> handler = data => receivedData = data;
            _eventDispatcher.RegisterHandler("character_state_changed", handler);

            var eventFrame = new JObject
            {
                ["type"] = "event",
                ["event"] = "character_state_changed",
                ["data"] = new JObject
                {
                    ["character_id"] = "npc_alice",
                    ["new_state"] = "idle"
                }
            };

            // Act
            _eventDispatcher.DispatchInbound(eventFrame);

            // Assert
            Assert.IsNotNull(receivedData, "Handler must have been invoked");
            Assert.AreEqual("npc_alice", receivedData["character_id"]?.Value<string>());
            Assert.AreEqual("idle", receivedData["new_state"]?.Value<string>());
        }

        [Test]
        public void EventHandlerRegistration_MultipleHandlers_AllReceiveData()
        {
            // Arrange
            int callCount = 0;
            _eventDispatcher.RegisterHandler("character_state_changed", _ => callCount++);
            _eventDispatcher.RegisterHandler("character_state_changed", _ => callCount++);

            var eventFrame = new JObject
            {
                ["type"] = "event",
                ["event"] = "character_state_changed",
                ["data"] = new JObject()
            };

            // Act
            _eventDispatcher.DispatchInbound(eventFrame);

            // Assert
            Assert.AreEqual(2, callCount, "Both handlers must be invoked");
        }

        [Test]
        public void EventHandlerRegistration_UnregisterHandler_HandlerNoLongerReceivesData()
        {
            // Arrange
            int callCount = 0;
            Action<JObject> handler = _ => callCount++;
            _eventDispatcher.RegisterHandler("character_state_changed", handler);
            _eventDispatcher.UnregisterHandler("character_state_changed", handler);

            var eventFrame = new JObject
            {
                ["type"] = "event",
                ["event"] = "character_state_changed",
                ["data"] = new JObject()
            };

            // Act
            _eventDispatcher.DispatchInbound(eventFrame);

            // Assert
            Assert.AreEqual(0, callCount, "Unregistered handler must not be invoked");
        }

        [Test]
        public void EventHandlerRegistration_NoHandlerRegistered_DispatchSucceedsWithoutException()
        {
            // Arrange: no handlers registered for this event
            var eventFrame = new JObject
            {
                ["type"] = "event",
                ["event"] = "unknown_event",
                ["data"] = new JObject()
            };

            // Act & Assert: must not throw
            Assert.DoesNotThrow(() => _eventDispatcher.DispatchInbound(eventFrame));
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  Test 5 — Outbound request / response round-trip
        //  CreateRequest → extract the generated request ID from the JSON frame →
        //  InjectResponse into the transport → call HandleResponse → verify the
        //  callback is invoked with the correct success flag and data.
        // ══════════════════════════════════════════════════════════════════════════
        [Test]
        public void OutboundRequest_ResponseRoundTrip_CallbackInvokedWithCorrectData()
        {
            // Arrange
            bool? callbackOk = null;
            JObject callbackData = null;

            Action<bool, JObject> onResponse = (ok, data) =>
            {
                callbackOk = ok;
                callbackData = data;
            };

            // Act: create the outbound request frame
            string reqFrameJson = _outboundRequests.CreateRequest(
                "query_npc_state",
                new JObject { ["npc_id"] = "npc_alice" },
                onResponse);

            // Capture the generated request ID by parsing the frame
            var reqFrame = JObject.Parse(reqFrameJson);
            string requestId = reqFrame["id"]?.Value<string>();

            Assert.IsNotNull(requestId, "CreateRequest must produce a frame with an 'id' field");
            Assert.AreEqual("req", reqFrame["type"]?.Value<string>(), "Frame type must be 'req'");
            Assert.AreEqual("query_npc_state", reqFrame["method"]?.Value<string>());
            Assert.AreEqual(1, _outboundRequests.PendingCount, "Exactly one request must be pending");

            // Simulate the backend sending a matching response
            var responseData = new JObject
            {
                ["npc_id"] = "npc_alice",
                ["state"] = "talking"
            };
            _transport.InjectResponse(requestId, ok: true, data: responseData);

            // Drain and route as MCPGateway.Update() would
            foreach (var msg in _transport.DrainMessages())
            {
                var frame = JObject.Parse(msg);
                if (frame["type"]?.Value<string>() == "res")
                    _outboundRequests.HandleResponse(frame);
            }

            // Assert
            Assert.IsTrue(callbackOk.HasValue, "Callback must have been invoked");
            Assert.IsTrue(callbackOk.Value, "Callback must report success");
            Assert.IsNotNull(callbackData);
            Assert.AreEqual("npc_alice", callbackData["npc_id"]?.Value<string>());
            Assert.AreEqual("talking", callbackData["state"]?.Value<string>());
            Assert.AreEqual(0, _outboundRequests.PendingCount, "Pending count must drop to 0 after response");
        }

        [Test]
        public void OutboundRequest_UnmatchedResponse_CallbackNotInvoked()
        {
            // Arrange
            bool callbackInvoked = false;
            _outboundRequests.CreateRequest("some_method", null, (ok, data) => callbackInvoked = true);

            // Inject a response with a different ID
            _transport.InjectResponse("completely_different_id", ok: true, data: new JObject());

            // Act
            foreach (var msg in _transport.DrainMessages())
            {
                var frame = JObject.Parse(msg);
                if (frame["type"]?.Value<string>() == "res")
                    _outboundRequests.HandleResponse(frame);
            }

            // Assert
            Assert.IsFalse(callbackInvoked, "Callback must not be invoked for an unmatched response ID");
            Assert.AreEqual(1, _outboundRequests.PendingCount, "Pending request must still be registered");
        }

        [Test]
        public void OutboundRequest_ErrorResponse_CallbackReceivesFalse()
        {
            // Arrange
            bool? callbackOk = null;
            JObject callbackData = null;

            string reqFrameJson = _outboundRequests.CreateRequest(
                "some_method",
                null,
                (ok, data) => { callbackOk = ok; callbackData = data; });

            var reqFrame = JObject.Parse(reqFrameJson);
            string requestId = reqFrame["id"]?.Value<string>();

            // Inject an error response
            var errorData = new JObject { ["message"] = "NPC not found" };
            _transport.InjectResponse(requestId, ok: false, data: errorData);

            // Act
            foreach (var msg in _transport.DrainMessages())
            {
                var frame = JObject.Parse(msg);
                if (frame["type"]?.Value<string>() == "res")
                    _outboundRequests.HandleResponse(frame);
            }

            // Assert
            Assert.IsTrue(callbackOk.HasValue);
            Assert.IsFalse(callbackOk.Value, "Error response must invoke callback with ok=false");
            Assert.IsNotNull(callbackData);
            Assert.AreEqual("NPC not found", callbackData["message"]?.Value<string>());
        }

        [Test]
        public void OutboundRequest_CancelAll_InvokesCallbacksWithCancelledError()
        {
            // Arrange
            int cancelledCount = 0;
            _outboundRequests.CreateRequest("method_a", null, (ok, data) =>
            {
                if (!ok) cancelledCount++;
            });
            _outboundRequests.CreateRequest("method_b", null, (ok, data) =>
            {
                if (!ok) cancelledCount++;
            });

            Assert.AreEqual(2, _outboundRequests.PendingCount);

            // Act
            _outboundRequests.CancelAll();

            // Assert
            Assert.AreEqual(2, cancelledCount, "Both pending callbacks must be called with ok=false");
            Assert.AreEqual(0, _outboundRequests.PendingCount, "All pending requests must be cleared");
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  Supplementary: CreateEvent produces a well-formed outbound event frame
        // ══════════════════════════════════════════════════════════════════════════
        [Test]
        public void CreateEvent_ProducesWellFormedEventFrame()
        {
            // Act
            string eventJson = _eventDispatcher.CreateEvent("player_moved", new
            {
                position = new { x = 1f, y = 0f, z = 2f }
            });

            // Assert
            Assert.IsNotNull(eventJson);
            var frame = JObject.Parse(eventJson);
            Assert.AreEqual("event", frame["type"]?.Value<string>());
            Assert.AreEqual("player_moved", frame["event"]?.Value<string>());
            Assert.IsNotNull(frame["data"]);
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  Supplementary: Full Update() simulation — inbound events are dispatched
        //  to registered handlers while inbound requests are answered, all in one
        //  drain pass (like a single MCPGateway.Update() call).
        // ══════════════════════════════════════════════════════════════════════════
        [Test]
        public void UpdateSimulation_MixedFrameTypes_RoutedCorrectly()
        {
            // Arrange
            bool queryResponseSent = false;
            bool eventHandlerCalled = false;

            _eventDispatcher.RegisterHandler("world_tick", _ => eventHandlerCalled = true);

            // Inject a req frame and an event frame together
            var reqFrame = new JObject
            {
                ["type"] = "req",
                ["id"] = "mixed_001",
                ["method"] = "get_inventory",
                ["params"] = new JObject()
            };
            var eventFrame = new JObject
            {
                ["type"] = "event",
                ["event"] = "world_tick",
                ["data"] = new JObject()
            };

            _transport.InjectIncoming(reqFrame.ToString(Newtonsoft.Json.Formatting.None));
            _transport.InjectIncoming(eventFrame.ToString(Newtonsoft.Json.Formatting.None));

            // Act: simulate one Update() drain pass
            foreach (var msg in _transport.DrainMessages())
            {
                var frame = JObject.Parse(msg);
                switch (frame["type"]?.Value<string>())
                {
                    case "req":
                        HandleInboundBackendRequest(frame);
                        queryResponseSent = true;
                        break;
                    case "res":
                        _outboundRequests.HandleResponse(frame);
                        break;
                    case "event":
                        _eventDispatcher.DispatchInbound(frame);
                        break;
                }
            }

            // Also tick action completion
            string completionEvent = _eventDispatcher.CheckActionCompletion(() => _router.GetCurrentAction());
            if (completionEvent != null)
                _transport.Send(completionEvent);

            // Assert
            Assert.IsTrue(queryResponseSent, "Req frame must have been processed");
            Assert.IsTrue(eventHandlerCalled, "Event frame must have triggered the registered handler");

            // The res frame from the query should have been sent via HandleInboundBackendRequest
            var sentReqFrames = _transport.GetSentFramesByType("res");
            Assert.AreEqual(1, sentReqFrames.Count, "Exactly one response frame must have been sent");
            Assert.AreEqual("mixed_001", sentReqFrames[0]["id"]?.Value<string>());
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  Private helpers that mirror MCPGateway's private methods
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Mirrors MCPGateway.HandleInboundBackendRequest, using _router and _transport
        /// directly. This avoids needing a live MCPGateway MonoBehaviour (which requires
        /// WebSocketTransport) while testing the same logic path.
        /// </summary>
        private void HandleInboundBackendRequest(JObject frame)
        {
            string id = frame["id"]?.Value<string>();
            string method = frame["method"]?.Value<string>();
            JObject reqParams = frame["params"] as JObject ?? new JObject();

            var mcpJson = new JObject
            {
                ["tool"] = method,
                ["args"] = reqParams
            };

            MCPResponse response = _gateway.ProcessRequest(mcpJson.ToString());

            var resFrame = new JObject
            {
                ["type"] = "res",
                ["id"] = id,
                ["ok"] = response.Ok
            };

            if (response.Ok)
            {
                resFrame["data"] = response.Data != null
                    ? (response.Data is JToken token ? token : JToken.FromObject(response.Data))
                    : new JObject();

                if (response.ActionId != null)
                    resFrame["data"]["action_id"] = response.ActionId;
                if (response.Status != null)
                    resFrame["data"]["status"] = response.Status;
                if (response.CancelledActionId != null)
                    resFrame["data"]["cancelled_action_id"] = response.CancelledActionId;
            }
            else
            {
                resFrame["data"] = response.Error != null
                    ? JObject.FromObject(response.Error)
                    : new JObject { ["message"] = "Unknown error" };
            }

            _transport.Send(resFrame.ToString(Newtonsoft.Json.Formatting.None));
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  Scene / singleton utilities
        // ══════════════════════════════════════════════════════════════════════════

        private static void BindEntityRegistryInstance(EntityRegistry instance)
        {
            typeof(EntityRegistry)
                .GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetSetMethod(true)
                ?.Invoke(null, new object[] { instance });
        }

        private static GameObject CreateEntity(string id, string displayName, Vector3 position,
            string[] aliases = null)
        {
            var go = new GameObject(id);
            go.transform.position = position;
            var identity = go.AddComponent<EntityIdentity>();
            identity.entityId = id;
            identity.displayName = displayName;
            identity.aliases = aliases;
            EntityRegistry.Instance?.Register(identity);
            return go;
        }
    }
}
