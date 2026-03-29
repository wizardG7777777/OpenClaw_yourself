using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// WebSocket 客户端桥接组件，连接 Python CyberEternity 后端。
/// 
/// 通信协议（JSON 帧）：
///   req   – 请求帧  { type, id, method, params }
///   res   – 响应帧  { type, id, ok, data, error }
///   event – 事件帧  { type, event, data }
/// </summary>
public class BackendBridge : MonoBehaviour
{
    public static BackendBridge Instance { get; private set; }

    [Header("后端连接")]
    [Tooltip("Python 后端 WebSocket 地址")]
    [SerializeField] private string serverUrl = "ws://127.0.0.1:8765/ws";

    [Tooltip("断线后重连间隔（秒）")]
    [SerializeField] private float reconnectInterval = 3f;

    [Tooltip("请求超时（秒）")]
    [SerializeField] private float requestTimeout = 30f;

    public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;

    /// <summary>收到后端事件时触发。参数：(event_name, data_dict)</summary>
    public event Action<string, Dictionary<string, object>> OnBackendEvent;

    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> _pendingRequests = new();
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();
    private int _requestCounter;
    private bool _shouldReconnect = true;

    // ── 生命周期 ────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        _shouldReconnect = true;
        _ = ConnectLoop();
    }

    private void OnDisable()
    {
        _shouldReconnect = false;
        CloseConnection();
    }

    private void Update()
    {
        while (_mainThreadActions.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }

    // ── 连接管理 ────────────────────────────────────

    private async Task ConnectLoop()
    {
        while (_shouldReconnect)
        {
            try
            {
                _cts = new CancellationTokenSource();
                _ws = new ClientWebSocket();

                Debug.Log($"[BackendBridge] 正在连接 {serverUrl} ...");
                await _ws.ConnectAsync(new Uri(serverUrl), _cts.Token);
                Debug.Log("[BackendBridge] 已连接到后端");

                await ReceiveLoop(_cts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BackendBridge] 连接异常: {ex.Message}");
            }

            if (!_shouldReconnect) break;

            Debug.Log($"[BackendBridge] {reconnectInterval}s 后重连...");
            await Task.Delay(TimeSpan.FromSeconds(reconnectInterval));
        }
    }

    private void CloseConnection()
    {
        try
        {
            _cts?.Cancel();
            if (_ws?.State == WebSocketState.Open)
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
        }
        catch { /* ignore */ }
        _ws?.Dispose();
        _ws = null;
    }

    // ── 接收循环 ────────────────────────────────────

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();

        while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            sb.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                Debug.Log("[BackendBridge] 后端关闭了连接");
                break;
            }

            string raw = sb.ToString();
            HandleIncomingFrame(raw);
        }
    }

    private void HandleIncomingFrame(string raw)
    {
        JObject frame;
        try { frame = JObject.Parse(raw); }
        catch { Debug.LogWarning($"[BackendBridge] 无法解析帧: {raw}"); return; }

        string frameType = frame.Value<string>("type") ?? "";

        switch (frameType)
        {
            case "res":
                HandleResponse(frame);
                break;
            case "event":
                HandleEvent(frame);
                break;
            default:
                Debug.LogWarning($"[BackendBridge] 未知帧类型: {frameType}");
                break;
        }
    }

    private void HandleResponse(JObject frame)
    {
        string id = frame.Value<string>("id") ?? "";
        if (_pendingRequests.TryRemove(id, out var tcs))
        {
            tcs.TrySetResult(frame);
        }
    }

    private void HandleEvent(JObject frame)
    {
        string eventName = frame.Value<string>("event") ?? "";
        var data = frame["data"]?.ToObject<Dictionary<string, object>>()
                   ?? new Dictionary<string, object>();

        _mainThreadActions.Enqueue(() =>
        {
            Debug.Log($"[BackendBridge] 收到事件: {eventName}");
            OnBackendEvent?.Invoke(eventName, data);
        });
    }

    // ── 发送请求 ────────────────────────────────────

    /// <summary>
    /// 向后端发送请求并等待响应。
    /// </summary>
    /// <param name="method">方法名，如 "talk_to_character"</param>
    /// <param name="parameters">参数字典</param>
    /// <returns>响应中的 data 字段（JObject），失败时返回 null</returns>
    public async Task<JObject> SendRequest(string method, Dictionary<string, object> parameters = null)
    {
        if (!IsConnected)
        {
            Debug.LogWarning("[BackendBridge] 未连接到后端，无法发送请求");
            return null;
        }

        string reqId = $"req_{Interlocked.Increment(ref _requestCounter):D6}";
        var frame = new JObject
        {
            ["type"] = "req",
            ["id"] = reqId,
            ["method"] = method,
            ["params"] = parameters != null ? JObject.FromObject(parameters) : new JObject()
        };

        var tcs = new TaskCompletionSource<JObject>();
        _pendingRequests[reqId] = tcs;

        string json = frame.ToString(Formatting.None);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            await _ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                _cts.Token
            );
        }
        catch (Exception ex)
        {
            _pendingRequests.TryRemove(reqId, out _);
            Debug.LogWarning($"[BackendBridge] 发送失败: {ex.Message}");
            return null;
        }

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(requestTimeout));
        var completed = await Task.WhenAny(tcs.Task, timeoutTask);

        if (completed == timeoutTask)
        {
            _pendingRequests.TryRemove(reqId, out _);
            Debug.LogWarning($"[BackendBridge] 请求超时: {method} ({reqId})");
            return null;
        }

        JObject response = await tcs.Task;
        bool ok = response.Value<bool>("ok");
        if (!ok)
        {
            var error = response["error"];
            Debug.LogWarning($"[BackendBridge] 请求失败: {method} — {error}");
            return null;
        }

        return response["data"] as JObject;
    }

    /// <summary>向后端发送事件（无需等待响应）。</summary>
    public async Task SendEvent(string eventName, Dictionary<string, object> data = null)
    {
        if (!IsConnected) return;

        var frame = new JObject
        {
            ["type"] = "event",
            ["event"] = eventName,
            ["data"] = data != null ? JObject.FromObject(data) : new JObject()
        };

        string json = frame.ToString(Formatting.None);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            await _ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                _cts.Token
            );
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BackendBridge] 发送事件失败: {ex.Message}");
        }
    }
}
