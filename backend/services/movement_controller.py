"""
移动决策模块：为角色生成自主移动行为。

设计原则（参考 Claw3D eventTriggers + Unity MoveToHandler）：
  - 后端只负责决策「去哪里」，通过 WebSocket 事件推送给 Unity
  - Unity 的 NavMeshAgent 负责实际寻路和碰撞
  - 兼容现有 move_to 工具的 target_id 协议
"""

from __future__ import annotations

import asyncio
import random
import logging
from typing import Any

from ws.protocol import EventFrame
from services.character_manager import CharacterManager
import config

logger = logging.getLogger("services.movement")


class MovementController:
    """角色自主移动控制器，周期性地为空闲角色生成移动决策。"""

    def __init__(self, character_manager: CharacterManager):
        self.char_mgr = character_manager
        self._ws_manager = None  # 延迟注入
        self._task: asyncio.Task | None = None
        self._running = False
        # Unity 场景中可用的导航目标（由 Unity 上报或在此预配置）
        self._scene_targets: list[dict[str, Any]] = []
        # 角色行为状态：character_id -> {"busy_until": float, "last_target": str}
        self._behavior_state: dict[str, dict] = {}

    def set_ws_manager(self, ws_manager):
        self._ws_manager = ws_manager

    def update_scene_targets(self, targets: list[dict[str, Any]]):
        """Unity 上报场景中可用的导航目标列表。"""
        self._scene_targets = targets
        logger.info("场景目标已更新: %d 个目标", len(targets))

    # ── 启动 / 停止 ────────────────────────────────

    def start(self):
        if self._running:
            return
        self._running = True
        self._task = asyncio.create_task(self._behavior_loop())
        logger.info("移动决策循环已启动 (间隔=%ds)", config.AUTO_BEHAVIOR_INTERVAL)

    def stop(self):
        self._running = False
        if self._task:
            self._task.cancel()
            self._task = None
            logger.info("移动决策循环已停止")

    # ── 核心决策循环 ────────────────────────────────

    async def _behavior_loop(self):
        while self._running:
            try:
                await self._tick()
            except asyncio.CancelledError:
                break
            except Exception as exc:
                logger.exception("决策循环异常: %s", exc)
            await asyncio.sleep(config.AUTO_BEHAVIOR_INTERVAL)

    async def _tick(self):
        """每个周期为所有空闲角色做一次行为决策。"""
        if not self._ws_manager or not self._scene_targets:
            return

        characters = await self.char_mgr.list_all()
        for char in characters:
            cid = char["id"]
            status = char.get("status", "idle")

            if status not in ("idle", "standing"):
                continue

            action = self._decide_action(cid, char)
            if action:
                await self._execute_action(cid, action)

    def _decide_action(self, character_id: str, char_data: dict) -> dict | None:
        """
        为角色选择下一个行为。返回动作描述字典或 None（继续空闲）。
        """
        state = self._behavior_state.get(character_id, {})

        # 30% 概率保持原地不动（模拟发呆、思考）
        if random.random() < 0.3:
            return None

        if not self._scene_targets:
            return None

        # 排除上次去过的目标，避免反复走同一条路
        last_target = state.get("last_target", "")
        candidates = [
            t for t in self._scene_targets if t.get("entity_id") != last_target
        ]
        if not candidates:
            candidates = self._scene_targets

        target = random.choice(candidates)

        self._behavior_state[character_id] = {
            "last_target": target.get("entity_id", ""),
        }

        return {
            "type": "move_to",
            "character_id": character_id,
            "target_id": target.get("entity_id", ""),
            "target_name": target.get("display_name", ""),
        }

    async def _execute_action(self, character_id: str, action: dict):
        """将决策作为事件推送给 Unity。"""
        await self.char_mgr.update_status(character_id, "walking")

        event = EventFrame(
            event="character_move",
            data={
                "character_id": character_id,
                "target_id": action["target_id"],
                "target_name": action.get("target_name", ""),
                "action": "move_to",
            },
        )
        await self._ws_manager.broadcast_event(event)
        logger.info(
            "角色 %s 移动到 %s (%s)",
            character_id,
            action["target_id"],
            action.get("target_name", ""),
        )

    # ── Unity 上报事件处理 ──────────────────────────

    async def on_movement_completed(self, character_id: str, position: dict):
        """Unity 上报角色已到达目标。"""
        await self.char_mgr.update_status(character_id, "idle")
        await self.char_mgr.update_position(character_id, position)
        logger.info("角色 %s 已到达目标位置", character_id)

    async def on_movement_failed(self, character_id: str, error: str):
        """Unity 上报角色移动失败。"""
        await self.char_mgr.update_status(character_id, "idle")
        logger.warning("角色 %s 移动失败: %s", character_id, error)
