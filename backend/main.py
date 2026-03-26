"""
CyberEternity 后端主入口。

启动 FastAPI 服务，注册 WebSocket 端点，绑定所有业务处理器。
使用方法：
    cd backend
    cp .env.example .env   # 编辑 .env 填入 LLM API Key
    pip install -r requirements.txt
    python main.py
"""

from __future__ import annotations

import uuid
import logging
import asyncio
from contextlib import asynccontextmanager
from typing import Any

import uvicorn
from fastapi import FastAPI, WebSocket
from fastapi.middleware.cors import CORSMiddleware

import config
from models.database import init_db
from ws.bridge import WebSocketManager
from ws.protocol import EventFrame
from services.character_manager import CharacterManager
from services.memory_manager import MemoryManager
from services.dialogue_engine import DialogueEngine
from services.movement_controller import MovementController

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(name)s] %(levelname)s: %(message)s",
)
logger = logging.getLogger("main")

# ── 全局服务实例 ────────────────────────────────────

ws_manager = WebSocketManager()
char_mgr = CharacterManager()
mem_mgr = MemoryManager()
dialogue = DialogueEngine(char_mgr, mem_mgr)
movement = MovementController(char_mgr)


# ══════════════════════════════════════════════════════
#  WebSocket 方法处理器（method -> handler）
# ══════════════════════════════════════════════════════

async def handle_ping(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    return {"pong": True}


# ── 角色管理 ────────────────────────────────────────

async def handle_create_character(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    result = await char_mgr.create(params)
    await mgr.broadcast_event(EventFrame(event="character_created", data=result))
    return result


async def handle_get_character(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    character_id = params.get("character_id", "")
    result = await char_mgr.get(character_id)
    if result is None:
        raise ValueError(f"角色不存在: {character_id}")
    return result


async def handle_list_characters(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    return {"characters": await char_mgr.list_all()}


async def handle_update_character(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    character_id = params.pop("character_id", "")
    result = await char_mgr.update(character_id, params)
    if result is None:
        raise ValueError(f"角色不存在: {character_id}")
    return result


async def handle_delete_character(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    character_id = params.get("character_id", "")
    await mem_mgr.delete_character_memories(character_id)
    deleted = await char_mgr.delete(character_id)
    if not deleted:
        raise ValueError(f"角色不存在: {character_id}")
    await mgr.broadcast_event(
        EventFrame(event="character_deleted", data={"character_id": character_id})
    )
    return {"deleted": True}


# ── 记忆管理 ────────────────────────────────────────

async def handle_add_memory(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    character_id = params.get("character_id", "")
    content = params.get("content", "")
    importance = params.get("importance", 5)
    if not content:
        raise ValueError("记忆内容不能为空")
    return await mem_mgr.add_memory(character_id, content, "core", importance)


async def handle_get_memories(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    character_id = params.get("character_id", "")
    memory_type = params.get("memory_type")
    limit = params.get("limit", 50)
    memories = await mem_mgr.get_memories(character_id, memory_type, limit)
    return {"memories": memories}


async def handle_search_memories(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    character_id = params.get("character_id", "")
    query = params.get("query", "")
    memories = await mem_mgr.search_memories(character_id, query)
    return {"memories": memories}


async def handle_delete_memory(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    memory_id = params.get("memory_id", "")
    deleted = await mem_mgr.delete_memory(memory_id)
    return {"deleted": deleted}


# ── 对话 ────────────────────────────────────────────

async def handle_talk_to_character(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    character_id = params.get("character_id", "")
    message = params.get("message", "")
    if not message:
        raise ValueError("对话消息不能为空")
    await char_mgr.update_status(character_id, "talking")
    result = await dialogue.chat(character_id, message)
    await char_mgr.update_status(character_id, "idle")
    return result


async def handle_summarize_memories(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    character_id = params.get("character_id", "")
    result = await dialogue.summarize_memories(character_id)
    if result is None:
        return {"message": "对话记忆不足，暂不需要归纳"}
    return result


# ── 移动相关 ────────────────────────────────────────

async def handle_update_scene_targets(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    """Unity 上报场景中可用的导航目标。"""
    targets = params.get("targets", [])
    movement.update_scene_targets(targets)
    return {"accepted": True, "target_count": len(targets)}


async def handle_movement_completed(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    """Unity 上报角色移动完成。"""
    character_id = params.get("character_id", "")
    position = params.get("position", {"x": 0, "y": 0, "z": 0})
    await movement.on_movement_completed(character_id, position)
    return {"acknowledged": True}


async def handle_movement_failed(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    """Unity 上报角色移动失败。"""
    character_id = params.get("character_id", "")
    error = params.get("error", "unknown")
    await movement.on_movement_failed(character_id, error)
    return {"acknowledged": True}


# ══════════════════════════════════════════════════════
#  注册所有处理器
# ══════════════════════════════════════════════════════

def _register_handlers():
    ws_manager.register("ping", handle_ping)
    # 角色
    ws_manager.register("create_character", handle_create_character)
    ws_manager.register("get_character", handle_get_character)
    ws_manager.register("list_characters", handle_list_characters)
    ws_manager.register("update_character", handle_update_character)
    ws_manager.register("delete_character", handle_delete_character)
    # 记忆
    ws_manager.register("add_memory", handle_add_memory)
    ws_manager.register("get_memories", handle_get_memories)
    ws_manager.register("search_memories", handle_search_memories)
    ws_manager.register("delete_memory", handle_delete_memory)
    # 对话
    ws_manager.register("talk_to_character", handle_talk_to_character)
    ws_manager.register("summarize_memories", handle_summarize_memories)
    # 移动
    ws_manager.register("update_scene_targets", handle_update_scene_targets)
    ws_manager.register("movement_completed", handle_movement_completed)
    ws_manager.register("movement_failed", handle_movement_failed)


# ══════════════════════════════════════════════════════
#  FastAPI 应用
# ══════════════════════════════════════════════════════

@asynccontextmanager
async def lifespan(app: FastAPI):
    await init_db()
    _register_handlers()
    movement.set_ws_manager(ws_manager)
    movement.start()
    logger.info("CyberEternity 后端已启动 — ws://%s:%d/ws", config.HOST, config.WS_PORT)
    yield
    movement.stop()
    logger.info("CyberEternity 后端已关闭")


app = FastAPI(title="CyberEternity Backend", version="0.1.0", lifespan=lifespan)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.get("/health")
async def health():
    return {"status": "ok", "service": "CyberEternity"}


@app.websocket("/ws")
async def websocket_endpoint(ws: WebSocket):
    connection_id = f"conn_{uuid.uuid4().hex[:8]}"
    await ws_manager.connect(connection_id, ws)
    await ws_manager.listen(connection_id, ws)


if __name__ == "__main__":
    uvicorn.run(
        "main:app",
        host=config.HOST,
        port=config.WS_PORT,
        log_level="info",
    )
