# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Unity 6 (URP) project — a top-down interior game where the player walks around a low-poly room, interacting with objects, NPCs, and items. The frontend connects to a Python backend via WebSocket for AI-agent-driven gameplay (NPC dialogue, character management, memory). Uses the new Input System (keyboard WASD + mouse click / E key).

## Key Packages

- **Render pipeline:** Universal Render Pipeline (URP 17.3)
- **Input:** Unity Input System 1.19 (reads `Keyboard.current` / `Mouse.current` directly)
- **3D model import:** Tripo3D Unity Bridge (local package at `~/Downloads/Tripo3d_Unity_Bridge`)
- **MCP integration:** `com.coplaydev.unity-mcp` for editor automation via Claude
- **Navigation:** AI Navigation 2.0 — NavMesh-based pathfinding for player and NPCs
- **glTF:** com.unity.cloud.gltfast 6.16

## Architecture

All gameplay scripts live in `Assets/Scripts/`. The codebase has grown beyond the basic MVP into several interconnected systems:

### Core Interaction Pattern

- `IInteractable` — interface with `Interact()`, `GetPromptText()`, `GetState()`. Any interactable object implements this.
- `PlayerInteraction` — raycasts on mouse click and `OverlapSphere` on E key to find nearest `IInteractable`.
- `ToggleDoor`, `ToggleCurtain`, `ToggleLight` — concrete `IInteractable` implementations. Doors rotate via a **parent pivot object**.
- `PlayerMovement` — Rigidbody-based, camera-relative WASD movement with NavMeshAgent integration.
- `CameraFollow` — fixed top-down camera that tracks the player with configurable offset/rotation.

### MCP Three-Layer Architecture (`Assets/Scripts/MCP/`)

The MCP system enables a Python backend agent to observe and control the game world via WebSocket:

```
Gateway (MCPGateway, WebSocketTransport, OutboundRequestManager, BackendEventDispatcher)
  ↓  Structure validation, whitelist, QPS throttling (10/s), WebSocket I/O
Router (MCPRouter, ToolRegistry, ToolDefinition, ParameterNormalizer)
  ↓  Tool lookup, parameter normalization, semantic target resolution
Executor (Action + Query handlers)
  ├── Queries (parallel, immediate): get_player_state, get_world_summary, get_nearby_entities, get_inventory
  └── Actions (exclusive, async, last-write-wins): move_to, interact_with, use_tool_on, talk_to_npc, equip_item
```

- **Entity System** (`MCP/Entity/`): `EntityRegistry` auto-discovers all `IInteractable` objects. `SemanticResolver` resolves target names via three-tier search (exact ID → name/alias → substring).
- **Gateway** supports bidirectional communication: inbound requests from backend, outbound requests with callback pairing, and event dispatch (`action_completed`, `action_failed`, `action_cancelled`).
- **WebSocketTransport**: auto-reconnect (3s), heartbeat (30s ping), thread-safe concurrent queues. Default URL: `ws://localhost:8765/ws`.

### NPC / Dialogue System (`Assets/Scripts/NPC/`)

- `NpcController` — state machine (Idle, Walking, Talking), implements `IInteractable`, NavMeshAgent movement, backend-driven dialogue.
- `NpcRegistry` — singleton tracking all NPCs by `characterId`, handles backend `character_state_changed` events.

### Item / Inventory System (`Assets/Scripts/Item/`)

- `Item` — data class with id, displayName, type (Tool/Consumable/KeyItem/Material), quantity, usable/equippable flags.
- `ItemRegistry` — singleton managing inventory. Default items: wrench, shovel, postcard.
- `IItemAction` — interface for item action behaviors.

### UI System (`Assets/Scripts/UI/`)

- `UIManager` — singleton managing panel lifecycle with exclusive panel blocking.
- `IUIPanel` — interface for panel behavior (`IsExclusive`, `IsOpen`, `Open()`, `Close()`).
- **Panels**: `NpcDialogueUI` (typewriter effect, conversation history), `InventoryUI` (Tab toggle), `CharacterCreationUI` (F2, NPC spawning), `MemoryInputUI` (F3, importance slider 1-10).

## MockBackend (`MockBackend/`)

Python async WebSocket server simulating the AI agent backend. Tech stack: Python 3.11+, `websockets`, pytest, uv.

- Server: `MockBackend/mock_server/server.py` — listens on `ws://localhost:8765/ws`
- Frame protocol: `req` (request), `res` (response), `event` (bidirectional)
- Tests: `MockBackend/tests/` — protocol tests, playmode action/query tests

## Testing

Tests are organized into three tiers:

### Unit Tests (`Assets/Editor/Tests/`)

NUnit via Unity Test Framework. Run from Unity **Window > General > Test Runner** or via MCP `run_tests`.

- `ItemTests.cs`, `ItemRegistryTests.cs` — item system
- `UIManagerTests.cs` — UI panel management
- `PlayerMovementTests.cs` — movement basics
- `MCP/` — comprehensive MCP tests: CoreTests, EntityTests, SemanticResolverTests, ActionHandlerTests, ExecutorTests, RouterTests, RouterFlowTests, GatewayTests, IntegrationTests

### System Integration Tests (`Assets/Editor/Tests/SystemTest/`)

End-to-end flows using `MockTransport` (in-memory WebSocket mock):

- `EventDispatchTests.cs` — event handler registration, action completion monitoring
- `GatewayFlowTests.cs` — full query/action/event flows through gateway
- `OutboundRequestTests.cs` — request/response pairing, timeouts, cancellation

### E2E Tests (`e2e-tests/`)

TypeScript vitest suite testing real MCP communication:

- `00-preflight.test.ts` — MCP connectivity, Play Mode, EntityRegistry verification
- `01-door-interaction.test.ts` — full door interaction flow

## Asset Sources

- `Assets/LowPolyInterior/` — low-poly interior art pack (prefabs, models, textures, materials)
- `Assets/TripoModels/` — AI-generated 3D models from Tripo
- `Assets/Screenshots/` — captured screenshots

## Working with This Project via MCP

When using Unity MCP tools:
- After creating/modifying scripts, always call `read_console` to check for compilation errors before proceeding.
- The main scene is `mvp_game`. Use `manage_scene` to query hierarchy.
- New interactable objects should implement `IInteractable` and be placed on a layer included in `PlayerInteraction.interactLayer`.
- Doors require a pivot parent: create an empty GameObject as pivot, put the door mesh as a child, attach `ToggleDoor` to the child.
- NPCs need `NpcController` + `EntityIdentity` components and a `NavMeshAgent`. Register with `NpcRegistry`.
- Items are managed through `ItemRegistry`. New items need an `ItemType` and should be registered at startup.

## Key Documentation

- `SETUP.md` — team onboarding (environment setup, MCP verification, NavMesh, E2E test instructions)
- `Docs/unity_frontend_adaptation_plan.md` — WebSocket integration design
- `Assets/Scripts/MCP/MCP_Agent_Design.md` — MCP three-layer architecture spec
