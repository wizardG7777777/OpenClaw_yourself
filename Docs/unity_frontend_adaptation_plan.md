# Unity 前端适配计划 — 对接 Python 后端

> 基于 `backend_development_plan.pdf` 的需求，结合 `MCP_Agent_Design.md` 的架构设计，梳理 Unity 游戏引擎侧需要做的所有改动。

---

## 一、当前架构概况

### 现有 MCP 管线

```
[Raw JSON] → MCPGateway.ProcessRequest()
   ↓ (解析 + 结构校验 + 白名单检查 + QPS 限流)
MCPRequest → MCPRouter.Route()
   ↓ (工具查找 + 参数标准化 + target_id 语义解析)
Tool 分发
   ├─ Query  → 静态 Handler → 立即返回数据
   └─ Action → IActionHandler → 返回 action_id，异步执行
```

这套 MCP 管线是运行时的 Agent → 游戏引擎通用接口。当前唯一的传输层是 MCP-for-Unity 插件（`com.coplaydev.unity-mcp`），它把外部 Agent（Claude）的 JSON 请求传递给 `MCPGateway.ProcessRequest()`。

### 现有脚本清单

| 分类 | 文件 | 状态 |
|------|------|------|
| 玩家 | `PlayerMovement.cs` | 完善（双模式：键盘 + NavMeshAgent） |
| 玩家 | `PlayerInteraction.cs` | 完善（鼠标点击 + E 键交互） |
| 相机 | `CameraFollow.cs` | 完善（固定俯视角跟随） |
| 交互接口 | `IInteractable.cs` | 完善（Interact / GetPromptText / GetState） |
| 交互实现 | `ToggleDoor.cs`, `ToggleCurtain.cs`, `ToggleLight.cs` | 完善 |
| MCP 核心 | `MCPGateway.cs`, `MCPRouter.cs`, `MCPRequest/Response.cs` | 完善 |
| MCP 实体 | `EntityRegistry.cs`, `EntityIdentity.cs`, `SemanticResolver.cs` | 完善 |
| MCP 动作 | `MoveToHandler.cs`, `InteractWithHandler.cs` | 完善 |
| MCP 动作 | **`TalkToNpcHandler.cs`** | **占位实现** — 仅 log 后立即 Complete |
| MCP 动作 | `EquipItemHandler.cs`, `UseToolOnHandler.cs` | MVP 实现（硬编码背包） |
| MCP 查询 | `GetPlayerState/NearbyEntities/Inventory/WorldSummary` | 完善（Inventory 硬编码） |
| NPC 系统 | — | **不存在** |

---

## 二、核心架构决策

### 2.1 在 MCPGateway 上直接扩展 WebSocket 双向通信

**决策：不新建独立的通信模块，直接在 MCPGateway 上增加 WebSocket 传输层和出站请求能力。**

理由：
- `backend_development_plan.pdf` 的架构图明确画的就是 `WSServer <--> MCPGateway`，Python 后端直接与 MCPGateway 通信
- `MCP_Agent_Design.md` 第 2.1 节定义网关层定位为"MCP 的统一入口（HTTP/WebSocket/STDIO 均可）"，WebSocket 本就在设计范围内
- `MCP_Agent_Design.md` 第 2.2 节决策 4 已预留升级路径："网关层从纯请求-响应升级为支持双向消息的 WebSocket 长连接"
- 单一模块统一管理所有通信，避免多模块共享 WebSocket 连接带来的协调问题

### 2.2 改造后的 MCPGateway 架构

```
MCPGateway（改造后，双向通信网关）
│
├── 现有能力（保持不变）
│   ├── ProcessRequest(rawJson) — 入站请求处理
│   ├── 结构校验 / 白名单 / QPS 限流
│   └── 转发给 MCPRouter
│
├── 新增能力 A：WebSocket 传输层
│   ├── 作为 WebSocket 客户端连接 Python 后端 ws://localhost:8765/ws
│   ├── 自动重连（断线后 3 秒重试）
│   ├── 心跳保活（定时 ping）
│   ├── 收到后端 req 帧 → method/params 映射为 tool/args → ProcessRequest()
│   └── 将 MCPResponse 包装为 res 帧发回后端
│
├── 新增能力 B：出站请求（Unity → 后端）
│   ├── SendToBackend(method, params, callback)
│   ├── 请求 ID 生成 + 回调配对（Dictionary<string, Action<JObject>>）
│   ├── 收到 res 帧时按 id 匹配回调并触发
│   └── 供 TalkToNpcHandler、UI 面板等内部模块调用后端 LLM/数据库
│
├── 新增能力 C：事件推送（双向）
│   ├── 出站：动作完成时主动推送 action_completed / action_failed 给后端
│   ├── 入站：接收后端 event 帧（character_move, character_state_changed 等）
│   └── 事件分发：通过 C# event/Action 机制，允许其他脚本注册监听
│
└── 现有传输层（不变）
    └── MCP-for-Unity 插件继续通过 ProcessRequest() 接入
```

**改造后的架构图（与 backend_development_plan.pdf 对齐）：**

```
┌─────────────────────────────────────────────────────────┐
│                    MCPGateway（改造后）                    │
│                                                         │
│  ┌──────────────┐   ┌──────────────┐   ┌─────────────┐ │
│  │ WebSocket 传输 │   │ 入站请求处理  │   │ 出站请求管理 │ │
│  │ (连接/重连/心跳)│──→│ProcessRequest│   │SendToBackend│ │
│  │              │   │ (现有，不变)  │   │ (新增)      │ │
│  │              │←──│  MCPResponse  │   │             │ │
│  └──────┬───────┘   └──────────────┘   └──────┬──────┘ │
│         │                                      │        │
│  ┌──────┴──────────────────────────────────────┴──────┐ │
│  │              事件分发 (C# event/Action)              │ │
│  │   入站 event → 分发到 NpcController 等               │ │
│  │   出站 event → 推送 action_completed 等              │ │
│  └────────────────────────────────────────────────────┘ │
└─────────────────────────┬───────────────────────────────┘
                          │ WebSocket (JSON 双向通信)
                          │ req/res/event 三帧协议
                          ▼
              ┌───────────────────────┐
              │    Python 后端         │
              │  ws://localhost:8765   │
              │  角色管理 / 记忆 / LLM  │
              │  移动决策 / SQLite     │
              └───────────────────────┘
```

### 2.3 帧协议（与后端对齐，参考 Claw3D 三帧模型）

**请求帧 req — 双向使用：**
```json
{"type": "req", "id": "req_001", "method": "talk_to_character", "params": {"character_id": "char_01", "message": "..."}}
```

**响应帧 res — 对 req 的回复：**
```json
{"type": "res", "id": "req_001", "ok": true, "data": {"reply": "当然记得..."}}
```

**事件帧 event — 主动推送，无需回复：**
```json
{"type": "event", "event": "character_move", "data": {"character_id": "char_01", "target_id": "sofa_01"}}
```

**入站 req 帧的字段映射（后端 → MCPGateway）：**

后端使用 `method` + `params`，MCPGateway 内部使用 `tool` + `args`。网关层在 WebSocket 接收端做转换：

```csharp
void OnWebSocketMessage(string rawFrame)
{
    var frame = JObject.Parse(rawFrame);
    string type = frame["type"]?.ToString();

    switch (type)
    {
        case "req":
            HandleInboundRequest(frame);
            break;
        case "res":
            HandleInboundResponse(frame);
            break;
        case "event":
            HandleInboundEvent(frame);
            break;
    }
}

void HandleInboundRequest(JObject frame)
{
    string id = frame["id"].ToString();
    string method = frame["method"].ToString();
    JObject params = frame["params"] as JObject ?? new JObject();

    // 字段映射：method → tool, params → args
    string mcpJson = new JObject
    {
        ["tool"] = method,
        ["args"] = params
    }.ToString();

    // 复用现有入站处理管线
    MCPResponse response = ProcessRequest(mcpJson);

    // 封装为 res 帧发回后端
    SendFrame(new JObject
    {
        ["type"] = "res",
        ["id"] = id,
        ["ok"] = response.Ok,
        ["data"] = response.Ok
            ? JObject.FromObject(response.Data ?? new {})
            : JObject.FromObject(response.Error)
    });
}
```

### 2.4 通信方向分类

| 操作 | 方向 | 机制 | 说明 |
|------|------|------|------|
| NPC 移动到某处 | 后端 → Unity | 后端 req `move_to` → MCPGateway 入站处理 | 复用现有 MCP 管线 |
| NPC 与物件交互 | 后端 → Unity | 后端 req `interact_with` → MCPGateway 入站处理 | 复用现有 MCP 管线 |
| 查询附近实体 | 后端 → Unity | 后端 req `get_nearby_entities` → MCPGateway 入站处理 | 复用现有 MCP 管线 |
| 查询世界状态 | 后端 → Unity | 后端 req `get_player_state` → MCPGateway 入站处理 | 复用现有 MCP 管线 |
| 玩家与 NPC 对话 | Unity → 后端 | MCPGateway.SendToBackend("talk_to_character", ...) | 需要后端 LLM 生成回复 |
| 创建/编辑角色 | Unity → 后端 | MCPGateway.SendToBackend("create_character", ...) | 后端数据库操作 |
| 输入/查询记忆 | Unity → 后端 | MCPGateway.SendToBackend("add_memory", ...) | 后端数据库操作 |
| NPC 自主行为通知 | 后端 → Unity | 后端 event `character_move` → 网关事件分发 | 被动通知 |
| 动作完成通知 | Unity → 后端 | 网关 event `action_completed` → 推送后端 | 主动推送 |

### 2.5 与 MCP-for-Unity 插件的共存

```
MCP-for-Unity 插件 ──调用──→ MCPGateway.ProcessRequest()  ← 不变
Python 后端 ←──WebSocket──→ MCPGateway (WebSocket 传输层)   ← 新增
```

两条传输层共享同一套业务管线（Router → Handlers），互不干扰。MCP-for-Unity 插件继续用于开发阶段的编辑器自动化，WebSocket 通道用于运行时与 Python 后端通信。

---

## 三、需要新增的模块

### 3.1 `NpcController.cs` — NPC 角色控制器

**职责：** 挂载在场景中的 NPC GameObject 上，管理单个 NPC 的移动、动画和状态。

```
NpcController (MonoBehaviour, IInteractable)
│
├── 身份信息
│   ├── characterId  — 与后端数据库 characters.id 对应
│   ├── displayName  — 显示名称
│   └── EntityIdentity 组件 — 复用现有实体注册系统
│       ├── entityType = "npc"
│       ├── entityId = characterId
│       └── 自动被 EntityRegistry 注册 → 可被 SemanticResolver 解析
│
├── 移动（NavMeshAgent）
│   ├── 复用现有 NavMesh 烘焙数据
│   ├── MoveTo(Vector3 target) — 供网关事件分发调用
│   ├── 到达目标后通过 MCPGateway.SendToBackend 通知后端
│   └── 后续可升级为通过 MoveToHandler 走 MCP 管线（见第四节 4.2）
│
├── IInteractable 实现
│   ├── Interact() → 打开对话 UI，通过 MCPGateway.SendToBackend 请求后端对话
│   ├── GetPromptText() → "与 {displayName} 对话"
│   └── GetState() → { "character_id", "status", "is_talking" }
│
├── 状态机
│   ├── Idle → Walking → Talking → Idle
│   ├── 状态变化时更新动画
│   └── GetState() 暴露当前状态给 get_nearby_entities 查询
│
└── 动画
    ├── Animator 或 legacy Animation
    └── idle / walk / talk 动画状态
```

**与现有系统的自动集成：**
- 挂载 `EntityIdentity` → 自动被 `EntityRegistry` 注册 → `SemanticResolver` 可解析
- 实现 `IInteractable` → 玩家 E 键/鼠标点击可触发 → `InteractWithHandler` 可操作
- `entityType = "npc"` → `get_nearby_entities(entity_types=["npc"])` 可查询到
- `GetState()` 返回 NPC 状态 → Agent 可通过查询了解 NPC 当前状态

**推荐位置：** `Assets/Scripts/NPC/NpcController.cs`

---

### 3.2 `NpcDialogueUI.cs` — 对话 UI

**职责：** 显示 NPC 对话气泡或对话面板。

```
NpcDialogueUI
│
├── 对话气泡（世界空间 Canvas）
│   ├── 跟随 NPC 头部位置
│   ├── 显示 LLM 返回的文本
│   ├── 支持逐字显示（打字机效果）
│   └── 自动隐藏（对话结束后 N 秒）
│
├── 对话输入面板（屏幕空间 Canvas）
│   ├── 玩家靠近 NPC 按 E 键时弹出（由 NpcController.Interact() 触发）
│   ├── 文本输入框 — 玩家输入对话内容
│   ├── 发送按钮 / Enter 键 → MCPGateway.SendToBackend("talk_to_character", ...)
│   └── 对话历史滚动列表
│
└── 加载状态
    └── LLM 生成回复期间显示 "..." 或加载动画
```

**推荐位置：** `Assets/Scripts/UI/NpcDialogueUI.cs`

---

### 3.3 `CharacterCreationUI.cs` — 角色创建界面

**职责：** 让玩家在游戏中创建/编辑 NPC 角色。

```
CharacterCreationUI
│
├── 输入字段
│   ├── 名称 (name)
│   ├── 关系 (relationship): 下拉框 — 亲人/朋友/宠物/其他
│   ├── 性格描述 (personality): 多行文本
│   ├── 外观描述 (appearance): 多行文本
│   ├── 背景故事 (backstory): 多行文本
│   └── 说话风格 (voice_style): 单行文本
│
├── 操作
│   ├── 创建 → MCPGateway.SendToBackend("create_character", {...})
│   ├── 保存编辑 → MCPGateway.SendToBackend("update_character", {...})
│   └── 创建成功后 → 在场景中实例化 NPC Prefab，挂载 NpcController
│
└── 角色列表
    └── 显示已创建角色，点击可编辑或删除
```

**推荐位置：** `Assets/Scripts/UI/CharacterCreationUI.cs`

---

### 3.4 `MemoryInputUI.cs` — 记忆输入界面

**职责：** 让玩家为指定角色输入核心记忆。

```
MemoryInputUI
│
├── 选择角色（下拉框或从场景中选中 NPC）
├── 输入记忆内容（多行文本）
├── 设置重要性（滑块 1-10）
├── 提交 → MCPGateway.SendToBackend("add_memory", { character_id, content, importance })
└── 查看已有记忆列表
```

**推荐位置：** `Assets/Scripts/UI/MemoryInputUI.cs`

---

## 四、需要改造的现有模块

### 4.1 `MCPGateway.cs` — 升级为双向通信网关

**当前状态：** 纯入站请求处理（`ProcessRequest(rawJson)` → `MCPResponse`）。

**改造内容：**

```
MCPGateway.cs 改造清单
│
├── 新增：WebSocket 连接管理
│   ├── 字段：WebSocket 客户端实例、连接状态、重连计时器
│   ├── Connect() — 连接 ws://localhost:8765/ws
│   ├── 自动重连 — 断线后 3 秒重试
│   ├── 心跳保活 — 定时发送 ping
│   └── OnDestroy() — 断开连接
│
├── 新增：WebSocket 消息接收与帧分发
│   ├── OnWebSocketMessage(string rawFrame)
│   ├── 解析 type 字段 → 分三路处理：
│   │   ├── "req"   → HandleInboundRequest()  — 字段映射后调用 ProcessRequest()
│   │   ├── "res"   → HandleInboundResponse() — 按 id 匹配出站请求回调
│   │   └── "event" → HandleInboundEvent()    — 分发给已注册的事件监听器
│   └── 帧解析错误时记录日志，不中断连接
│
├── 新增：出站请求能力
│   ├── SendToBackend(string method, object params, Action<JObject> onResponse)
│   ├── 生成唯一请求 id
│   ├── 注册回调到 Dictionary<string, Action<JObject>> _pendingRequests
│   ├── 组装 req 帧并通过 WebSocket 发送
│   └── 超时清理（可配置，默认 30 秒）
│
├── 新增：出站事件推送
│   ├── SendEvent(string eventName, object data) — 组装 event 帧并发送
│   ├── 在 Update() 中检测动作状态变化：
│   │   ├── MCPRouter.GetCurrentAction() 进入终态时
│   │   └── 推送 action_completed / action_failed event 帧
│   └── 已推送的 action_id 记录在 HashSet 中避免重复推送
│
├── 新增：入站事件分发机制
│   ├── C# event：OnBackendEvent(string eventName, JObject data)
│   ├── 或 Dictionary<string, Action<JObject>> _eventHandlers
│   ├── RegisterEventHandler(string eventName, Action<JObject> handler)
│   └── NpcController 等脚本在 Awake/OnEnable 时注册监听
│
├── 新增：连接状态查询
│   ├── bool IsConnected { get; }
│   ├── 供 UI 显示连接状态（已连接/断开/重连中）
│   └── 供 Handler 判断是否可以发出站请求
│
└── 保持不变
    ├── ProcessRequest(rawJson) — 入站请求处理逻辑完全不变
    ├── 结构校验 / 白名单 / QPS 限流 — 不变
    ├── MCPRouter 引用 — 不变
    └── MCP-for-Unity 插件继续调用 ProcessRequest() — 不变
```

**关键设计约束：**
- `ProcessRequest()` 的签名和行为完全不变，保证 MCP-for-Unity 插件零影响
- WebSocket 连接失败不影响本地 MCP 功能（MCP-for-Unity 插件照常工作）
- 出站请求在 WebSocket 未连接时，回调立即收到连接错误，不阻塞

---

### 4.2 `TalkToNpcHandler.cs` — 从占位改为后端对接

**当前实现（占位）：**
```csharp
// 仅打 log，立即返回 Completed
action.Status = ActionStatus.Completed;
action.Result = new { message = $"Initiated dialogue with {npcName}." };
```

**改造方案：**
```
改造后的 TalkToNpcHandler
│
├── StartAction()
│   ├── 检查 target 是否有 NpcController 组件
│   ├── 获取 characterId
│   ├── 从 args 获取 topic / dialogue_option（可选）
│   ├── 获取 MCPGateway 实例
│   ├── 检查 MCPGateway.IsConnected（未连接时 → Failed）
│   ├── MCPGateway.SendToBackend("talk_to_character", { character_id, topic }, OnReply)
│   └── action.Status = Running
│
├── OnReply(JObject response)  — 出站请求回调
│   ├── 解析 reply, emotion 字段
│   ├── action.Status = Completed
│   ├── action.Result = { reply, emotion }
│   └── 触发 NpcDialogueUI 显示对话气泡
│
├── UpdateAction()
│   └── 空（等待回调触发，超时由 MCPRouter 统一处理）
│
└── Cancel()
    └── 标记回调失效，避免取消后仍然触发 UI
```

**两条触发路径并存：**
- **玩家直接交互**：E 键 → `NpcController.Interact()` → 打开输入面板 → 玩家输入文本 → `MCPGateway.SendToBackend`
- **MCP Agent 触发**：Agent req `talk_to_npc` → MCPGateway → TalkToNpcHandler → `MCPGateway.SendToBackend`

两条路径最终都通过 MCPGateway 的出站请求能力调用后端 LLM。

---

### 4.3 `MoveToHandler.cs` — 支持操控非 Player 角色（可选改造）

**当前实现：** 硬编码只操控 `Player` tag 对象。

**方案 A（推荐，后续实施）：改造 MoveToHandler 支持 `actor_id` 参数**
- 如果 args 中包含 `actor_id`，则操控指定角色（NPC）的 NavMeshAgent
- 如果没有 `actor_id`，保持默认行为（操控 Player）
- 需要在 ToolRegistry 中为 `move_to` 增加 `actor_id` 可选参数

**方案 B（简单，先用这个）：NPC 移动通过 event 直推**
- 后端发送 event 帧 `character_move`
- MCPGateway 事件分发 → NpcController.MoveTo()
- NPC 到达后 → MCPGateway.SendToBackend("movement_completed", ...)
- 缺点：不走 MCP 管线，没有超时/打断/状态追踪

**建议先用方案 B 快速验证，后续再升级到方案 A。**

---

### 4.4 无需改动的模块

- `MCPRouter.cs` — 路由/分发/状态管理逻辑不变
- `PlayerMovement.cs` — 玩家移动与后端无关
- `PlayerInteraction.cs` — NPC 实现 IInteractable 即可自动被检测
- `CameraFollow.cs` — 相机跟随逻辑不变
- `ToggleDoor/Curtain/Light.cs` — 物件交互不变
- `MoveToHandler.cs` — 暂不改造（方案 B 阶段）
- `EntityRegistry.cs` / `SemanticResolver.cs` — NPC 自动注册，无需修改

---

## 五、数据流详解

### 5.1 后端控制 NPC 移动（方案 B：event 直推）

```
后端移动决策模块（每 10-30 秒）
  → 决定 NPC char_01 移动到 sofa_01
  → 发送 event 帧:
    { "type": "event", "event": "character_move",
      "data": { "character_id": "char_01", "target_id": "sofa_01" } }

MCPGateway WebSocket 收到 event 帧
  → 事件分发 → NpcController 收到 character_move
  → 根据 target_id 通过 EntityRegistry.GetById("sofa_01") 获取目标位置
  → NpcController.MoveTo(targetPosition)
  → NPC 的 NavMeshAgent 执行寻路
  → 到达后 NpcController 调用：
    MCPGateway.SendToBackend("movement_completed", { character_id: "char_01" })
```

### 5.2 玩家与 NPC 对话

```
玩家按 E 键靠近 NPC
  → PlayerInteraction 检测到 NpcController (IInteractable)
  → NpcController.Interact()
  → 打开 NpcDialogueUI 输入面板
  → 玩家输入文本，点击发送
  → MCPGateway.SendToBackend("talk_to_character", {
      character_id: "char_01",
      message: "你还记得那年夏天吗？"
    })
  → 后端对话引擎：检索相关记忆 + LLM 生成回复
  → 后端返回 res 帧: { "ok": true, "data": { "reply": "当然记得...", "emotion": "nostalgic" } }
  → MCPGateway 按 id 匹配回调 → NpcDialogueUI 显示对话气泡
  → 后端自动保存本次对话为 conversation_memory
```

### 5.3 创建角色

```
玩家打开角色创建界面
  → 填写角色信息
  → MCPGateway.SendToBackend("create_character", {
      name: "妈妈", relationship: "亲人",
      personality: "温柔善良...", ...
    })
  → 后端写入 SQLite，返回 { ok: true, data: { id: "char_01" } }
  → 在场景中实例化 NPC Prefab
  → 挂载 NpcController（characterId = "char_01"）
  → 挂载 EntityIdentity（entityId = "char_01", entityType = "npc"）
  → EntityRegistry 自动注册 → 可被 MCP 管线查询和操控
```

### 5.4 输入记忆

```
玩家打开记忆输入界面
  → 选择角色，输入记忆内容
  → MCPGateway.SendToBackend("add_memory", {
      character_id: "char_01",
      content: "2015年夏天我们一起去了青岛的海边...",
      importance: 8
    })
  → 后端写入 SQLite，提取关键词，返回确认
```

### 5.5 后端通过 MCP 管线查询游戏状态

```
后端移动决策模块需要了解场景中有什么
  → 发送 req 帧:
    { "type": "req", "id": "req_042", "method": "get_nearby_entities",
      "params": { "radius": 50, "entity_types": ["furniture"] } }

MCPGateway WebSocket 收到 req 帧
  → 字段映射: method → tool, params → args
  → ProcessRequest('{"tool":"get_nearby_entities","args":{"radius":50,...}}')
  → MCPRouter → GetNearbyEntitiesHandler → 返回实体列表
  → 封装为 res 帧发回后端:
    { "type": "res", "id": "req_042", "ok": true,
      "data": { "entities": [...] } }
```

---

## 六、新增文件目录结构

```
Assets/Scripts/
├── NPC/
│   └── NpcController.cs              # NPC 角色控制器（移动、状态、IInteractable）
│
├── UI/
│   ├── NpcDialogueUI.cs              # 对话气泡 + 对话输入面板
│   ├── CharacterCreationUI.cs        # 角色创建/编辑界面
│   └── MemoryInputUI.cs              # 记忆输入界面
│
└── MCP/
    └── Gateway/
        └── MCPGateway.cs             # 改造：新增 WebSocket + 出站请求 + 事件分发
```

---

## 七、依赖与技术选型

| 需求 | 方案 | 说明 |
|------|------|------|
| WebSocket 客户端 | `NativeWebSocket` 或 `websocket-sharp` | Unity 原生不含 WebSocket 客户端，需引入第三方库 |
| JSON 序列化 | `Newtonsoft.Json`（已有） | 项目已经在用，无需额外引入 |
| UI 框架 | Unity UI (uGUI) | 标准 Canvas + Text/InputField/Button |
| NavMesh | AI Navigation 2.0（已有） | NPC 直接复用现有 NavMesh 烘焙数据 |

---

## 八、实施顺序建议

```
第 1 步: 改造 MCPGateway.cs             ← WebSocket 连接 + 帧分发 + 出站请求 + 事件推送
         验证：后端发 get_player_state req → 网关转发 → 返回玩家位置
         验证：Unity 调用 SendToBackend → 后端收到 req → 返回 res → 回调触发
         验证：后端发 event 帧 → 网关分发 → 注册的监听器收到

第 2 步: NpcController.cs               ← NPC 在场景中存在、被查询、被交互
         注册监听 character_move 事件
         验证：get_nearby_entities 能发现 NPC；E 键点击 NPC 能触发 Interact()

第 3 步: NpcDialogueUI.cs               ← 对话 UI 能显示
         + 改造 TalkToNpcHandler.cs      ← 对话走后端 LLM
         验证：玩家 E 键 → 输入文本 → 后端返回 → 气泡显示

第 4 步: 后端 event 驱动 NPC 移动        ← character_move 事件处理
         验证：后端发 character_move → NPC 寻路移动 → 到达通知后端

第 5 步: CharacterCreationUI.cs          ← 角色创建流程
         验证：创建角色 → 场景中出现 NPC → 可查询/可交互

第 6 步: MemoryInputUI.cs               ← 记忆输入流程
         验证：输入记忆 → 对话时 NPC 引用记忆内容
```

每一步完成后都可以与后端联调验证，不需要全部做完才能测试。

> **注：Python 后端将在未来若干版本后正式实现。本文档描述的是 Unity 侧需要提前做好的适配准备，使得后端就绪后可以立即对接。在后端未就绪期间，MCPGateway 的 WebSocket 连接失败不影响任何现有功能。**
