using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Tests.SystemTest
{
    /// <summary>
    /// In-memory mock that replaces WebSocketTransport for system tests.
    /// Simulates a backend by allowing tests to inject responses and events,
    /// and inspect what the Unity client sent.
    /// </summary>
    public class MockTransport
    {
        /// <summary>Messages sent by Unity (captured for assertion).</summary>
        public List<string> SentMessages { get; } = new List<string>();

        /// <summary>Messages queued to be received by Unity (simulating backend responses).</summary>
        private readonly Queue<string> _incomingQueue = new Queue<string>();

        public bool IsConnected { get; set; } = true;

        /// <summary>
        /// Simulate sending a message from Unity to backend.
        /// Captures the message for later assertion.
        /// </summary>
        public void Send(string message)
        {
            SentMessages.Add(message);
        }

        /// <summary>
        /// Inject a message as if the backend sent it to Unity.
        /// </summary>
        public void InjectIncoming(string message)
        {
            _incomingQueue.Enqueue(message);
        }

        /// <summary>
        /// Inject a response frame matching a specific request ID.
        /// </summary>
        public void InjectResponse(string requestId, bool ok, JObject data)
        {
            var frame = new JObject
            {
                ["type"] = "res",
                ["id"] = requestId,
                ["ok"] = ok,
                ["data"] = data ?? new JObject()
            };
            InjectIncoming(frame.ToString(Newtonsoft.Json.Formatting.None));
        }

        /// <summary>
        /// Inject an event frame as if the backend pushed it.
        /// </summary>
        public void InjectEvent(string eventName, JObject data)
        {
            var frame = new JObject
            {
                ["type"] = "event",
                ["event"] = eventName,
                ["data"] = data ?? new JObject()
            };
            InjectIncoming(frame.ToString(Newtonsoft.Json.Formatting.None));
        }

        /// <summary>
        /// Drain all incoming messages (mirrors WebSocketTransport.DrainMessages).
        /// </summary>
        public List<string> DrainMessages()
        {
            var messages = new List<string>();
            while (_incomingQueue.Count > 0)
                messages.Add(_incomingQueue.Dequeue());
            return messages;
        }

        /// <summary>
        /// Find the last sent message and parse it as JSON.
        /// </summary>
        public JObject GetLastSentFrame()
        {
            if (SentMessages.Count == 0) return null;
            return JObject.Parse(SentMessages[SentMessages.Count - 1]);
        }

        /// <summary>
        /// Find sent messages by frame type (e.g., "req", "event").
        /// </summary>
        public List<JObject> GetSentFramesByType(string type)
        {
            var result = new List<JObject>();
            foreach (var msg in SentMessages)
            {
                var frame = JObject.Parse(msg);
                if (frame["type"]?.Value<string>() == type)
                    result.Add(frame);
            }
            return result;
        }

        /// <summary>
        /// Extract the request ID from the last sent request frame.
        /// Useful for injecting a matching response.
        /// </summary>
        public string GetLastSentRequestId()
        {
            var frame = GetLastSentFrame();
            return frame?["id"]?.Value<string>();
        }

        public void Clear()
        {
            SentMessages.Clear();
            _incomingQueue.Clear();
        }
    }
}
