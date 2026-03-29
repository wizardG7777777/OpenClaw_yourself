# 环境配置指南

本文档帮助新组员从零配置开发和测试环境。

## 1. 必装软件

| 软件 | 最低版本 | 用途 | 安装方式 |
|------|---------|------|---------|
| Unity Editor | 6000.x (Unity 6) | 游戏引擎 | [Unity Hub](https://unity.com/download) |
| Node.js | v18+ | 运行 E2E 测试 | [nodejs.org](https://nodejs.org/) 或 `brew install node` |
| Git | 2.x | 版本管理 | `brew install git` 或 [git-scm.com](https://git-scm.com/) |

验证安装：

```bash
node --version    # 需要 v18.0.0 以上
git --version
```

## 2. 克隆项目

```bash
git clone https://github.com/wizardG7777777/OpenClaw_yourself.git
cd OpenClaw_yourself
```

## 3. Unity 项目配置

1. 打开 Unity Hub → Add → 选择克隆下来的项目文件夹
2. 使用 Unity 6 (6000.x) 版本打开
3. 首次打开会自动导入资源，等待完成
4. 打开主场景：`Assets/mvp_game.unity`

### 确认 MCP-For-Unity 服务运行

1. Unity 菜单栏 → `Window` → `MCP For Unity`
2. 确认 HTTP 服务已启动，端口为 **8080**
3. 状态显示为 Connected

### 确认 NavMesh 已烘焙

1. 菜单栏 → `Tools` → `Bake NavMesh Surface`
2. 如果提示 "No NavMeshSurface found"，检查场景中是否有 NavMeshSurface 组件

## 4. 安装测试依赖

```bash
cd e2e-tests
npm install
```

## 5. 运行系统测试

**前提：Unity 必须处于 Play Mode。**

1. 在 Unity 中按下 Play 按钮（或 Ctrl+P / Cmd+P）
2. 在终端中运行：

```bash
cd e2e-tests
npm run test:system
```

测试会自动检查所有前置条件（MCP 连接、Play Mode、实体配置等），如果有条件不满足会给出明确提示。

### 测试命令一览

| 命令 | 说明 |
|------|------|
| `npm run test:system` | 运行 Play Mode 系统测试（门交互全流程） |
| `npm test` | 运行所有测试（含 Edit Mode 兼容的单元测试） |
| `npm run test:watch` | 监听模式，文件变更自动重跑 |

## 6. 常见问题

### 测试报错 "Cannot connect to Unity MCP server"

- 确认 Unity 已打开且 MCP For Unity 窗口显示 Connected
- 确认端口 8080 未被占用：`lsof -i :8080`
- 默认地址为 `http://127.0.0.1:8080/mcp`，可通过环境变量 `MCP_URL` 覆盖

### 测试报错 "Player not found"

- 确认 Unity 处于 Play Mode（不是 Edit Mode）
- 确认场景中 MVPlayer 的 Tag 设置为 `Player`

### 测试报错 "door_main not found"

- 确认场景中 Door_Left 上有 `EntityIdentity` 组件，`entity_id` 设为 `door_main`
- 确认场景中 MCPSystem 上有 `EntityRegistry` 组件

### move_to 超时（玩家不移动）

- 确认 MVPlayer 上有 `NavMeshAgent` 组件且已启用
- 确认 NavMesh 已烘焙：`Tools` → `Bake NavMesh Surface`
- 确认 `Application.runInBackground` 为 true（MCPGateway.Awake 中自动设置）

## 7. 项目结构概览

```
├── Assets/
│   ├── Scripts/              # 游戏脚本
│   │   ├── MCP/              # MCP 三层架构 (Gateway/Router/Executor)
│   │   ├── PlayerMovement.cs # 玩家移动（WASD + NavMeshAgent）
│   │   ├── IInteractable.cs  # 交互接口
│   │   ├── ToggleDoor.cs     # 门开关
│   │   ├── ToggleLight.cs    # 灯开关
│   │   └── ToggleCurtain.cs  # 窗帘开关
│   ├── Editor/               # 编辑器工具和测试
│   └── mvp_game.unity        # 主场景
├── e2e-tests/                # TypeScript 端到端测试
│   └── src/SystemTest/       # Play Mode 系统测试
├── CLAUDE.md                 # Claude Code 项目指令
└── SETUP.md                  # 本文档
```
