using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCP.Gateway
{
    /// <summary>
    /// Manages the WebSocket connection to the Python MCP backend server.
    /// Designed to be composed by <see cref="MCPGateway"/> (not a MonoBehaviour itself).
    /// All public methods are safe to call from the Unity main thread.
    /// </summary>
    public class WebSocketTransport
    {
        /// <summary>Default server endpoint.</summary>
        private const string DefaultUrl = "ws://localhost:8765/ws";

        /// <summary>Delay before attempting reconnection after a disconnect.</summary>
        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

        /// <summary>Interval between WebSocket ping frames.</summary>
        private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);

        /// <summary>If no pong is received within this window, log a warning.</summary>
        private static readonly TimeSpan PongTimeout = TimeSpan.FromSeconds(60);

        /// <summary>Maximum size of a single receive buffer (64 KB).</summary>
        private const int ReceiveBufferSize = 65536;

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------

        private readonly string _url;
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;

        private readonly ConcurrentQueue<string> _incomingQueue = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _outgoingQueue = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();

        private volatile bool _running;
        private DateTime _lastPongReceived = DateTime.UtcNow;

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Creates a new transport instance.
        /// </summary>
        /// <param name="url">WebSocket server URL. Pass <c>null</c> for the default.</param>
        public WebSocketTransport(string url = null)
        {
            _url = url ?? DefaultUrl;
        }

        /// <summary>
        /// Whether the underlying WebSocket is currently in the Open state.
        /// </summary>
        public bool IsConnected =>
            _ws != null && _ws.State == WebSocketState.Open;

        /// <summary>
        /// Begin connecting to the server. Starts background receive, send,
        /// heartbeat, and auto-reconnect loops.
        /// Call once from <c>MCPGateway.OnEnable</c> or similar.
        /// </summary>
        public void Start()
        {
            if (_running) return;

            _running = true;
            _cts = new CancellationTokenSource();

            // Fire-and-forget the main connection loop.
            _ = ConnectionLoopAsync(_cts.Token);
        }

        /// <summary>
        /// Gracefully disconnect and stop all background tasks.
        /// Call from <c>MCPGateway.OnDisable</c> or <c>OnDestroy</c>.
        /// </summary>
        public void Stop()
        {
            _running = false;

            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }
            catch (ObjectDisposedException) { }

            CloseSocket();
            _cts = null;
        }

        /// <summary>
        /// Enqueue a JSON message to be sent to the server.
        /// If not currently connected the message is discarded with a warning.
        /// </summary>
        /// <param name="message">Raw JSON string to send.</param>
        public void Send(string message)
        {
            if (!IsConnected)
            {
                EnqueueLog("warn", "WebSocketTransport.Send called while not connected — message discarded.");
                return;
            }

            _outgoingQueue.Enqueue(message);
        }

        /// <summary>
        /// Drain all messages received since the last call.
        /// Intended to be called by <c>MCPGateway.Update()</c> on the main thread.
        /// </summary>
        /// <returns>A list of raw JSON strings (may be empty, never null).</returns>
        public List<string> DrainMessages()
        {
            // Flush any log entries that were enqueued from background threads.
            FlushLogs();

            var messages = new List<string>();
            while (_incomingQueue.TryDequeue(out string msg))
            {
                messages.Add(msg);
            }
            return messages;
        }

        // ------------------------------------------------------------------
        // Background loops
        // ------------------------------------------------------------------

        /// <summary>
        /// Top-level loop: connect, run receive/send/heartbeat, reconnect on failure.
        /// </summary>
        private async Task ConnectionLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _running)
            {
                try
                {
                    await ConnectAsync(ct);

                    if (!IsConnected)
                    {
                        await Task.Delay(ReconnectDelay, ct);
                        continue;
                    }

                    EnqueueLog("info", $"WebSocketTransport connected to {_url}");

                    // Run receive, send, and heartbeat concurrently.
                    // When any of them exits (disconnect, error) we reconnect.
                    var receiveTask = ReceiveLoopAsync(ct);
                    var sendTask = SendLoopAsync(ct);
                    var heartbeatTask = HeartbeatLoopAsync(ct);

                    await Task.WhenAny(receiveTask, sendTask, heartbeatTask);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    EnqueueLog("error", $"WebSocketTransport connection error: {ex.Message}");
                }

                CloseSocket();

                if (!ct.IsCancellationRequested && _running)
                {
                    EnqueueLog("info", $"WebSocketTransport will reconnect in {ReconnectDelay.TotalSeconds}s...");
                    try
                    {
                        await Task.Delay(ReconnectDelay, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Attempt to open the WebSocket connection.
        /// </summary>
        private async Task ConnectAsync(CancellationToken ct)
        {
            CloseSocket();

            _ws = new ClientWebSocket();
            _ws.Options.KeepAliveInterval = PingInterval;

            try
            {
                await _ws.ConnectAsync(new Uri(_url), ct);
                _lastPongReceived = DateTime.UtcNow;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                EnqueueLog("warn", $"WebSocketTransport failed to connect: {ex.Message}");
                CloseSocket();
            }
        }

        /// <summary>
        /// Continuously read messages from the WebSocket and enqueue them.
        /// </summary>
        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[ReceiveBufferSize];
            var messageBuffer = new StringBuilder();

            while (!ct.IsCancellationRequested && IsConnected)
            {
                try
                {
                    var segment = new ArraySegment<byte>(buffer);
                    WebSocketReceiveResult result = await _ws.ReceiveAsync(segment, ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        EnqueueLog("info", "WebSocketTransport received close frame from server.");
                        return;
                    }

                    // Pong frames are handled internally by ClientWebSocket;
                    // update our tracking timestamp on any successful receive.
                    _lastPongReceived = DateTime.UtcNow;

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                        if (result.EndOfMessage)
                        {
                            string message = messageBuffer.ToString();
                            messageBuffer.Clear();
                            _incomingQueue.Enqueue(message);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException ex)
                {
                    EnqueueLog("error", $"WebSocketTransport receive error: {ex.Message}");
                    return;
                }
                catch (Exception ex)
                {
                    EnqueueLog("error", $"WebSocketTransport unexpected receive error: {ex.Message}");
                    return;
                }
            }
        }

        /// <summary>
        /// Continuously dequeue outgoing messages and send them over the socket.
        /// </summary>
        private async Task SendLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                if (_outgoingQueue.TryDequeue(out string message))
                {
                    try
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(message);
                        var segment = new ArraySegment<byte>(bytes);
                        await _ws.SendAsync(segment, WebSocketMessageType.Text, true, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (WebSocketException ex)
                    {
                        EnqueueLog("error", $"WebSocketTransport send error: {ex.Message}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        EnqueueLog("error", $"WebSocketTransport unexpected send error: {ex.Message}");
                        return;
                    }
                }
                else
                {
                    // No messages to send — yield briefly to avoid busy-waiting.
                    try
                    {
                        await Task.Delay(10, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Periodically checks that we are still receiving pong responses.
        /// <see cref="ClientWebSocket"/> sends pings automatically via
        /// <see cref="ClientWebSocket.Options.KeepAliveInterval"/>, and handles
        /// pong frames internally. We track liveness via successful receives.
        /// </summary>
        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                try
                {
                    await Task.Delay(PingInterval, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                TimeSpan sincePong = DateTime.UtcNow - _lastPongReceived;
                if (sincePong > PongTimeout)
                {
                    EnqueueLog("warn",
                        $"WebSocketTransport no pong received for {sincePong.TotalSeconds:F0}s — connection may be dead.");
                }
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Close and dispose the current <see cref="ClientWebSocket"/> if it exists.
        /// </summary>
        private void CloseSocket()
        {
            if (_ws == null) return;

            try
            {
                if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
                {
                    // Best-effort graceful close (non-cancellable, short timeout).
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client shutting down", closeCts.Token)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();
                }
            }
            catch
            {
                // Swallow — we're tearing down.
            }
            finally
            {
                try { _ws.Dispose(); }
                catch { /* ignore */ }
                _ws = null;
            }
        }

        /// <summary>
        /// Enqueue a log message to be flushed on the main thread.
        /// Safe to call from any thread.
        /// </summary>
        /// <param name="level">"info", "warn", or "error".</param>
        /// <param name="message">Log text.</param>
        private void EnqueueLog(string level, string message)
        {
            _logQueue.Enqueue($"{level}|{message}");
        }

        /// <summary>
        /// Flush queued log messages using Unity's logging API.
        /// Must be called from the main thread (e.g., inside <see cref="DrainMessages"/>).
        /// </summary>
        private void FlushLogs()
        {
            while (_logQueue.TryDequeue(out string entry))
            {
                int sep = entry.IndexOf('|');
                if (sep < 0)
                {
                    Debug.Log(entry);
                    continue;
                }

                string level = entry.Substring(0, sep);
                string msg = entry.Substring(sep + 1);

                switch (level)
                {
                    case "warn":
                        Debug.LogWarning(msg);
                        break;
                    case "error":
                        Debug.LogError(msg);
                        break;
                    default:
                        Debug.Log(msg);
                        break;
                }
            }
        }
    }
}
