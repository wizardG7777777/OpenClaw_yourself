"""
角色间互动系统。

角色之间可以自主对话，就像真实家庭成员一样：
  - 不在用户面前时也会做自己的事、互相聊天
  - 每个角色的记忆完全独立
  - 互动对话分别写入各自的每日记忆文件

参考 OpenClaw 的 multi-agent 设计，简化为后端自主发起的角色间对话。
"""

from __future__ import annotations

import asyncio
import random
import logging
from typing import Any

from openai import AsyncOpenAI

import config
from services.character_manager import CharacterManager
from services.memory_manager import MemoryManager
from services.memory_files import append_daily_memory, _char_dir
from services.user_profile import read_user_profile
from ws.protocol import EventFrame

logger = logging.getLogger("services.interaction")

_THINK_TAG_RE_IMPORT = __import__("re").compile(r"<think>[\s\S]*?</think>\s*", __import__("re").IGNORECASE)


def _strip_think(text: str) -> str:
    return _THINK_TAG_RE_IMPORT.sub("", text).strip()


_INTERACTION_PROMPT_A = """你是「{name_a}」，{personality_a}。
你现在和「{name_b}」在同一个房间里。{name_b}是{relationship_b}。

{user_profile_context}

你的长期记忆中的关键信息：
{memory_a_brief}

请以「{name_a}」的身份，自然地对「{name_b}」说一句话。
可以是闲聊、关心、讨论用户、回忆过去、或者日常琐事。
只说一句话，不要太长，像家人之间的日常对话。"""

_INTERACTION_PROMPT_B = """你是「{name_b}」，{personality_b}。
「{name_a}」刚刚对你说："{utterance_a}"

{user_profile_context}

你的长期记忆中的关键信息：
{memory_b_brief}

请以「{name_b}」的身份自然地回复。
只说一句话，像家人之间的日常对话。"""


class CharacterInteraction:
    """角色间互动调度器与对话生成。"""

    def __init__(
        self,
        character_manager: CharacterManager,
        memory_manager: MemoryManager,
    ):
        self.char_mgr = character_manager
        self.mem_mgr = memory_manager
        self._ws_manager = None
        self._task: asyncio.Task | None = None
        self._running = False
        self.client = AsyncOpenAI(
            api_key=config.LLM_API_KEY or "no-key",
            base_url=config.LLM_BASE_URL,
        )

    def set_ws_manager(self, ws_manager):
        self._ws_manager = ws_manager

    def start(self):
        if self._running:
            return
        self._running = True
        self._task = asyncio.create_task(self._interaction_loop())
        logger.info(
            "角色互动调度已启动 (间隔=%ds, 概率=%.0f%%)",
            config.CHARACTER_INTERACTION_INTERVAL,
            config.CHARACTER_INTERACTION_PROBABILITY * 100,
        )

    def stop(self):
        self._running = False
        if self._task:
            self._task.cancel()
            self._task = None

    async def _interaction_loop(self):
        while self._running:
            try:
                await self._tick()
            except asyncio.CancelledError:
                break
            except Exception as exc:
                logger.exception("互动调度异常: %s", exc)
            await asyncio.sleep(config.CHARACTER_INTERACTION_INTERVAL)

    async def _tick(self):
        if not self._ws_manager:
            return

        if random.random() > config.CHARACTER_INTERACTION_PROBABILITY:
            return

        characters = await self.char_mgr.list_all()
        idle_chars = [c for c in characters if c.get("status") in ("idle", "standing")]

        if len(idle_chars) < 2:
            return

        pair = random.sample(idle_chars, 2)
        await self._run_interaction(pair[0], pair[1])

    async def _run_interaction(self, char_a: dict, char_b: dict):
        """执行一次两角色互动：A 说话 -> B 回复 -> 各自记忆。"""
        id_a, id_b = char_a["id"], char_b["id"]
        name_a, name_b = char_a["name"], char_b["name"]

        logger.info("角色互动开始: %s ↔ %s", name_a, name_b)

        await self.char_mgr.update_status(id_a, "talking")
        await self.char_mgr.update_status(id_b, "talking")

        try:
            mem_a = self.mem_mgr.get_long_term_memory(id_a)
            mem_b = self.mem_mgr.get_long_term_memory(id_b)
            user_profile = read_user_profile()
            user_ctx = f"你们共同认识的人——用户的信息：\n{user_profile[:500]}" if user_profile else ""

            # A 先说
            prompt_a = _INTERACTION_PROMPT_A.format(
                name_a=name_a,
                personality_a=char_a.get("personality", "温和"),
                name_b=name_b,
                relationship_b=char_b.get("relationship", "家人"),
                user_profile_context=user_ctx,
                memory_a_brief=mem_a[:800] if mem_a else "（暂无）",
            )
            utterance_a = await self._llm_generate(prompt_a)

            # B 回复
            prompt_b = _INTERACTION_PROMPT_B.format(
                name_b=name_b,
                personality_b=char_b.get("personality", "温和"),
                name_a=name_a,
                utterance_a=utterance_a,
                user_profile_context=user_ctx,
                memory_b_brief=mem_b[:800] if mem_b else "（暂无）",
            )
            utterance_b = await self._llm_generate(prompt_b)

            # 各自写入记忆（完全隔离）
            append_daily_memory(id_a, [
                f"和{name_b}聊天",
                f"我对{name_b}说: {utterance_a}",
                f"{name_b}回复: {utterance_b}",
            ])
            append_daily_memory(id_b, [
                f"和{name_a}聊天",
                f"{name_a}对我说: {utterance_a}",
                f"我回复: {utterance_b}",
            ])

            # 同步索引
            from services.memory_files import _daily_path
            from datetime import datetime
            today = datetime.now().strftime("%Y-%m-%d")
            daily_a = _daily_path(id_a, today)
            daily_b = _daily_path(id_b, today)
            if daily_a.exists():
                await self.mem_mgr.index.sync_single_file(id_a, daily_a)
            if daily_b.exists():
                await self.mem_mgr.index.sync_single_file(id_b, daily_b)

            # 广播事件给 Unity
            if self._ws_manager:
                await self._ws_manager.broadcast_event(EventFrame(
                    event="character_interaction",
                    data={
                        "character_a": {"id": id_a, "name": name_a, "utterance": utterance_a},
                        "character_b": {"id": id_b, "name": name_b, "utterance": utterance_b},
                    },
                ))

            logger.info("角色互动完成: %s说「%s」, %s回「%s」", name_a, utterance_a[:20], name_b, utterance_b[:20])

        except Exception as exc:
            logger.exception("角色互动失败: %s", exc)
        finally:
            await self.char_mgr.update_status(id_a, "idle")
            await self.char_mgr.update_status(id_b, "idle")

    async def _llm_generate(self, prompt: str) -> str:
        try:
            response = await self.client.chat.completions.create(
                model=config.LLM_MODEL,
                messages=[{"role": "user", "content": prompt}],
                temperature=0.9,
                max_tokens=150,
            )
            return _strip_think(response.choices[0].message.content)
        except Exception as exc:
            logger.warning("互动 LLM 调用失败: %s", exc)
            return "......"

    async def trigger_interaction(self, char_a_id: str, char_b_id: str) -> dict:
        """手动触发两个角色互动（供 WebSocket API 调用）。"""
        char_a = await self.char_mgr.get(char_a_id)
        char_b = await self.char_mgr.get(char_b_id)
        if not char_a or not char_b:
            raise ValueError("角色不存在")
        await self._run_interaction(char_a, char_b)
        return {"status": "interaction_completed", "characters": [char_a_id, char_b_id]}
