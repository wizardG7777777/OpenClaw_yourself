"""
记忆管理服务（v2）—— 两层 Markdown + FTS5 混合架构。

存储真源为 Markdown 文件（参考 OpenClaw memory 设计）：
  - MEMORY.md          长期记忆（结构化分区）
  - memory/YYYY-MM-DD.md  每日记忆（带时间戳）

索引层为 SQLite FTS5，通过 BM25 + 时间衰减实现高效检索。
"""

from __future__ import annotations

import logging
from typing import Any
from pathlib import Path

from services.memory_files import (
    init_memory_md,
    read_memory_md,
    parse_sections,
    append_to_section,
    update_memory_md,
    append_daily_memory,
    read_daily_memory,
    read_recent_daily_memories,
    list_daily_files,
    list_all_memory_files,
    delete_character_files,
    _memory_md_path,
    _daily_path,
    _char_dir,
    SECTION_ABOUT_USER,
    SECTION_PREFERENCES,
    SECTION_EVENTS,
    SECTION_LEARNED,
)
from services.memory_index import MemoryIndex
import config

logger = logging.getLogger("services.memory")


class MemoryManager:
    """统一记忆 API：文件层 + 索引层。"""

    def __init__(self):
        self.index = MemoryIndex()

    # ══════════════════════════════════════════════════
    #  角色记忆初始化
    # ══════════════════════════════════════════════════

    async def init_character_memory(self, character_id: str, char_data: dict):
        """角色创建时初始化 MEMORY.md 并建索引。"""
        init_memory_md(character_id, char_data)
        await self.index.sync_character(character_id)
        logger.info("角色 %s 记忆系统已初始化", character_id)

    # ══════════════════════════════════════════════════
    #  核心记忆写入（用户主动输入）
    # ══════════════════════════════════════════════════

    async def add_core_memory(
        self,
        character_id: str,
        content: str,
        section: str | None = None,
    ) -> dict:
        """
        添加核心记忆到 MEMORY.md 的指定区段。
        默认写入"重要事件"区段。
        """
        if section is None:
            section = SECTION_EVENTS

        lines = [l.strip() for l in content.split("\n") if l.strip()]
        append_to_section(character_id, section, lines)

        md_path = _memory_md_path(character_id)
        if md_path.exists():
            await self.index.sync_single_file(character_id, md_path)

        logger.info("核心记忆已添加到 %s [%s]: %s...", character_id, section, content[:30])
        return {
            "character_id": character_id,
            "section": section,
            "content": content,
            "type": "core",
        }

    # ══════════════════════════════════════════════════
    #  对话记忆写入（每轮对话自动产生）
    # ══════════════════════════════════════════════════

    async def add_conversation_memory(
        self,
        character_id: str,
        user_msg: str,
        char_reply: str,
    ) -> dict:
        """将一轮对话记录追加到当日记忆文件。"""
        entries = [
            f"用户说: {user_msg}",
            f"角色回复: {char_reply}",
        ]
        rel_path = append_daily_memory(character_id, entries)

        daily_path = _char_dir(character_id) / rel_path
        if daily_path.exists():
            await self.index.sync_single_file(character_id, daily_path)

        return {
            "character_id": character_id,
            "file": rel_path,
            "type": "conversation",
        }

    # ══════════════════════════════════════════════════
    #  对话自动学习（LLM 提取新信息写入长期记忆）
    # ══════════════════════════════════════════════════

    async def learn_from_conversation(
        self,
        character_id: str,
        new_facts: list[str],
    ):
        """
        将 LLM 从对话中提取的新信息追加到 MEMORY.md 的
        「从对话中学到的新信息」区段。
        """
        if not new_facts:
            return

        append_to_section(character_id, SECTION_LEARNED, new_facts)

        md_path = _memory_md_path(character_id)
        if md_path.exists():
            await self.index.sync_single_file(character_id, md_path)

        logger.info("角色 %s 从对话中学到 %d 条新信息", character_id, len(new_facts))

    # ══════════════════════════════════════════════════
    #  记忆检索（FTS5 BM25 + 时间衰减）
    # ══════════════════════════════════════════════════

    async def search_memories(
        self,
        character_id: str,
        query: str,
        max_results: int = 10,
    ) -> list[dict]:
        """使用 FTS5 BM25 搜索相关记忆片段。"""
        return await self.index.search(
            character_id,
            query,
            max_results=max_results,
            min_score=config.MEMORY_FTS_MIN_SCORE,
        )

    # ══════════════════════════════════════════════════
    #  记忆读取（直接读 Markdown 文件）
    # ══════════════════════════════════════════════════

    def get_long_term_memory(self, character_id: str) -> str:
        """读取 MEMORY.md 全文。"""
        return read_memory_md(character_id)

    def get_recent_memories(self, character_id: str, days: int | None = None) -> str:
        """读取最近 N 天的每日记忆。"""
        if days is None:
            days = config.MEMORY_DAILY_INJECT_DAYS
        return read_recent_daily_memories(character_id, days)

    def get_daily_memory(self, character_id: str, date_str: str) -> str:
        """读取指定日期的记忆文件。"""
        return read_daily_memory(character_id, date_str)

    def get_memory_timeline(self, character_id: str) -> list[dict]:
        """获取角色的记忆时间线（所有每日文件列表）。"""
        return list_daily_files(character_id)

    def get_memory_file(self, character_id: str, file_type: str = "long_term", date_str: str = "") -> str:
        """读取指定记忆文件的内容。"""
        if file_type == "long_term":
            return read_memory_md(character_id)
        elif file_type == "daily" and date_str:
            return read_daily_memory(character_id, date_str)
        return ""

    # ══════════════════════════════════════════════════
    #  记忆编辑
    # ══════════════════════════════════════════════════

    async def update_memory_file(self, character_id: str, content: str) -> bool:
        """覆写 MEMORY.md（供前端 UI 直接编辑长期记忆）。"""
        ok = update_memory_md(character_id, content)
        if ok:
            md_path = _memory_md_path(character_id)
            await self.index.sync_single_file(character_id, md_path)
        return ok

    # ══════════════════════════════════════════════════
    #  清理
    # ══════════════════════════════════════════════════

    async def delete_character_memories(self, character_id: str):
        """删除角色的全部记忆（文件 + 索引）。"""
        await self.index.delete_character(character_id)
        delete_character_files(character_id)
        logger.info("角色 %s 的所有记忆已删除", character_id)

    async def rebuild_index(self, character_id: str):
        """重建角色的 FTS 索引（从 Markdown 文件重新同步）。"""
        await self.index.sync_character(character_id)
