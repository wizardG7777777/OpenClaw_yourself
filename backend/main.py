"""
CyberEternity 后端主入口（v3）。

新增功能：角色间互动、定期反思、全局用户档案。
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
from services.dialogue_engine import DialogueEngine, set_reflection_engine
from services.movement_controller import MovementController
from services.character_interaction import CharacterInteraction
from services.reflection_engine import ReflectionEngine
from services.user_profile import (
    init_user_profile, read_user_profile, update_user_profile, profile_exists,
)

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
interaction = CharacterInteraction(char_mgr, mem_mgr)
reflection = ReflectionEngine(char_mgr, mem_mgr)


# ══════════════════════════════════════════════════════
#  WebSocket 方法处理器
# ══════════════════════════════════════════════════════

async def handle_ping(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    return {"pong": True}


# ── 角色管理 ────────────────────────────────────────

async def handle_create_character(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    result = await char_mgr.create(params)
    await mem_mgr.init_character_memory(result["id"], params)
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
    section = params.get("section")
    if not content:
        raise ValueError("记忆内容不能为空")
    return await mem_mgr.add_core_memory(character_id, content, section)


async def handle_get_memories(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    character_id = params.get("character_id", "")
    return {
        "long_term": mem_mgr.get_long_term_memory(character_id),
        "recent": mem_mgr.get_recent_memories(character_id),
        "timeline": mem_mgr.get_memory_timeline(character_id),
    }


async def handle_search_memories(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    character_id = params.get("character_id", "")
    query = params.get("query", "")
    max_results = params.get("max_results", 10)
    results = await mem_mgr.search_memories(character_id, query, max_results)
    return {"memories": results}


async def handle_delete_memory(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    return {"message": "请使用 update_memory_file 直接编辑 MEMORY.md"}


async def handle_get_memory_file(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    character_id = params.get("character_id", "")
    file_type = params.get("file_type", "long_term")
    date_str = params.get("date", "")
    content = mem_mgr.get_memory_file(character_id, file_type, date_str)
    return {"content": content, "file_type": file_type}


async def handle_update_memory_file(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    character_id = params.get("character_id", "")
    content = params.get("content", "")
    if not content:
        raise ValueError("内容不能为空")
    ok = await mem_mgr.update_memory_file(character_id, content)
    return {"updated": ok}


async def handle_get_memory_timeline(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    character_id = params.get("character_id", "")
    timeline = mem_mgr.get_memory_timeline(character_id)
    return {"timeline": timeline}


async def handle_rebuild_index(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    character_id = params.get("character_id", "")
    await mem_mgr.rebuild_index(character_id)
    return {"rebuilt": True}


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
        return {"message": "记忆不足，暂不需要归纳"}
    return result


# ── 角色间互动 ──────────────────────────────────────

async def handle_trigger_interaction(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    """手动触发两个角色互动。"""
    char_a = params.get("character_a_id", "")
    char_b = params.get("character_b_id", "")
    if not char_a or not char_b:
        raise ValueError("需要提供两个角色 ID")
    return await interaction.trigger_interaction(char_a, char_b)


# ── 反思 ────────────────────────────────────────────

async def handle_trigger_reflection(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    """手动触发角色反思。"""
    character_id = params.get("character_id", "")
    result = await reflection.run_reflection(character_id)
    if result is None:
        return {"message": "今天活动不足，无法反思"}
    return result


async def handle_get_activity(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    """获取角色的活动状态。"""
    character_id = params.get("character_id", "")
    return reflection.get_activity(character_id)


# ── 用户档案 ────────────────────────────────────────

async def handle_set_user_profile(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    """创建或更新用户档案。"""
    nickname = params.get("nickname", "")
    basic_info = params.get("basic_info", "")
    if not nickname:
        raise ValueError("用户昵称不能为空")
    if profile_exists():
        content = params.get("content", "")
        if content:
            update_user_profile(content)
            return {"updated": True}
    content = init_user_profile(nickname, basic_info)
    return {"created": True, "content": content}


async def handle_get_user_profile(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    """读取用户档案。"""
    return {"content": read_user_profile(), "exists": profile_exists()}


# ── 移动相关 ────────────────────────────────────────

async def handle_update_scene_targets(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    targets = params.get("targets", [])
    movement.update_scene_targets(targets)
    return {"accepted": True, "target_count": len(targets)}


async def handle_movement_completed(mgr: WebSocketManager, cid: str, params: dict) -> Any:
    character_id = params.get("character_id", "")
    position = params.get("position", {"x": 0, "y": 0, "z": 0})
    await movement.on_movement_completed(character_id, position)
    return {"acknowledged": True}


async def handle_movement_failed(mgr: WebSocketManager, cid: str, params: dict) -> Any:
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
    ws_manager.register("get_memory_file", handle_get_memory_file)
    ws_manager.register("update_memory_file", handle_update_memory_file)
    ws_manager.register("get_memory_timeline", handle_get_memory_timeline)
    ws_manager.register("rebuild_index", handle_rebuild_index)
    # 对话
    ws_manager.register("talk_to_character", handle_talk_to_character)
    ws_manager.register("summarize_memories", handle_summarize_memories)
    # 角色互动
    ws_manager.register("trigger_interaction", handle_trigger_interaction)
    # 反思
    ws_manager.register("trigger_reflection", handle_trigger_reflection)
    ws_manager.register("get_activity", handle_get_activity)
    # 用户档案
    ws_manager.register("set_user_profile", handle_set_user_profile)
    ws_manager.register("get_user_profile", handle_get_user_profile)
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
    # 注入依赖
    set_reflection_engine(reflection)
    movement.set_ws_manager(ws_manager)
    interaction.set_ws_manager(ws_manager)
    # 启动后台 task
    movement.start()
    interaction.start()
    reflection.start()
    logger.info("CyberEternity 后端 v3 已启动 — ws://%s:%d/ws", config.HOST, config.WS_PORT)
    yield
    movement.stop()
    interaction.stop()
    reflection.stop()
    logger.info("CyberEternity 后端已关闭")


app = FastAPI(title="CyberEternity Backend", version="0.3.0", lifespan=lifespan)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.get("/health")
async def health():
    return {"status": "ok", "service": "CyberEternity", "version": "0.3.0"}


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
