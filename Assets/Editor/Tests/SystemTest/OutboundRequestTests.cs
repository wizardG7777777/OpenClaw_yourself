using NUnit.Framework;
using MCP.Gateway;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace Tests.SystemTest
{
    /// <summary>
    /// Tests for the outbound request flow: Unity sends requests to backend and receives responses.
    /// Exercises OutboundRequestManager in isolation using MockTransport for frame inspection.
    /// </summary>
    public class OutboundRequestTests
    {
        private OutboundRequestManager _manager;
        private MockTransport _transport;

        [SetUp]
        public void SetUp()
        {
            _manager = new OutboundRequestManager();
            _transport = new MockTransport();
        }

        [TearDown]
        public void TearDown()
        {
            _manager.CancelAll();
            _transport.Clear();
        }

        // ─────────────────────────────────────────────────────────────────────
        // 1. CreateRequest generates a valid JSON frame
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void CreateRequest_GeneratesValidFrame_WithCorrectFields()
        {
            // Arrange
            var @params = new JObject { ["npc_id"] = "npc_01" };

            // Act
            string json = _manager.CreateRequest("npc.talk", @params, onResponse: null);
            _transport.Send(json);

            // Assert – frame structure
            var frame = _transport.GetLastSentFrame();
            Assert.IsNotNull(frame, "Frame should not be null");
            Assert.AreEqual("req", frame["type"]?.Value<string>(), "type must be 'req'");
            Assert.IsNotNull(frame["id"]?.Value<string>(), "id must be present");
            Assert.AreEqual("npc.talk", frame["method"]?.Value<string>(), "method must match");
            Assert.IsNotNull(frame["params"], "params must be present");
            Assert.AreEqual("npc_01", frame["params"]["npc_id"]?.Value<string>(), "params content must round-trip");
        }

        [Test]
        public void CreateRequest_WithNullParams_SetsEmptyParamsObject()
        {
            // Act
            string json = _manager.CreateRequest("ping", null, onResponse: null);
            _transport.Send(json);

            // Assert
            var frame = _transport.GetLastSentFrame();
            Assert.IsNotNull(frame["params"], "params must not be null even when null was passed");
            Assert.AreEqual(JTokenType.Object, frame["params"].Type, "params must be an object");
            Assert.AreEqual(0, ((JObject)frame["params"]).Count, "params must be empty");
        }

        [Test]
        public void CreateRequest_IncreasesPendingCount()
        {
            // Act
            _manager.CreateRequest("ping", null, onResponse: null);

            // Assert
            Assert.AreEqual(1, _manager.PendingCount);
        }

        [Test]
        public void CreateRequest_UniqueIdsPerCall()
        {
            // Act
            string json1 = _manager.CreateRequest("ping", null, onResponse: null);
            string json2 = _manager.CreateRequest("ping", null, onResponse: null);

            var id1 = JObject.Parse(json1)["id"]?.Value<string>();
            var id2 = JObject.Parse(json2)["id"]?.Value<string>();

            // Assert
            Assert.AreNotEqual(id1, id2, "Each request must get a unique ID");
        }

        // ─────────────────────────────────────────────────────────────────────
        // 2. HandleResponse matches response and invokes callback
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void HandleResponse_MatchesPendingRequest_InvokesCallbackWithOkTrue()
        {
            // Arrange
            bool? receivedOk = null;
            JObject receivedData = null;
            string json = _manager.CreateRequest("game.save", null, (ok, data) =>
            {
                receivedOk = ok;
                receivedData = data;
            });
            string requestId = JObject.Parse(json)["id"].Value<string>();

            var responseData = new JObject { ["slot"] = 1 };

            // Act
            var resFrame = new JObject
            {
                ["type"] = "res",
                ["id"] = requestId,
                ["ok"] = true,
                ["data"] = responseData
            };
            bool matched = _manager.HandleResponse(resFrame);

            // Assert
            Assert.IsTrue(matched, "Response should match the pending request");
            Assert.IsTrue(receivedOk.HasValue, "Callback must have been invoked");
            Assert.IsTrue(receivedOk.Value, "ok must be true");
            Assert.IsNotNull(receivedData);
            Assert.AreEqual(1, receivedData["slot"]?.Value<int>());
        }

        [Test]
        public void HandleResponse_MatchesPendingRequest_InvokesCallbackWithOkFalse()
        {
            // Arrange
            bool? receivedOk = null;
            JObject receivedData = null;
            string json = _manager.CreateRequest("game.save", null, (ok, data) =>
            {
                receivedOk = ok;
                receivedData = data;
            });
            string requestId = JObject.Parse(json)["id"].Value<string>();

            // Act
            var resFrame = new JObject
            {
                ["type"] = "res",
                ["id"] = requestId,
                ["ok"] = false,
                ["data"] = new JObject { ["error"] = "disk_full" }
            };
            _manager.HandleResponse(resFrame);

            // Assert
            Assert.IsFalse(receivedOk.Value, "ok must be false");
            Assert.AreEqual("disk_full", receivedData["error"]?.Value<string>());
        }

        [Test]
        public void HandleResponse_RemovesPendingRequestAfterMatch()
        {
            // Arrange
            string json = _manager.CreateRequest("ping", null, onResponse: null);
            string requestId = JObject.Parse(json)["id"].Value<string>();

            // Act
            _manager.HandleResponse(new JObject { ["id"] = requestId, ["ok"] = true });

            // Assert
            Assert.AreEqual(0, _manager.PendingCount, "Request must be removed after response");
        }

        // ─────────────────────────────────────────────────────────────────────
        // 3. HandleResponse with unknown ID returns false and does not callback
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void HandleResponse_UnknownId_ReturnsFalse()
        {
            // Arrange – register a real request so the dictionary is not empty
            bool callbackInvoked = false;
            _manager.CreateRequest("ping", null, (ok, data) => { callbackInvoked = true; });

            // Act
            var resFrame = new JObject
            {
                ["type"] = "res",
                ["id"] = "req_00000000",   // not a registered ID
                ["ok"] = true
            };
            bool matched = _manager.HandleResponse(resFrame);

            // Assert
            Assert.IsFalse(matched, "HandleResponse should return false for unknown ID");
            Assert.IsFalse(callbackInvoked, "No callback must fire for an unmatched response");
            Assert.AreEqual(1, _manager.PendingCount, "Original pending request must remain");
        }

        [Test]
        public void HandleResponse_NullFrame_ReturnsFalse()
        {
            // Act
            bool matched = _manager.HandleResponse(null);

            // Assert
            Assert.IsFalse(matched);
        }

        [Test]
        public void HandleResponse_MissingIdField_ReturnsFalse()
        {
            // Act
            bool matched = _manager.HandleResponse(new JObject { ["ok"] = true });

            // Assert
            Assert.IsFalse(matched);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 4. Tick expires timed-out requests
        //
        // Time.unscaledTime is 0 in edit mode, so every request created at t=0
        // will appear timed-out as soon as its Timeout <= 0.  We use a custom
        // negative timeout to guarantee expiry on the very first Tick().
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Tick_ExpiresTimedOutRequest_InvokesCallbackWithOkFalse()
        {
            // Arrange – timeout of -1 means already expired at creation time (t=0)
            bool? receivedOk = null;
            JObject receivedError = null;
            _manager.CreateRequest("slow.call", null, (ok, data) =>
            {
                receivedOk = ok;
                receivedError = data;
            }, timeout: -1f);

            // Act
            _manager.Tick();

            // Assert
            Assert.IsTrue(receivedOk.HasValue, "Callback must fire on timeout");
            Assert.IsFalse(receivedOk.Value, "ok must be false for a timeout");
            Assert.IsNotNull(receivedError);
            Assert.AreEqual("TIMEOUT", receivedError["code"]?.Value<string>());
        }

        [Test]
        public void Tick_RemovesTimedOutRequestFromPending()
        {
            // Arrange
            _manager.CreateRequest("slow.call", null, onResponse: null, timeout: -1f);

            // Act
            _manager.Tick();

            // Assert
            Assert.AreEqual(0, _manager.PendingCount);
        }

        [Test]
        public void Tick_DoesNotExpireRequestWithSufficientTimeout()
        {
            // Arrange – very large timeout will not have elapsed at t=0
            bool callbackInvoked = false;
            _manager.CreateRequest("fast.call", null, (ok, data) => { callbackInvoked = true; },
                timeout: OutboundRequestManager.DefaultTimeout);

            // Act
            _manager.Tick();

            // Assert
            Assert.AreEqual(1, _manager.PendingCount, "Request with sufficient timeout must not expire");
            Assert.IsFalse(callbackInvoked);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 5. CancelAll invokes all pending callbacks with ok=false
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void CancelAll_InvokesAllCallbacksWithOkFalse()
        {
            // Arrange
            var results = new List<(bool ok, string code)>();
            _manager.CreateRequest("a", null, (ok, data) => results.Add((ok, data["code"]?.Value<string>())));
            _manager.CreateRequest("b", null, (ok, data) => results.Add((ok, data["code"]?.Value<string>())));
            _manager.CreateRequest("c", null, (ok, data) => results.Add((ok, data["code"]?.Value<string>())));

            // Act
            _manager.CancelAll();

            // Assert
            Assert.AreEqual(3, results.Count, "All three callbacks must fire");
            foreach (var (ok, code) in results)
            {
                Assert.IsFalse(ok, "ok must be false for every cancelled request");
                Assert.AreEqual("CANCELLED", code, "error code must be CANCELLED");
            }
        }

        [Test]
        public void CancelAll_ClearsPendingCount()
        {
            // Arrange
            _manager.CreateRequest("a", null, onResponse: null);
            _manager.CreateRequest("b", null, onResponse: null);

            // Act
            _manager.CancelAll();

            // Assert
            Assert.AreEqual(0, _manager.PendingCount);
        }

        [Test]
        public void CancelAll_OnEmptyManager_DoesNotThrow()
        {
            // Act + Assert – must not throw
            Assert.DoesNotThrow(() => _manager.CancelAll());
        }

        // ─────────────────────────────────────────────────────────────────────
        // 6. Multiple concurrent requests each receive their own response
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void ConcurrentRequests_EachReceiveCorrectResponse()
        {
            // Arrange
            string receivedByA = null;
            string receivedByB = null;
            string receivedByC = null;

            string jsonA = _manager.CreateRequest("method.a", null, (ok, data) => receivedByA = data["result"]?.Value<string>());
            string jsonB = _manager.CreateRequest("method.b", null, (ok, data) => receivedByB = data["result"]?.Value<string>());
            string jsonC = _manager.CreateRequest("method.c", null, (ok, data) => receivedByC = data["result"]?.Value<string>());

            string idA = JObject.Parse(jsonA)["id"].Value<string>();
            string idB = JObject.Parse(jsonB)["id"].Value<string>();
            string idC = JObject.Parse(jsonC)["id"].Value<string>();

            // Act – inject responses out of order
            _manager.HandleResponse(new JObject { ["id"] = idC, ["ok"] = true, ["data"] = new JObject { ["result"] = "C-data" } });
            _manager.HandleResponse(new JObject { ["id"] = idA, ["ok"] = true, ["data"] = new JObject { ["result"] = "A-data" } });
            _manager.HandleResponse(new JObject { ["id"] = idB, ["ok"] = true, ["data"] = new JObject { ["result"] = "B-data" } });

            // Assert
            Assert.AreEqual("A-data", receivedByA, "Request A should receive its own data");
            Assert.AreEqual("B-data", receivedByB, "Request B should receive its own data");
            Assert.AreEqual("C-data", receivedByC, "Request C should receive its own data");
            Assert.AreEqual(0, _manager.PendingCount, "All requests should be resolved");
        }

        [Test]
        public void ConcurrentRequests_UnrespondedRequestsRemainPending()
        {
            // Arrange
            string jsonA = _manager.CreateRequest("method.a", null, onResponse: null);
            string jsonB = _manager.CreateRequest("method.b", null, onResponse: null);
            string idA = JObject.Parse(jsonA)["id"].Value<string>();

            // Act – only respond to A
            _manager.HandleResponse(new JObject { ["id"] = idA, ["ok"] = true });

            // Assert
            Assert.AreEqual(1, _manager.PendingCount, "Request B must still be pending");
        }
    }
}
