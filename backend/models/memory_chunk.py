"""
FTS5 索引 chunk 的数据结构定义。

memory_chunks 表不使用 ORM 映射（因为 FTS5 trigger 直接操作），
此文件只提供辅助函数和类型。
"""

from __future__ import annotations

from dataclasses import dataclass


@dataclass
class MemoryChunk:
    chunk_id: str
    character_id: str
    source_path: str
    section: str
    content: str
    content_tokenized: str
    start_line: int
    end_line: int

    def to_dict(self) -> dict:
        return {
            "chunk_id": self.chunk_id,
            "character_id": self.character_id,
            "source_path": self.source_path,
            "section": self.section,
            "content": self.content,
            "start_line": self.start_line,
            "end_line": self.end_line,
        }
