"""
文本处理工具：关键词提取、BM25 分数转换、时间衰减、Markdown chunk 切分。
使用 jieba 分词支持中文。
"""

from __future__ import annotations

import math
from datetime import datetime, timezone

import jieba
import jieba.analyse

jieba.initialize()

_CN_STOPWORDS = frozenset(
    "的 了 是 在 我 有 和 就 不 人 都 一 一个 上 也 很 到 说 要 去 你 会 着 没有 看 好 "
    "自己 这 他 她 它 们 那 里 为 什么 吗 吧 呢 啊 哦 嗯 呀 把 被 让 给 从 但 而 "
    "与 及 或 如果 因为 所以 虽然 但是 然后 可以 这个 那个 已经 还是 只是 不是".split()
)


# ══════════════════════════════════════════════════════
#  关键词提取
# ══════════════════════════════════════════════════════

def extract_keywords(text: str, top_k: int = 8) -> list[str]:
    if not text or not text.strip():
        return []
    keywords = jieba.analyse.extract_tags(text, topK=top_k, withWeight=False)
    if len(keywords) < 3:
        words = jieba.lcut(text)
        for w in words:
            w = w.strip()
            if len(w) >= 2 and w not in _CN_STOPWORDS and w not in keywords:
                keywords.append(w)
            if len(keywords) >= top_k:
                break
    return keywords


def tokenize_for_fts(text: str) -> str:
    """将中文文本切分为空格分隔的词，供 FTS5 索引使用。"""
    words = jieba.lcut(text)
    return " ".join(w.strip() for w in words if w.strip() and w not in _CN_STOPWORDS)


# ══════════════════════════════════════════════════════
#  BM25 分数与混合排序（参考 OpenClaw hybrid.ts）
# ══════════════════════════════════════════════════════

def bm25_rank_to_score(rank: float) -> float:
    """
    将 SQLite FTS5 bm25() 返回的 rank 转换为 0~1 分数。
    FTS5 的 bm25 rank 越小（越负）表示越相关。
    参考 OpenClaw src/memory/hybrid.ts bm25RankToScore。
    """
    if not math.isfinite(rank):
        return 1.0 / (1.0 + 999.0)
    if rank < 0:
        relevance = -rank
        return relevance / (1.0 + relevance)
    return 1.0 / (1.0 + rank)


def temporal_decay(created_at: datetime | str, half_life_days: float = 30.0) -> float:
    """
    时间衰减因子：越近的记忆得分越高。
    使用指数衰减，half_life_days 为半衰期天数。
    """
    if isinstance(created_at, str):
        try:
            created_at = datetime.fromisoformat(created_at)
        except (ValueError, TypeError):
            return 0.5
    now = datetime.now(timezone.utc)
    if created_at.tzinfo is None:
        created_at = created_at.replace(tzinfo=timezone.utc)
    age_days = max(0, (now - created_at).total_seconds() / 86400)
    return math.exp(-0.693 * age_days / half_life_days)


# ══════════════════════════════════════════════════════
#  Markdown chunk 切分（参考 OpenClaw internal.ts chunkMarkdown）
# ══════════════════════════════════════════════════════

def chunk_markdown(content: str, source_path: str = "") -> list[dict]:
    """
    将 Markdown 文件按 ## 标题切分为 chunk 列表。
    每个 chunk: {text, source_path, start_line, end_line, section}

    对于 MEMORY.md，每个 ## 区段成为一个 chunk。
    对于每日记忆文件，每个时间段（## 下午3:15）成为一个 chunk。
    """
    if not content.strip():
        return []

    lines = content.split("\n")
    chunks: list[dict] = []
    current_section = ""
    current_lines: list[str] = []
    current_start = 1

    for i, line in enumerate(lines, start=1):
        if line.startswith("## "):
            if current_lines and any(l.strip() for l in current_lines):
                text = "\n".join(current_lines).strip()
                if text:
                    chunks.append({
                        "text": text,
                        "source_path": source_path,
                        "start_line": current_start,
                        "end_line": i - 1,
                        "section": current_section,
                    })
            current_section = line[3:].strip()
            current_lines = [line]
            current_start = i
        elif line.startswith("# ") and not current_section:
            current_lines.append(line)
        else:
            current_lines.append(line)

    if current_lines and any(l.strip() for l in current_lines):
        text = "\n".join(current_lines).strip()
        if text:
            chunks.append({
                "text": text,
                "source_path": source_path,
                "start_line": current_start,
                "end_line": len(lines),
                "section": current_section,
            })

    return chunks
