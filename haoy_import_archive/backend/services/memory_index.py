"""
FTS5 全文检索索引管理。

参考 OpenClaw 的 manager-search.ts 和 manager-sync-ops.ts：
  - 将 Markdown 文件切分为 chunk，写入 memory_chunks 表
  - 通过 trigger 自动同步到 memory_fts 虚拟表
  - 使用 FTS5 MATCH + bm25() 进行 BM25 关键词搜索
  - 结合时间衰减进行混合排序
"""

from __future__ import annotations

import hashlib
import logging
import re
from datetime import datetime
from pathlib import Path
from typing import Any

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from models.database import async_session
from services.memory_files import list_all_memory_files, _char_dir
from utils.text_utils import chunk_markdown, tokenize_for_fts, bm25_rank_to_score, temporal_decay

logger = logging.getLogger("services.memory_index")

_DATE_RE = re.compile(r"(\d{4}-\d{2}-\d{2})")


def _chunk_id(character_id: str, source_path: str, start_line: int) -> str:
    raw = f"{character_id}:{source_path}:{start_line}"
    return hashlib.md5(raw.encode()).hexdigest()[:16]


def _extract_date_from_path(source_path: str) -> str | None:
    """从路径中提取日期，如 memory/2026-03-26.md -> 2026-03-26"""
    m = _DATE_RE.search(source_path)
    return m.group(1) if m else None


class MemoryIndex:
    """FTS5 索引管理：同步、搜索、维护。"""

    async def sync_character(self, character_id: str):
        """
        将角色的所有 Markdown 文件同步到 FTS5 索引。
        全量重建策略：删除旧 chunk，重新切分写入。
        """
        files = list_all_memory_files(character_id)
        if not files:
            return

        char_dir = _char_dir(character_id)
        all_chunks: list[dict] = []

        for file_path in files:
            content = file_path.read_text(encoding="utf-8")
            rel_path = str(file_path.relative_to(char_dir))
            chunks = chunk_markdown(content, source_path=rel_path)
            for chunk in chunks:
                cid = _chunk_id(character_id, rel_path, chunk["start_line"])
                tokenized = tokenize_for_fts(chunk["text"])
                if not tokenized.strip():
                    continue
                all_chunks.append({
                    "chunk_id": cid,
                    "character_id": character_id,
                    "source_path": rel_path,
                    "section": chunk.get("section", ""),
                    "content": chunk["text"],
                    "content_tokenized": tokenized,
                    "start_line": chunk["start_line"],
                    "end_line": chunk["end_line"],
                })

        async with async_session() as session:
            await session.execute(
                text("DELETE FROM memory_chunks WHERE character_id = :cid"),
                {"cid": character_id},
            )
            for chunk in all_chunks:
                await session.execute(
                    text("""
                        INSERT INTO memory_chunks
                            (chunk_id, character_id, source_path, section,
                             content, content_tokenized, start_line, end_line)
                        VALUES
                            (:chunk_id, :character_id, :source_path, :section,
                             :content, :content_tokenized, :start_line, :end_line)
                    """),
                    chunk,
                )
            await session.commit()

        logger.info("已同步 %s 的 %d 个 chunk 到 FTS 索引", character_id, len(all_chunks))

    async def sync_single_file(self, character_id: str, file_path: Path):
        """增量同步单个文件：删除该文件旧 chunk，重新写入。"""
        char_dir = _char_dir(character_id)
        rel_path = str(file_path.relative_to(char_dir))

        content = file_path.read_text(encoding="utf-8")
        chunks = chunk_markdown(content, source_path=rel_path)
        new_chunks: list[dict] = []
        for chunk in chunks:
            cid = _chunk_id(character_id, rel_path, chunk["start_line"])
            tokenized = tokenize_for_fts(chunk["text"])
            if not tokenized.strip():
                continue
            new_chunks.append({
                "chunk_id": cid,
                "character_id": character_id,
                "source_path": rel_path,
                "section": chunk.get("section", ""),
                "content": chunk["text"],
                "content_tokenized": tokenized,
                "start_line": chunk["start_line"],
                "end_line": chunk["end_line"],
            })

        async with async_session() as session:
            await session.execute(
                text("DELETE FROM memory_chunks WHERE character_id = :cid AND source_path = :sp"),
                {"cid": character_id, "sp": rel_path},
            )
            for chunk in new_chunks:
                await session.execute(
                    text("""
                        INSERT INTO memory_chunks
                            (chunk_id, character_id, source_path, section,
                             content, content_tokenized, start_line, end_line)
                        VALUES
                            (:chunk_id, :character_id, :source_path, :section,
                             :content, :content_tokenized, :start_line, :end_line)
                    """),
                    chunk,
                )
            await session.commit()

    async def search(
        self,
        character_id: str,
        query: str,
        max_results: int = 10,
        min_score: float = 0.05,
    ) -> list[dict]:
        """
        使用 FTS5 BM25 搜索 + 时间衰减排序。
        参考 OpenClaw manager-search.ts 的 searchKeyword + mergeHybridResults。
        """
        tokenized_query = tokenize_for_fts(query)
        if not tokenized_query.strip():
            return []

        fts_terms = " OR ".join(tokenized_query.split())

        async with async_session() as session:
            result = await session.execute(
                text("""
                    SELECT c.chunk_id, c.character_id, c.source_path, c.section,
                           c.content, c.start_line, c.end_line, c.created_at,
                           bm25(memory_fts) AS rank
                    FROM memory_fts f
                    JOIN memory_chunks c ON c.rowid = f.rowid
                    WHERE memory_fts MATCH :query
                      AND c.character_id = :cid
                    ORDER BY rank ASC
                    LIMIT :limit
                """),
                {"query": fts_terms, "cid": character_id, "limit": max_results * 3},
            )
            rows = result.fetchall()

        scored: list[dict] = []
        for row in rows:
            bm25_score = bm25_rank_to_score(row.rank)

            date_str = _extract_date_from_path(row.source_path)
            if date_str:
                try:
                    file_dt = datetime.fromisoformat(date_str)
                    decay = temporal_decay(file_dt, half_life_days=30.0)
                except ValueError:
                    decay = 0.5
            else:
                decay = 1.0  # MEMORY.md 不衰减

            is_memory_md = row.source_path == "MEMORY.md"
            source_boost = 1.2 if is_memory_md else 1.0

            final_score = bm25_score * decay * source_boost

            if final_score < min_score:
                continue

            scored.append({
                "chunk_id": row.chunk_id,
                "character_id": row.character_id,
                "source_path": row.source_path,
                "section": row.section,
                "content": row.content,
                "start_line": row.start_line,
                "end_line": row.end_line,
                "bm25_score": round(bm25_score, 4),
                "decay": round(decay, 4),
                "score": round(final_score, 4),
            })

        scored.sort(key=lambda x: x["score"], reverse=True)
        return scored[:max_results]

    async def delete_character(self, character_id: str):
        """删除角色的全部索引 chunk。"""
        async with async_session() as session:
            await session.execute(
                text("DELETE FROM memory_chunks WHERE character_id = :cid"),
                {"cid": character_id},
            )
            await session.commit()
