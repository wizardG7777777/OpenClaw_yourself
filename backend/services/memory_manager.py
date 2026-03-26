"""
记忆管理服务：添加、检索、摘要化记忆。

记忆分三层（参考 OpenClaw memory 设计）：
  - core:         用户主动输入的关于角色的回忆
  - conversation:  与角色对话过程中自动产生的记录
  - summary:       由 LLM 定期归纳的长期记忆
"""

from __future__ import annotations

import uuid
import logging
from typing import Any

from sqlalchemy import select, and_
from sqlalchemy.ext.asyncio import AsyncSession

from models.database import async_session
from models.memory import Memory
from utils.text_utils import extract_keywords, compute_relevance
import config

logger = logging.getLogger("services.memory")


def _new_id() -> str:
    return f"mem_{uuid.uuid4().hex[:8]}"


class MemoryManager:
    """记忆 CRUD + 检索服务。"""

    # ── 添加记忆 ────────────────────────────────────

    async def add_memory(
        self,
        character_id: str,
        content: str,
        memory_type: str = "core",
        importance: int = 5,
    ) -> dict:
        keywords = extract_keywords(content)
        mem = Memory(
            id=_new_id(),
            character_id=character_id,
            content=content,
            memory_type=memory_type,
            importance=importance,
            keywords=",".join(keywords),
        )
        async with async_session() as session:
            session.add(mem)
            await session.commit()
            await session.refresh(mem)
            logger.info(
                "记忆已添加: %s (类型=%s, 角色=%s)", mem.id, memory_type, character_id
            )
            return mem.to_dict()

    async def add_conversation_memory(
        self, character_id: str, user_msg: str, char_reply: str
    ) -> dict:
        content = f"用户说: {user_msg}\n角色回复: {char_reply}"
        return await self.add_memory(character_id, content, "conversation", importance=3)

    # ── 查询记忆 ────────────────────────────────────

    async def get_memories(
        self,
        character_id: str,
        memory_type: str | None = None,
        limit: int = 50,
    ) -> list[dict]:
        async with async_session() as session:
            conditions = [Memory.character_id == character_id]
            if memory_type:
                conditions.append(Memory.memory_type == memory_type)
            result = await session.execute(
                select(Memory)
                .where(and_(*conditions))
                .order_by(Memory.created_at.desc())
                .limit(limit)
            )
            return [m.to_dict() for m in result.scalars().all()]

    # ── 记忆检索（基于关键词相关性） ────────────────

    async def search_memories(
        self,
        character_id: str,
        query: str,
        max_core: int | None = None,
        max_conversation: int | None = None,
    ) -> list[dict]:
        """
        根据 query 检索相关记忆，按相关性排序返回。
        核心记忆和对话记忆分别限制数量，最终合并返回。
        """
        if max_core is None:
            max_core = config.MAX_CORE_MEMORIES_PER_QUERY
        if max_conversation is None:
            max_conversation = config.MAX_CONVERSATION_HISTORY

        async with async_session() as session:
            result = await session.execute(
                select(Memory).where(Memory.character_id == character_id)
            )
            all_memories = result.scalars().all()

        scored: list[tuple[float, Memory]] = []
        for mem in all_memories:
            score = compute_relevance(query, mem.content)
            # 核心记忆额外加权
            if mem.memory_type == "core":
                score += 0.2
            # 重要性加权
            score += mem.importance * 0.02
            scored.append((score, mem))

        scored.sort(key=lambda x: x[0], reverse=True)

        core_results: list[dict] = []
        conv_results: list[dict] = []
        summary_results: list[dict] = []

        for score, mem in scored:
            d = mem.to_dict()
            d["relevance_score"] = round(score, 4)
            if mem.memory_type == "core" and len(core_results) < max_core:
                core_results.append(d)
            elif mem.memory_type == "conversation" and len(conv_results) < max_conversation:
                conv_results.append(d)
            elif mem.memory_type == "summary" and len(summary_results) < 3:
                summary_results.append(d)

        return summary_results + core_results + conv_results

    # ── 删除记忆 ────────────────────────────────────

    async def delete_memory(self, memory_id: str) -> bool:
        from sqlalchemy import delete as sql_delete
        async with async_session() as session:
            result = await session.execute(
                sql_delete(Memory).where(Memory.id == memory_id)
            )
            await session.commit()
            return result.rowcount > 0

    async def delete_character_memories(self, character_id: str) -> int:
        from sqlalchemy import delete as sql_delete
        async with async_session() as session:
            result = await session.execute(
                sql_delete(Memory).where(Memory.character_id == character_id)
            )
            await session.commit()
            return result.rowcount
