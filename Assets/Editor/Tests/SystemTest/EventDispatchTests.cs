using System;
using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.TestTools;
using MCP.Core;
using MCP.Gateway;

namespace Tests.SystemTest
{
    /// <summary>
    /// Tests for BackendEventDispatcher: inbound pub-sub dispatch, outbound event
    /// creation, and action-completion monitoring.
    /// </summary>
    public class EventDispatchTests
    {
        private BackendEventDispatcher _dispatcher;

        [SetUp]
        public void SetUp()
        {
            _dispatcher = new BackendEventDispatcher();
        }

        [TearDown]
        public void TearDown()
        {
            _dispatcher.ClearAllHandlers();
            _dispatcher = null;
        }

        // ──────────────────────────────────────────────────────────────
        //  1. RegisterHandler + DispatchInbound
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void RegisterHandler_DispatchInbound_InvokesHandlerWithCorrectData()
        {
            // Arrange
            JObject received = null;
            _dispatcher.RegisterHandler("character_move", data => received = data);

            var frame = new JObject
            {
                ["type"] = "event",
                ["event"] = "character_move",
                ["data"] = new JObject { ["x"] = 3, ["y"] = 7 }
            };

            // Act
            _dispatcher.DispatchInbound(frame);

            // Assert
            Assert.IsNotNull(received, "Handler should have been called");
            Assert.AreEqual(3, received.Value<int>("x"));
            Assert.AreEqual(7, received.Value<int>("y"));
        }

        // ──────────────────────────────────────────────────────────────
        //  2. Multiple handlers for the same event all get invoked
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void DispatchInbound_MultipleHandlersSameEvent_AllHandlersInvoked()
        {
            // Arrange
            var callLog = new List<string>();
            _dispatcher.RegisterHandler("test_event", _ => callLog.Add("handler_a"));
            _dispatcher.RegisterHandler("test_event", _ => callLog.Add("handler_b"));
            _dispatcher.RegisterHandler("test_event", _ => callLog.Add("handler_c"));

            var frame = new JObject
            {
                ["type"] = "event",
                ["event"] = "test_event",
                ["data"] = new JObject()
            };

            // Act
            _dispatcher.DispatchInbound(frame);

            // Assert
            Assert.AreEqual(3, callLog.Count, "All three handlers should be called");
            Assert.Contains("handler_a", callLog);
            Assert.Contains("handler_b", callLog);
            Assert.Contains("handler_c", callLog);
        }

        // ──────────────────────────────────────────────────────────────
        //  3. Unregistered event logs warning but doesn't throw
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void DispatchInbound_UnregisteredEvent_DoesNotThrow()
        {
            // Arrange
            var frame = new JObject
            {
                ["type"] = "event",
                ["event"] = "unknown_event",
                ["data"] = new JObject()
            };

            // Act & Assert — must not throw
            Assert.DoesNotThrow(() => _dispatcher.DispatchInbound(frame));
        }

        // ──────────────────────────────────────────────────────────────
        //  4. UnregisterHandler removes the handler correctly
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void UnregisterHandler_RemovesHandler_NotCalledAfterUnregister()
        {
            // Arrange
            int callCount = 0;
            Action<JObject> handler = _ => callCount++;
            _dispatcher.RegisterHandler("player_update", handler);

            var frame = new JObject
            {
                ["type"] = "event",
                ["event"] = "player_update",
                ["data"] = new JObject()
            };

            // Verify it fires before unregistering
            _dispatcher.DispatchInbound(frame);
            Assert.AreEqual(1, callCount, "Handler should fire once before unregistering");

            // Act
            _dispatcher.UnregisterHandler("player_update", handler);
            _dispatcher.DispatchInbound(frame);

            // Assert
            Assert.AreEqual(1, callCount, "Handler should NOT fire again after being unregistered");
        }

        [Test]
        public void UnregisterHandler_LastHandlerRemoved_DispatchDoesNotThrow()
        {
            // Arrange
            Action<JObject> handler = _ => { };
            _dispatcher.RegisterHandler("solo_event", handler);
            _dispatcher.UnregisterHandler("solo_event", handler);

            var frame = new JObject
            {
                ["type"] = "event",
                ["event"] = "solo_event",
                ["data"] = new JObject()
            };

            // After removing the only handler, dispatch to that event should log a
            // warning (no handlers) but must not throw.
            Assert.DoesNotThrow(() => _dispatcher.DispatchInbound(frame));
        }

        // ──────────────────────────────────────────────────────────────
        //  5. CreateEvent produces valid JSON frame
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void CreateEvent_ProducesValidJsonFrame_WithCorrectFields()
        {
            // Arrange
            var payload = new { action_id = "act_001", status = "Completed" };

            // Act
            string json = _dispatcher.CreateEvent("action_completed", payload);
            var frame = JObject.Parse(json);

            // Assert
            Assert.AreEqual("event", frame.Value<string>("type"),
                "Frame 'type' field must be \"event\"");
            Assert.AreEqual("action_completed", frame.Value<string>("event"),
                "Frame 'event' field must match the supplied event name");
            Assert.IsNotNull(frame["data"], "Frame must contain a 'data' field");
            Assert.AreEqual("act_001", frame["data"].Value<string>("action_id"));
            Assert.AreEqual("Completed", frame["data"].Value<string>("status"));
        }

        // ──────────────────────────────────────────────────────────────
        //  6. CheckActionCompletion returns null for Running actions
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void CheckActionCompletion_RunningAction_ReturnsNull()
        {
            // Arrange
            var action = new ActionInstance
            {
                ActionId = "act_run_01",
                ToolName = "move_to",
                Status = ActionStatus.Running
            };

            // Act
            string result = _dispatcher.CheckActionCompletion(() => action);

            // Assert
            Assert.IsNull(result, "Running action should produce no outbound event");
        }

        // ──────────────────────────────────────────────────────────────
        //  7. CheckActionCompletion returns "action_completed" for Completed actions
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void CheckActionCompletion_CompletedAction_ReturnsActionCompletedEvent()
        {
            // Arrange
            var resultPayload = new { position = "lobby" };
            var action = new ActionInstance
            {
                ActionId = "act_done_01",
                ToolName = "move_to",
                Status = ActionStatus.Completed,
                Result = resultPayload
            };

            // Act
            string json = _dispatcher.CheckActionCompletion(() => action);
            Assert.IsNotNull(json, "Completed action should produce an outbound event");

            var frame = JObject.Parse(json);

            // Assert frame structure
            Assert.AreEqual("event", frame.Value<string>("type"));
            Assert.AreEqual("action_completed", frame.Value<string>("event"));

            var data = frame["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.AreEqual("act_done_01", data.Value<string>("action_id"));
            Assert.AreEqual("move_to", data.Value<string>("tool"));
            Assert.AreEqual("Completed", data.Value<string>("status"));
            Assert.IsNotNull(data["result"], "Completed event must include 'result'");
        }

        // ──────────────────────────────────────────────────────────────
        //  8. CheckActionCompletion returns "action_failed" with error_code
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void CheckActionCompletion_FailedAction_ReturnsActionFailedEventWithErrorCode()
        {
            // Arrange
            var action = new ActionInstance
            {
                ActionId = "act_fail_01",
                ToolName = "interact_with",
                Status = ActionStatus.Failed,
                ErrorCode = "TARGET_NOT_FOUND"
            };

            // Act
            string json = _dispatcher.CheckActionCompletion(() => action);
            Assert.IsNotNull(json, "Failed action should produce an outbound event");

            var frame = JObject.Parse(json);

            // Assert
            Assert.AreEqual("action_failed", frame.Value<string>("event"));

            var data = frame["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.AreEqual("act_fail_01", data.Value<string>("action_id"));
            Assert.AreEqual("Failed", data.Value<string>("status"));
            Assert.AreEqual("TARGET_NOT_FOUND", data.Value<string>("error_code"),
                "Failed event must include 'error_code'");
        }

        // ──────────────────────────────────────────────────────────────
        //  9. CheckActionCompletion returns "action_cancelled" for Cancelled actions
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void CheckActionCompletion_CancelledAction_ReturnsActionCancelledEvent()
        {
            // Arrange
            var action = new ActionInstance
            {
                ActionId = "act_cancel_01",
                ToolName = "move_to",
                Status = ActionStatus.Cancelled
            };

            // Act
            string json = _dispatcher.CheckActionCompletion(() => action);
            Assert.IsNotNull(json, "Cancelled action should produce an outbound event");

            var frame = JObject.Parse(json);

            // Assert
            Assert.AreEqual("action_cancelled", frame.Value<string>("event"));

            var data = frame["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.AreEqual("act_cancel_01", data.Value<string>("action_id"));
            Assert.AreEqual("Cancelled", data.Value<string>("status"));
        }

        // ──────────────────────────────────────────────────────────────
        //  10. CheckActionCompletion does not notify the same action twice
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void CheckActionCompletion_SameActionCheckedTwice_SecondCallReturnsNull()
        {
            // Arrange
            var action = new ActionInstance
            {
                ActionId = "act_dedup_01",
                ToolName = "get_inventory",
                Status = ActionStatus.Completed,
                Result = new { items = 3 }
            };

            // Act — first call should produce the event
            string firstResult = _dispatcher.CheckActionCompletion(() => action);

            // Act — second call for the same action id must be suppressed
            string secondResult = _dispatcher.CheckActionCompletion(() => action);

            // Assert
            Assert.IsNotNull(firstResult, "First call should return an event frame");
            Assert.IsNull(secondResult,
                "Second call for the same action_id must return null (already notified)");
        }

        [Test]
        public void CheckActionCompletion_DifferentActionIds_BothNotified()
        {
            // Arrange
            var action1 = new ActionInstance
            {
                ActionId = "act_a",
                ToolName = "move_to",
                Status = ActionStatus.Completed,
                Result = new { }
            };
            var action2 = new ActionInstance
            {
                ActionId = "act_b",
                ToolName = "move_to",
                Status = ActionStatus.Completed,
                Result = new { }
            };

            // Act
            string result1 = _dispatcher.CheckActionCompletion(() => action1);
            string result2 = _dispatcher.CheckActionCompletion(() => action2);

            // Assert — two distinct IDs: both should produce events
            Assert.IsNotNull(result1, "First distinct action should produce an event");
            Assert.IsNotNull(result2, "Second distinct action should produce an event");
        }

        // ──────────────────────────────────────────────────────────────
        //  11. Handler exception doesn't prevent other handlers from being called
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void DispatchInbound_HandlerThrows_RemainingHandlersStillInvoked()
        {
            // Arrange
            var callLog = new List<string>();

            _dispatcher.RegisterHandler("fault_event", _ =>
            {
                callLog.Add("before_throw");
                throw new InvalidOperationException("Simulated handler failure");
            });

            _dispatcher.RegisterHandler("fault_event", _ => callLog.Add("after_throw"));

            var frame = new JObject
            {
                ["type"] = "event",
                ["event"] = "fault_event",
                ["data"] = new JObject()
            };

            // Expect the LogError from BackendEventDispatcher
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Handler threw exception"));

            // Act — must not propagate the exception
            Assert.DoesNotThrow(() => _dispatcher.DispatchInbound(frame));

            // Assert — both handlers ran (second one was not skipped)
            Assert.Contains("before_throw", callLog,
                "Handler before the throw should have been called");
            Assert.Contains("after_throw", callLog,
                "Handler after the throw should still have been called");
        }

        // ──────────────────────────────────────────────────────────────
        //  Bonus edge cases
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void CheckActionCompletion_NullAction_ReturnsNull()
        {
            // Act
            string result = _dispatcher.CheckActionCompletion(() => null);

            // Assert
            Assert.IsNull(result, "Null action should return null");
        }

        [Test]
        public void CheckActionCompletion_CompletedActionWithNullResult_EventHasNoResultField()
        {
            // Arrange — Completed but Result is null
            var action = new ActionInstance
            {
                ActionId = "act_no_result",
                ToolName = "ping",
                Status = ActionStatus.Completed,
                Result = null
            };

            // Act
            string json = _dispatcher.CheckActionCompletion(() => action);
            Assert.IsNotNull(json);

            var frame = JObject.Parse(json);
            var data = frame["data"] as JObject;

            // Assert — no "result" key when Result is null
            Assert.IsFalse(data.ContainsKey("result"),
                "Completed event with null Result should omit the 'result' field");
        }

        [Test]
        public void ResetNotified_AllowsActionToBeNotifiedAgain()
        {
            // Arrange
            var action = new ActionInstance
            {
                ActionId = "act_reset_01",
                ToolName = "move_to",
                Status = ActionStatus.Completed,
                Result = new { }
            };

            // First notification
            string firstResult = _dispatcher.CheckActionCompletion(() => action);
            Assert.IsNotNull(firstResult);

            // Suppress check
            string suppressedResult = _dispatcher.CheckActionCompletion(() => action);
            Assert.IsNull(suppressedResult);

            // Act — reset the notified set
            _dispatcher.ResetNotified();

            // After reset the action should be notifiable again
            string afterResetResult = _dispatcher.CheckActionCompletion(() => action);
            Assert.IsNotNull(afterResetResult,
                "After ResetNotified the same action should be notifiable once more");
        }
    }
}
